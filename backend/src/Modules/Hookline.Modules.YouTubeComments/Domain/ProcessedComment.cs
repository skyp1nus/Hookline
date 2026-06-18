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

    // ── Engagement snapshot, captured at forward time (Phase B). All nullable: rows written before the
    //    capture migration carry null and are simply excluded from the enriched analytics (never counted
    //    as a real 0). Comment TEXT is never stored — only its length — so durable analytics stays lean. ──

    /// <summary>The video's title at forward time (lets per-video stats show a name, not a raw id).</summary>
    public string? VideoTitle { get; set; }

    /// <summary>The comment author's display name at forward time.</summary>
    public string? AuthorName { get; set; }

    /// <summary>The author's channel URL — the stable identity for the author leaderboard.</summary>
    public string? AuthorChannelUrl { get; set; }

    /// <summary>The comment's like count at forward time.</summary>
    public long? LikeCount { get; set; }

    /// <summary>When the comment was published on YouTube (vs <see cref="ProcessedAt"/> = forward latency).</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>The comment text's character length (the text itself is intentionally not persisted).</summary>
    public int? CommentLength { get; set; }

    public ChannelMapping? Mapping { get; set; }
}
