using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record SlackWorkspaceDto(
    Guid Id, string SlackTeamId, string TeamName, string? BotUserId,
    bool IsActive, DateTimeOffset InstalledAt, int ChannelCount);

public sealed record SlackChannelDto(Guid Id, string SlackChannelId, string Name, bool IsPrivate, bool IsMember);

/// <summary>
/// Manages connected Slack workspaces. The workspace + bot token live in the SHARED <c>connections</c>
/// store (via <see cref="ISlackConnections"/>); this service completes the OAuth install (token exchange
/// → upsert into the shared store), keeps a module-local channel cache (<c>youtube_uploads.slack_channels</c>),
/// and resolves the per-workspace bot token used everywhere we post to Slack. Channels are a module
/// concern, not a shared connection.
/// </summary>
public sealed class SlackChannelService(
    YouTubeUploadsDbContext db,
    ISlackConnections workspaces,
    SlackClient slack,
    ILogger<SlackChannelService> logger)
{
    /// <summary>Token exchange → upsert workspace in the shared store (encrypt token) → best-effort channel sync.</summary>
    public async Task<Guid> HandleOAuthCallbackAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var oauth = await slack.ExchangeCodeAsync(code, redirectUri, ct);

        var workspaceId = await workspaces.UpsertWorkspaceAsync(new SlackWorkspaceWrite(
            TeamId: oauth.TeamId,
            TeamName: oauth.TeamName,
            BotToken: oauth.AccessToken,
            BotUserId: oauth.BotUserId,
            Scope: oauth.Scope,
            AuthedUserId: oauth.AuthedUserId), ct);

        try
        {
            await SyncChannelsAsync(workspaceId, oauth.AccessToken, ct);
        }
        catch (Exception ex)
        {
            // Don't fail the install if the immediate sync fails — the user can refresh later.
            logger.LogWarning(ex, "Channel sync failed right after connecting workspace {Team}", oauth.TeamId);
        }

        return workspaceId;
    }

    public async Task<IReadOnlyList<SlackWorkspaceDto>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var list = await workspaces.ListAsync(ct);
        var counts = await db.SlackChannels.AsNoTracking()
            .GroupBy(c => c.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);

        // Only surface active workspaces. Disconnect soft-deactivates the row (DeactivateAsync fans out
        // SlackWorkspaceDisconnected for dependent cleanup); the deactivated row is kept for audit/history
        // but must not linger as an "Inactive" card in the UI.
        return list
            .Where(w => w.IsActive)
            .Select(w => new SlackWorkspaceDto(
                w.Id, w.TeamId, w.TeamName, null, w.IsActive, DateTimeOffset.MinValue,
                counts.GetValueOrDefault(w.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<SlackChannelDto>> ListChannelsAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SlackChannels.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .Select(c => new SlackChannelDto(c.Id, c.SlackChannelId, c.Name, c.IsPrivate, c.IsMember))
            .ToListAsync(ct);

    /// <summary>All channels the bot is a member of, across workspaces (for the mapping picker).</summary>
    public async Task<IReadOnlyList<object>> ListAllMemberChannelsAsync(CancellationToken ct = default)
    {
        var names = (await workspaces.ListAsync(ct)).ToDictionary(w => w.Id, w => w.TeamName);
        var channels = await db.SlackChannels.AsNoTracking()
            .Where(c => c.IsMember)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.SlackChannelId, c.Name, c.IsPrivate, c.WorkspaceId })
            .ToListAsync(ct);

        return channels
            .OrderBy(c => names.GetValueOrDefault(c.WorkspaceId, "")).ThenBy(c => c.Name)
            .Select(c => (object)new
            {
                c.Id,
                c.SlackChannelId,
                c.Name,
                c.IsPrivate,
                workspaceId = c.WorkspaceId,
                workspaceName = names.GetValueOrDefault(c.WorkspaceId, ""),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SlackChannelDto>?> RefreshChannelsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var token = await workspaces.GetBotTokenAsync(workspaceId, ct);
        if (token is null) return null;

        await SyncChannelsAsync(workspaceId, token, ct);

        return await ListChannelsAsync(workspaceId, ct);
    }

    public async Task<bool> DeleteWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var ok = await workspaces.DeactivateAsync(id, ct); // publishes SlackWorkspaceDisconnected
        var channels = await db.SlackChannels.Where(c => c.WorkspaceId == id).ToListAsync(ct);
        if (channels.Count > 0)
        {
            db.SlackChannels.RemoveRange(channels);
            await db.SaveChangesAsync(ct);
        }
        return ok;
    }

    /// <summary>Decrypted bot token of the workspace that owns the given Slack channel id, or null.</summary>
    public async Task<string?> GetBotTokenForChannelAsync(string slackChannelId, CancellationToken ct = default)
    {
        var workspaceId = await db.SlackChannels.AsNoTracking()
            .Where(c => c.SlackChannelId == slackChannelId)
            .Select(c => (Guid?)c.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        return workspaceId is null ? null : await workspaces.GetBotTokenAsync(workspaceId.Value, ct);
    }

    /// <summary>Decrypted bot token for a Slack team id (used by the events endpoint), or null.</summary>
    public Task<string?> GetBotTokenForTeamAsync(string teamId, CancellationToken ct = default) =>
        workspaces.GetBotTokenForTeamAsync(teamId, ct);

    public async Task<int> CountActiveWorkspacesAsync(CancellationToken ct = default) =>
        (await workspaces.ListAsync(ct)).Count(w => w.IsActive);

    // ---- channel reconciliation ------------------------------------------------------
    private async Task SyncChannelsAsync(Guid workspaceId, string botToken, CancellationToken ct)
    {
        var live = await slack.ListChannelsAsync(botToken, ct);
        var existing = await db.SlackChannels
            .Where(c => c.WorkspaceId == workspaceId)
            .ToDictionaryAsync(c => c.SlackChannelId, c => c, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var info in live)
        {
            seen.Add(info.Id);
            if (existing.TryGetValue(info.Id, out var channel))
            {
                channel.Name = info.Name;
                channel.IsPrivate = info.IsPrivate;
                channel.IsMember = info.IsMember;
                channel.UpdatedAt = now;
            }
            else
            {
                db.SlackChannels.Add(new SlackChannel
                {
                    WorkspaceId = workspaceId,
                    SlackChannelId = info.Id,
                    Name = info.Name,
                    IsPrivate = info.IsPrivate,
                    IsMember = info.IsMember,
                    UpdatedAt = now,
                });
            }
        }

        foreach (var (id, gone) in existing)
        {
            if (!seen.Contains(id)) db.SlackChannels.Remove(gone);
        }

        await db.SaveChangesAsync(ct);
    }
}
