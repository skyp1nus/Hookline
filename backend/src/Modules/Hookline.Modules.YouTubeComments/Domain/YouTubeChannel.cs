namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>A monitored YouTube channel (the poll source). Module-local.</summary>
public class YouTubeChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string YouTubeChannelId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? ThumbnailUrl { get; set; }
    public string? Handle { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChannelMapping> Mappings { get; set; } = new List<ChannelMapping>();
}
