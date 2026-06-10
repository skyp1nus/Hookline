namespace Hookline.Modules.YouTubeUploads.Domain;

/// <summary>
/// One Google OAuth client = one Google Cloud project ("Project"). YouTube Data API quota is
/// enforced PER PROJECT (OAuth client), so adding more projects raises the per-channel upload
/// ceiling: connect the same channel through several projects and the worker rotates to the next
/// when one project's daily quota is exhausted (see <c>UploadJobHandler</c>).
///
/// HARD RULE: a refresh token can only be refreshed by the client that issued it — every
/// <see cref="GoogleAccountBinding"/> permanently remembers its issuing project and must always
/// rebuild credentials with THAT project. The client secret is stored ENCRYPTED via the shared
/// <c>ISecretProtector</c> (no module-local crypto) and never leaves the server.
///
/// This lives in the module's <c>youtube_uploads</c> schema; the shared Connections subsystem deliberately
/// models a single OAuth client. The connected account record (identity + refresh token) lives in the
/// shared <c>connections.google_accounts</c>; the binding below ties that account to its issuing project.
/// </summary>
public class GoogleProject
{
    public const string StatusActive = "Active";
    public const string StatusDisabled = "Disabled";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly name, e.g. "Project A".</summary>
    public string Label { get; set; } = "";

    /// <summary>Google OAuth client id — NOT a secret (it appears in the consent URL).</summary>
    public string ClientId { get; set; } = default!;

    /// <summary>Client secret, encrypted at rest via the shared <c>ISecretProtector</c>.</summary>
    public string EncryptedClientSecret { get; set; } = default!;

    /// <summary>Active / Disabled. A disabled project is skipped by upload rotation.</summary>
    public string Status { get; set; } = StatusActive;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Binds a shared-Connections Google account to the module-local project that issued its refresh
/// token. One binding per account (an account is issued by exactly one project). Carries everything
/// candidate SELECTION needs locally — the bound account id (plain value, NO cross-schema FK), the
/// issuing project, and the target YouTube channel/pool key snapshotted at bind time — so rotation
/// runs entirely on <c>youtube_uploads</c> tables. Only the FINAL refresh-token resolve for the chosen
/// account crosses to <c>connections</c>, via the <c>IGoogleConnections</c> accessor (a contract,
/// never a SQL join).
/// </summary>
public class GoogleAccountBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Shared Connections Google account id (plain value, NO FK to <c>connections</c>).</summary>
    public Guid AccountId { get; set; }

    /// <summary>The issuing project (module-local). A refresh token is only refreshable by this client.</summary>
    public Guid ProjectId { get; set; }
    public GoogleProject? Project { get; set; }

    /// <summary>YouTube channel id snapshotted at bind time — the rotation pool key (single-schema selection).</summary>
    public string? YouTubeChannelId { get; set; }

    /// <summary>Display label snapshotted at bind time (defaults to the channel title).</summary>
    public string Label { get; set; } = "";

    /// <summary>Active / Error — rotation skips non-active bindings.</summary>
    public string Status { get; set; } = "Active";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
