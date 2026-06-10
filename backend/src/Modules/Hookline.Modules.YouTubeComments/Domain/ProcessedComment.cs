namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>Dedup ledger. Composite PK (MappingId, CommentId): a comment is delivered once per mapping.</summary>
public class ProcessedComment
{
    public Guid MappingId { get; set; }
    public string CommentId { get; set; } = default!;
    public string VideoId { get; set; } = default!;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Slack message ts where this comment landed; lets replies thread under it. Null for older rows / failed posts.</summary>
    public string? SlackMessageTs { get; set; }

    /// <summary>The top-level comment id this is a reply to; null when this is itself a top-level comment.</summary>
    public string? ParentCommentId { get; set; }

    public ChannelMapping? Mapping { get; set; }
}
