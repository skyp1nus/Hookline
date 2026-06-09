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

public interface ISlackConnections
{
    Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default);
}

public sealed record GoogleAccessCredential(
    Guid AccountId,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);

public sealed record GoogleAccountSummary(Guid Id, string ChannelTitle, bool IsActive);

public interface IGoogleConnections
{
    Task<GoogleAccessCredential?> GetCredentialAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<GoogleAccountSummary>> ListAsync(CancellationToken ct = default);
}

public sealed record ConnectionSummary(Guid Id, ConnectionType Type, string Label, string Status, string? Detail);

public interface IConnectionCatalog
{
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(ConnectionType? type = null, CancellationToken ct = default);
}

// ── Events published by the Connections subsystem ──

public sealed record SlackWorkspaceDisconnected(Guid WorkspaceId) : IntegrationEvent;

public sealed record GoogleAccountDisconnected(Guid AccountId) : IntegrationEvent;
