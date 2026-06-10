namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// A channel from a connected Slack workspace (mapping-picker cache). Module-local —
/// channels are a module concern, not a shared connection. <see cref="WorkspaceId"/> is the
/// shared Connections workspace id (plain value, no cross-schema FK).
/// </summary>
public class SlackChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string SlackChannelId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsPrivate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChannelMapping> Mappings { get; set; } = new List<ChannelMapping>();
}
