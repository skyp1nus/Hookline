namespace Hookline.Infrastructure.Connections;

/// <summary>A connected Slack workspace (OAuth v2 bot token). Multiple per provider allowed.</summary>
public sealed class SlackWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
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
    public string RefreshTokenEncrypted { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A YouTube Data API key (comment polling). Multiple per provider, quota-rotated.</summary>
public sealed class YouTubeApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string KeyHint { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
