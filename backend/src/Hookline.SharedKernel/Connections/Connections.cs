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

    /// <summary>
    /// Update an EXISTING account's refresh token + granted scopes in place. Used on a re-consent that
    /// widens scope (e.g. adding <c>youtube.force-ssl</c> for comment moderation) so the SAME account
    /// record is reused — no duplicate row — and its scope snapshot reflects the new grant. The owning
    /// module decides WHICH account to update (by its channel↔account binding); this is just the write.
    /// Returns false when the account is missing.
    /// </summary>
    Task<bool> UpdateConsentAsync(
        Guid accountId,
        string refreshToken,
        string scopes,
        string? channelTitle = null,
        string? accountEmail = null,
        string? avatarUrl = null,
        CancellationToken ct = default);

    /// <summary>Deactivate an account and publish <see cref="GoogleAccountDisconnected"/>.</summary>
    Task<bool> DeactivateAsync(Guid accountId, CancellationToken ct = default);
}

// ── YouTube Data API keys (comment polling — API keys, NOT OAuth) ──

public sealed record YouTubeApiKeySummary(Guid Id, string Name, string KeyHint, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>
/// Typed accessor over the shared <c>api_keys</c> store for the YouTube Comments module's
/// quota-rotated polling. Multiple keys per provider; the module ranks them by remaining
/// Pacific-day quota (tracked module-locally) and resolves the decrypted key at poll time.
/// </summary>
public interface IYouTubeApiKeyConnections
{
    /// <summary>All keys (active + disabled) for the admin UI.</summary>
    Task<IReadOnlyList<YouTubeApiKeySummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Only active keys — the rotation candidate set.</summary>
    Task<IReadOnlyList<YouTubeApiKeySummary>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>The decrypted API key for a given id (resolved at poll time for the lease).</summary>
    Task<string?> GetApiKeyAsync(Guid keyId, CancellationToken ct = default);

    /// <summary>Insert a new key (encrypts at rest). Returns the new key id.</summary>
    Task<Guid> CreateAsync(string name, string apiKey, string keyHint, CancellationToken ct = default);

    /// <summary>Enable/disable a key in the rotation pool. No event (the pool simply shrinks/grows).</summary>
    Task<bool> ToggleAsync(Guid keyId, bool isActive, CancellationToken ct = default);

    /// <summary>Hard-delete a key and publish <see cref="YouTubeApiKeyDisconnected"/>.</summary>
    Task<bool> DeleteAsync(Guid keyId, CancellationToken ct = default);
}

public sealed record ConnectionSummary(Guid Id, ConnectionType Type, string Label, string Status, string? Detail);

public interface IConnectionCatalog
{
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(ConnectionType? type = null, CancellationToken ct = default);
}

// ── Events published by the Connections subsystem ──

public sealed record SlackWorkspaceDisconnected(Guid WorkspaceId) : IntegrationEvent;

public sealed record GoogleAccountDisconnected(Guid AccountId) : IntegrationEvent;

/// <summary>A YouTube API key was deleted from the shared pool — modules drop any per-key state (e.g. quota rows).</summary>
public sealed record YouTubeApiKeyDisconnected(Guid KeyId) : IntegrationEvent;
