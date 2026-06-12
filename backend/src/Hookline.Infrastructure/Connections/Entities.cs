namespace Hookline.Infrastructure.Connections;

/// <summary>A connected Slack workspace (OAuth v2 bot token). One row per (team, app): each tool is its
/// own Slack app, so the same team can be connected once per app with its own bot token.</summary>
public sealed class SlackWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which Slack app installed this (the owning module key, e.g. <c>youtube-uploads</c>).</summary>
    public string App { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string BotTokenEncrypted { get; set; } = string.Empty;
    public string? BotUserId { get; set; }
    public string? Scope { get; set; }
    public string? AuthedUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A connected Google account (OAuth refresh token → YouTube + Drive scopes).</summary>
public sealed class GoogleAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ChannelId { get; set; }
    public string ChannelTitle { get; set; } = string.Empty;
    public string? AccountEmail { get; set; }
    public string? AvatarUrl { get; set; }
    public string RefreshTokenEncrypted { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}
