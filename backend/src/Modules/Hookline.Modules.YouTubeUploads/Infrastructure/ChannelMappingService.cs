using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record ChannelMappingDto(
    Guid Id, Guid SlackWorkspaceId, string SlackWorkspaceName,
    string SlackChannelId, string SlackChannelName,
    Guid GoogleAccountId, string GoogleAccountLabel,
    string? GoogleAccountAvatarUrl, string? GoogleAccountChannelId,
    DateTimeOffset CreatedAt);

/// <summary>Lightweight routing record used by the ingest/status paths.</summary>
public sealed record MappingRoute(
    Guid Id, string SlackChannelId, string SlackChannelName, Guid GoogleAccountId, Guid SlackWorkspaceId);

/// <summary>
/// Channel → account routing. Mappings are module-local; the workspace name and the account
/// label/avatar/channel are resolved through the shared Connections accessors (in-app, never a
/// cross-schema SQL join).
/// </summary>
public sealed class ChannelMappingService(
    YouTubeUploadsDbContext db,
    ISlackConnections workspaces,
    IGoogleConnections googleAccounts)
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
                m.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<MappingRoute>> ListRoutesAsync(CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId))
            .ToListAsync(ct);

    public async Task<MappingRoute?> GetByChannelAsync(string slackChannelId, CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Where(m => m.SlackChannelId == slackChannelId)
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId))
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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.ChannelMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        db.ChannelMappings.Remove(m);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> IsAccountMappedAsync(Guid googleAccountId, CancellationToken ct = default) =>
        db.ChannelMappings.AnyAsync(m => m.GoogleAccountId == googleAccountId, ct);

    /// <summary>Deactivate every mapping pointing at a now-disconnected account (event-driven).</summary>
    public async Task<int> RemoveByAccountAsync(Guid googleAccountId, CancellationToken ct = default)
    {
        var gone = await db.ChannelMappings.Where(m => m.GoogleAccountId == googleAccountId).ToListAsync(ct);
        if (gone.Count == 0) return 0;
        db.ChannelMappings.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
        return gone.Count;
    }

    /// <summary>Deactivate every mapping for a now-disconnected workspace (event-driven).</summary>
    public async Task<int> RemoveByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var gone = await db.ChannelMappings.Where(m => m.SlackWorkspaceId == workspaceId).ToListAsync(ct);
        if (gone.Count == 0) return 0;
        db.ChannelMappings.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
        return gone.Count;
    }
}
