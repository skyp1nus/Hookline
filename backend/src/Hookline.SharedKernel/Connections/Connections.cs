using Hookline.SharedKernel.Messaging;

namespace Hookline.SharedKernel.Connections;

/// <summary>The external connection kinds the hub knows about.</summary>
public enum ConnectionType
{
    Slack,
    Google,
    YouTubeApiKey,
}

/// <summary>A module declares the connections it needs; the host can validate availability.</summary>
public sealed record ConnectionRequirement(ConnectionType Type, bool Required = true, string? Note = null);

// ── Typed accessors (modules resolve a credential at job time; they never touch storage) ──

public sealed record SlackWorkspaceSummary(Guid Id, string TeamId, string TeamName, bool IsActive);

/// <summary>Payload to upsert a Slack workspace into the shared store (OAuth v2 install callback).</summary>
public sealed record SlackWorkspaceWrite(
    string TeamId,
    string TeamName,
    string BotToken,
    string? BotUserId = null,
    string? Scope = null,
    string? AuthedUserId = null);

public interface ISlackConnections
{
    Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Resolve a bot token by Slack team id — the events endpoint maps an inbound <c>team_id</c>.</summary>
    Task<string?> GetBotTokenForTeamAsync(string teamId, CancellationToken ct = default);

    Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default);

    Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, CancellationToken ct = default);

    /// <summary>Insert or update (by team id) a workspace + encrypted bot token. Returns the workspace id.</summary>
    Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default);

    /// <summary>Deactivate a workspace and publish <see cref="SlackWorkspaceDisconnected"/>.</summary>
    Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default);
}

public sealed record GoogleAccessCredential(
    Guid AccountId,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);

public sealed record GoogleAccountSummary(Guid Id, string ChannelTitle, bool IsActive);

/// <summary>Full account detail a module needs to drive uploads + display (no secrets).</summary>
public sealed record GoogleAccountDetail(
    Guid Id,
    string? ChannelId,
    string ChannelTitle,
    string? AccountEmail,
    string? AvatarUrl,
    string Scopes,
    bool IsActive);

/// <summary>Payload to insert a Google account into the shared store (per-consent OAuth callback).</summary>
public sealed record GoogleAccountWrite(
    string? ChannelId,
    string ChannelTitle,
    string RefreshToken,
    string Scopes,
    string? AccountEmail = null,
    string? AvatarUrl = null);

public interface IGoogleConnections
{
    Task<IReadOnlyList<GoogleAccountSummary>> ListAsync(CancellationToken ct = default);

    Task<GoogleAccountDetail?> GetAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Short-lived access credential. Implementable only with an app-wide OAuth client; in Phase 1
    /// the YouTube Uploads module owns its OAuth clients ("Projects"), so it resolves the refresh token via
    /// <see cref="GetRefreshTokenAsync"/> and builds the credential with its issuing client itself.
    /// </summary>
    Task<GoogleAccessCredential?> GetCredentialAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>The account's decrypted refresh token — refreshed only by its issuing OAuth client.</summary>
    Task<string?> GetRefreshTokenAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Insert a new connected account (encrypts the refresh token at rest). Returns the account id.</summary>
    Task<Guid> CreateAccountAsync(GoogleAccountWrite write, CancellationToken ct = default);

    /// <summary>Deactivate an account and publish <see cref="GoogleAccountDisconnected"/>.</summary>
    Task<bool> DeactivateAsync(Guid accountId, CancellationToken ct = default);
}

public sealed record ConnectionSummary(Guid Id, ConnectionType Type, string Label, string Status, string? Detail);

public interface IConnectionCatalog
{
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(ConnectionType? type = null, CancellationToken ct = default);
}

// ── Events published by the Connections subsystem ──

public sealed record SlackWorkspaceDisconnected(Guid WorkspaceId) : IntegrationEvent;

public sealed record GoogleAccountDisconnected(Guid AccountId) : IntegrationEvent;
