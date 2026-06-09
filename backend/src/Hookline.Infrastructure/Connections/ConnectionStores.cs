using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Connections;

/// <summary>Reads Slack bot tokens (decrypted by the converter on read) + lists workspaces.</summary>
public sealed class SlackConnections(ConnectionsDbContext db) : ISlackConnections
{
    public async Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId && w.IsActive)
            .Select(w => w.BotTokenEncrypted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .OrderBy(w => w.TeamName)
            .Select(w => new SlackWorkspaceSummary(w.Id, w.TeamId, w.TeamName, w.IsActive))
            .ToListAsync(ct);
}

/// <summary>
/// Lists Google accounts. <see cref="GetCredentialAsync"/> is a Phase-0 skeleton — real
/// refresh-token exchange lands when SlackTube is absorbed (Phase 1).
/// </summary>
public sealed class GoogleConnections(ConnectionsDbContext db) : IGoogleConnections
{
    public Task<GoogleAccessCredential?> GetCredentialAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<GoogleAccessCredential?>(null);

    public async Task<IReadOnlyList<GoogleAccountSummary>> ListAsync(CancellationToken ct = default) =>
        await db.GoogleAccounts.AsNoTracking()
            .OrderBy(g => g.ChannelTitle)
            .Select(g => new GoogleAccountSummary(g.Id, g.ChannelTitle, g.IsActive))
            .ToListAsync(ct);
}

/// <summary>Aggregates every connection into a unified catalog for the UI.</summary>
public sealed class ConnectionCatalog(ConnectionsDbContext db) : IConnectionCatalog
{
    public async Task<IReadOnlyList<ConnectionSummary>> ListAsync(
        ConnectionType? type = null,
        CancellationToken ct = default)
    {
        var result = new List<ConnectionSummary>();

        if (type is null or ConnectionType.Slack)
        {
            result.AddRange(await db.SlackWorkspaces.AsNoTracking()
                .Select(w => new ConnectionSummary(
                    w.Id, ConnectionType.Slack, w.TeamName,
                    w.IsActive ? "connected" : "disabled", w.TeamId))
                .ToListAsync(ct));
        }

        if (type is null or ConnectionType.Google)
        {
            result.AddRange(await db.GoogleAccounts.AsNoTracking()
                .Select(g => new ConnectionSummary(
                    g.Id, ConnectionType.Google, g.ChannelTitle,
                    g.IsActive ? "connected" : "disabled", g.ChannelId))
                .ToListAsync(ct));
        }

        if (type is null or ConnectionType.YouTubeApiKey)
        {
            result.AddRange(await db.YouTubeApiKeys.AsNoTracking()
                .Select(k => new ConnectionSummary(
                    k.Id, ConnectionType.YouTubeApiKey, k.Name,
                    k.IsActive ? "active" : "disabled", k.KeyHint))
                .ToListAsync(ct));
        }

        return result;
    }
}
