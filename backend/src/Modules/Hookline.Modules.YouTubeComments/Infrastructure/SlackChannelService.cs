using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>A connected Slack workspace (from the shared store), with a count of channels synced for it.</summary>
public sealed record SlackWorkspaceDto(Guid Id, string TeamId, string TeamName, bool IsActive, int ChannelCount);

/// <summary>A Slack channel cached for a workspace (the mapping picker reads these).</summary>
public sealed record SlackChannelDto(Guid Id, string SlackChannelId, string Name, bool IsPrivate);

/// <summary>
/// Bridges the module to the shared Slack Connections store. Workspaces (bot tokens) live in the
/// shared <c>connections</c> schema; this owns only the module-local channel cache (the mapping
/// picker). Connect goes through the provider OAuth callback (upsert into the shared store); disconnect
/// deactivates the shared workspace, which fans out a <c>SlackWorkspaceDisconnected</c> event.
/// </summary>
public sealed class SlackChannelService(
    YouTubeCommentsDbContext db,
    ISlackClient slack,
    ISlackConnections slackConnections,
    ICommentsAudit audit)
{
    /// <summary>Lists every connected workspace with the number of channels cached for it.</summary>
    public async Task<SlackWorkspaceDto[]> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var workspaces = await slackConnections.ListAsync(ct);
        var counts = await db.SlackChannels
            .AsNoTracking()
            .GroupBy(c => c.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);

        return workspaces
            .Select(w => new SlackWorkspaceDto(w.Id, w.TeamId, w.TeamName, w.IsActive, counts.GetValueOrDefault(w.Id)))
            .ToArray();
    }

    /// <summary>
    /// Completes the OAuth flow: exchanges <paramref name="code"/> for a bot token, upserts the
    /// workspace into the shared store (keyed by Slack team id), then syncs its channels into the
    /// module-local cache. Returns the shared workspace id.
    /// </summary>
    public async Task<Guid> HandleOAuthCallbackAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var oauth = await slack.ExchangeCodeAsync(code, redirectUri, ct);

        var workspaceId = await slackConnections.UpsertWorkspaceAsync(new SlackWorkspaceWrite(
            TeamId: oauth.TeamId,
            TeamName: oauth.TeamName,
            BotToken: oauth.AccessToken,
            BotUserId: oauth.BotUserId,
            Scope: oauth.Scope,
            AuthedUserId: oauth.AuthedUserId), ct);

        // Best-effort channel sync immediately after connect, using the token we just obtained.
        await SyncChannelsAsync(workspaceId, oauth.AccessToken, ct);

        await audit.LogAsync(AuditLevel.Information, "Slack",
            $"Connected Slack workspace '{oauth.TeamName}'", "SlackWorkspace", workspaceId.ToString(), ct: ct);

        return workspaceId;
    }

    /// <summary>Lists the channels cached for a workspace.</summary>
    public async Task<SlackChannelDto[]> ListChannelsAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SlackChannels
            .AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .Select(c => new SlackChannelDto(c.Id, c.SlackChannelId, c.Name, c.IsPrivate))
            .ToArrayAsync(ct);

    /// <summary>
    /// Re-fetches channels from Slack for a workspace and reconciles the cached set. Returns the
    /// resulting channels, or <c>null</c> when the workspace has no bot token (gone/disconnected).
    /// </summary>
    public async Task<SlackChannelDto[]?> RefreshChannelsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var botToken = await slackConnections.GetBotTokenAsync(workspaceId, ct);
        if (string.IsNullOrEmpty(botToken))
            return null;

        await SyncChannelsAsync(workspaceId, botToken, ct);
        return await ListChannelsAsync(workspaceId, ct);
    }

    /// <summary>Disconnects a workspace from the shared store (fans out <c>SlackWorkspaceDisconnected</c>).
    /// Returns <c>false</c> when not found.</summary>
    public async Task<bool> DeleteWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        if (!await slackConnections.DeactivateAsync(id, ct))
            return false;

        await audit.LogAsync(AuditLevel.Information, "Slack",
            "Disconnected Slack workspace", "SlackWorkspace", id.ToString(), ct: ct);
        return true;
    }

    /// <summary>Reconciles the module-local channel cache for a workspace against the live list from Slack.</summary>
    private async Task SyncChannelsAsync(Guid workspaceId, string botToken, CancellationToken ct)
    {
        var live = await slack.ListChannelsAsync(botToken, ct);

        var existing = await db.SlackChannels
            .Where(c => c.WorkspaceId == workspaceId)
            .ToDictionaryAsync(c => c.SlackChannelId, c => c, StringComparer.Ordinal, ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var info in live)
        {
            seen.Add(info.Id);
            if (existing.TryGetValue(info.Id, out var channel))
            {
                channel.Name = info.Name;
                channel.IsPrivate = info.IsPrivate;
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
                    UpdatedAt = now,
                });
            }
        }

        // Drop channels the bot can no longer see.
        var gone = existing.Values.Where(c => !seen.Contains(c.SlackChannelId)).ToList();
        if (gone.Count > 0)
            db.SlackChannels.RemoveRange(gone);

        await db.SaveChangesAsync(ct);
    }
}
