namespace Hookline.Modules.YouTubeUploads.Domain;

/// <summary>
/// A channel synced from a connected Slack workspace via conversations.list. Module-local
/// cache (channels are a module concern, not a shared connection). <see cref="WorkspaceId"/>
/// is the shared Connections workspace id (plain value, no cross-schema FK).
/// </summary>
public class SlackChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    public string SlackChannelId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsPrivate { get; set; }
    /// <summary>Whether the bot is a member — it must be invited before it can read/post.</summary>
    public bool IsMember { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
