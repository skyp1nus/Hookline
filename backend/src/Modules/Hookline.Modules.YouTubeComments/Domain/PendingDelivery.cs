namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// A Slack post that failed transiently, persisted so it survives the YouTube fetch window and is
/// retried out-of-band by the delivery-retry job (instead of being lost when the comment scrolls
/// out of the poll's 50-comment view). <see cref="PayloadJson"/> is the serialized notification.
/// </summary>
public class PendingDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MappingId { get; set; }
    public string CommentId { get; set; } = default!;
    public string? ParentCommentId { get; set; }
    public string VideoId { get; set; } = default!;

    /// <summary>Serialized CommentNotification — the exact message to (re)post.</summary>
    public string PayloadJson { get; set; } = default!;

    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChannelMapping? Mapping { get; set; }
}
