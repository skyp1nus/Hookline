using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record ChannelMappingDto(
    Guid Id, Guid SlackWorkspaceId, string SlackWorkspaceName,
    string SlackChannelId, string SlackChannelName,
    Guid GoogleAccountId, string GoogleAccountLabel,
    string? GoogleAccountAvatarUrl, string? GoogleAccountChannelId,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Lightweight routing record used by the ingest/status paths. <see cref="IsActive"/> is the gate
/// the ingest path reads to skip paused routes.</summary>
public sealed record MappingRoute(
    Guid Id, string SlackChannelId, string SlackChannelName, Guid GoogleAccountId, Guid SlackWorkspaceId, bool IsActive);

/// <summary>
/// Channel → account routing. Mappings are module-local; the workspace name and the account
/// label/avatar/channel are resolved through the shared Connections accessors (in-app, never a
/// cross-schema SQL join).
/// </summary>
public sealed class ChannelMappingService(
    YouTubeUploadsDbContext db,
    ISlackConnections workspaces,
    IGoogleConnections googleAccounts,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<ChannelMappingDto>> ListAsync(CancellationToken ct = default)
    {
        var mappings = await db.ChannelMappings.AsNoTracking()
            .OrderBy(m => m.SlackChannelName)
            .ToListAsync(ct);

        var workspaceNames = (await workspaces.ListAsync(ct)).ToDictionary(w => w.Id, w => w.TeamName);
        var details = new Dictionary<Guid, GoogleAccountDetail>();
        foreach (var accountId in mappings.Select(m => m.GoogleAccountId).Distinct())
        {
            if (await googleAccounts.GetAsync(accountId, ct) is { } d) details[accountId] = d;
        }

        return mappings.Select(m =>
        {
            details.TryGetValue(m.GoogleAccountId, out var d);
            return new ChannelMappingDto(
                m.Id, m.SlackWorkspaceId, workspaceNames.GetValueOrDefault(m.SlackWorkspaceId, ""),
                m.SlackChannelId, m.SlackChannelName,
                m.GoogleAccountId, d?.ChannelTitle ?? "",
                d?.AvatarUrl, d?.ChannelId,
                m.IsActive,
                m.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<MappingRoute>> ListRoutesAsync(CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId, m.IsActive))
            .ToListAsync(ct);

    public async Task<MappingRoute?> GetByChannelAsync(string slackChannelId, CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Where(m => m.SlackChannelId == slackChannelId)
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId, m.IsActive))
            .FirstOrDefaultAsync(ct);

    /// <summary>Creates a mapping. Returns an error code on conflict (channel already mapped, etc.).</summary>
    public async Task<(bool ok, string? error)> CreateAsync(
        Guid workspaceId, string slackChannelId, Guid googleAccountId, CancellationToken ct = default)
    {
        var channel = await db.SlackChannels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.SlackChannelId == slackChannelId, ct);
        if (channel is null) return (false, "channel_not_found");
        if (await db.ChannelMappings.AnyAsync(m => m.SlackChannelId == slackChannelId, ct)) return (false, "already_mapped");
        if (await googleAccounts.GetAsync(googleAccountId, ct) is null) return (false, "account_not_found");

        db.ChannelMappings.Add(new ChannelMapping
        {
            SlackWorkspaceId = workspaceId,
            SlackChannelId = slackChannelId,
            SlackChannelName = channel.Name,
            GoogleAccountId = googleAccountId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Toggles a route's active/paused state (the P0 pause toggle). Returns <c>false</c> when not found.
    /// There is no scheduler call: uploads are event-driven, so a paused route is skipped at ingest
    /// (<see cref="MappingRoute.IsActive"/> read on every Slack message) rather than via a per-mapping job.
    /// Every change writes one shared audit entry.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, bool? isActive, CancellationToken ct = default)
    {
        var m = await db.ChannelMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;

        if (isActive.HasValue) m.IsActive = isActive.Value;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("mapping.updated", module: "youtube-uploads", entityType: "channel_mapping",
            entityId: m.Id.ToString(), detail: $"active={m.IsActive}", ct: ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.ChannelMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        db.ChannelMappings.Remove(m);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("mapping.deleted", module: "youtube-uploads", entityType: "channel_mapping",
            entityId: id.ToString(), detail: m.SlackChannelName, ct: ct);
        return true;
    }

    public Task<bool> IsAccountMappedAsync(Guid googleAccountId, CancellationToken ct = default) =>
        db.ChannelMappings.AnyAsync(m => m.GoogleAccountId == googleAccountId, ct);

    /// <summary>Remove every mapping pointing at a now-disconnected account (event-driven). Mappings are
    /// rebuildable routing config, so they are hard-deleted; the disconnect is recorded in the audit log
    /// by the caller (the account↔project binding is soft-deactivated, preserving that link's history).</summary>
    public async Task<int> RemoveByAccountAsync(Guid googleAccountId, CancellationToken ct = default)
    {
        var gone = await db.ChannelMappings.Where(m => m.GoogleAccountId == googleAccountId).ToListAsync(ct);
        if (gone.Count == 0) return 0;
        db.ChannelMappings.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
        return gone.Count;
    }

    /// <summary>Remove every mapping for a now-disconnected workspace (event-driven). Hard-deleted as
    /// rebuildable routing config; the disconnect is recorded in the audit log by the caller.</summary>
    public async Task<int> RemoveByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var gone = await db.ChannelMappings.Where(m => m.SlackWorkspaceId == workspaceId).ToListAsync(ct);
        if (gone.Count == 0) return 0;
        db.ChannelMappings.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
        return gone.Count;
    }
}
