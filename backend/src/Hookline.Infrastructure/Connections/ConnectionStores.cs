using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Connections;

/// <summary>Reads/writes Slack bot tokens (encrypted by the converter) + lists workspaces.</summary>
public sealed class SlackConnections(ConnectionsDbContext db, IEventBus events) : ISlackConnections
{
    public async Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId && w.IsActive)
            .Select(w => w.BotTokenEncrypted)
            .FirstOrDefaultAsync(ct);

    public async Task<string?> GetBotTokenForTeamAsync(string teamId, string app, CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .Where(w => w.TeamId == teamId && w.App == app && w.IsActive)
            .Select(w => w.BotTokenEncrypted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .OrderBy(w => w.TeamName)
            .Select(w => new SlackWorkspaceSummary(w.Id, w.TeamId, w.TeamName, w.App, w.IsActive))
            .ToListAsync(ct);

    public async Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, string app, CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .Where(w => w.TeamId == teamId && w.App == app)
            .Select(w => new SlackWorkspaceSummary(w.Id, w.TeamId, w.TeamName, w.App, w.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default)
    {
        // Find-or-create by (team, app): two Slack apps installed into the same team keep distinct rows +
        // bot tokens (was keyed on team_id alone — the second install overwrote the first's token).
        var existing = await db.SlackWorkspaces.FirstOrDefaultAsync(
            w => w.TeamId == write.TeamId && w.App == write.App, ct);
        if (existing is null)
        {
            existing = new SlackWorkspace { App = write.App, TeamId = write.TeamId, InstalledAt = DateTimeOffset.UtcNow };
            db.SlackWorkspaces.Add(existing);
        }

        existing.TeamName = write.TeamName;
        existing.BotTokenEncrypted = write.BotToken; // encrypted on write by the converter
        existing.BotUserId = write.BotUserId;
        existing.Scope = write.Scope;
        existing.AuthedUserId = write.AuthedUserId;
        existing.IsActive = true;

        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    public async Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var ws = await db.SlackWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (ws is null)
        {
            return false;
        }

        ws.IsActive = false;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new SlackWorkspaceDisconnected(workspaceId), ct);
        return true;
    }
}

/// <summary>Reads/writes Google accounts in the shared store. The decrypted refresh token is handed to
/// the owning module, which rebuilds credentials with its issuing OAuth client (Projects are module-local).</summary>
public sealed class GoogleConnections(ConnectionsDbContext db, IEventBus events) : IGoogleConnections
{
    public async Task<IReadOnlyList<GoogleAccountSummary>> ListAsync(CancellationToken ct = default) =>
        await db.GoogleAccounts.AsNoTracking()
            .OrderBy(g => g.ConnectedAt)
            .Select(g => new GoogleAccountSummary(g.Id, g.ChannelTitle, g.IsActive))
            .ToListAsync(ct);

    public async Task<GoogleAccountDetail?> GetAsync(Guid accountId, CancellationToken ct = default) =>
        await db.GoogleAccounts.AsNoTracking()
            .Where(g => g.Id == accountId)
            .Select(g => new GoogleAccountDetail(
                g.Id, g.ChannelId, g.ChannelTitle, g.AccountEmail, g.AvatarUrl, g.Scopes, g.IsActive))
            .FirstOrDefaultAsync(ct);

    // Needs an app-wide OAuth client to mint access tokens; module-local Projects own that, so this
    // stays null and callers use GetRefreshTokenAsync + their issuing client instead.
    public Task<GoogleAccessCredential?> GetCredentialAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<GoogleAccessCredential?>(null);

    public async Task<string?> GetRefreshTokenAsync(Guid accountId, CancellationToken ct = default) =>
        await db.GoogleAccounts.AsNoTracking()
            .Where(g => g.Id == accountId && g.IsActive)
            .Select(g => g.RefreshTokenEncrypted) // decrypted on read by the converter
            .FirstOrDefaultAsync(ct);

    public async Task<Guid> CreateAccountAsync(GoogleAccountWrite write, CancellationToken ct = default)
    {
        var account = new GoogleAccount
        {
            ChannelId = write.ChannelId,
            ChannelTitle = write.ChannelTitle,
            AccountEmail = write.AccountEmail,
            AvatarUrl = write.AvatarUrl,
            RefreshTokenEncrypted = write.RefreshToken, // encrypted on write by the converter
            Scopes = write.Scopes,
            IsActive = true,
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        db.GoogleAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    public async Task<bool> UpdateConsentAsync(
        Guid accountId, string refreshToken, string scopes,
        string? channelTitle = null, string? accountEmail = null, string? avatarUrl = null,
        CancellationToken ct = default)
    {
        var account = await db.GoogleAccounts.FirstOrDefaultAsync(g => g.Id == accountId, ct);
        if (account is null)
        {
            return false;
        }

        account.RefreshTokenEncrypted = refreshToken; // re-encrypted on write by the converter
        account.Scopes = scopes;
        if (channelTitle is not null) account.ChannelTitle = channelTitle;
        if (accountEmail is not null) account.AccountEmail = accountEmail;
        if (avatarUrl is not null) account.AvatarUrl = avatarUrl;
        account.IsActive = true; // a fresh consent re-activates the record
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeactivateAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await db.GoogleAccounts.FirstOrDefaultAsync(g => g.Id == accountId, ct);
        if (account is null)
        {
            return false;
        }

        account.IsActive = false;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new GoogleAccountDisconnected(accountId), ct);
        return true;
    }
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

        return result;
    }
}
