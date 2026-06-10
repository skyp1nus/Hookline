namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// Routes a monitored YouTube channel's new comments to a Slack channel. Both the YouTube channel
/// and the Slack channel are module-local rows (intra-schema FKs). The Slack <em>workspace</em> the
/// channel belongs to lives in the shared <c>connections</c> schema and is referenced only by id.
/// </summary>
public class ChannelMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YouTubeChannelId { get; set; }
    public Guid SlackChannelId { get; set; }
    public PollingFrequency Frequency { get; set; } = PollingFrequency.FifteenMinutes;
    public bool IsActive { get; set; } = true;

    /// <summary>When true, replies to top-level comments are also forwarded (threaded under the parent in Slack).</summary>
    public bool IncludeReplies { get; set; }

    /// <summary>
    /// Cadence of the deep reply sweep that guarantees full reply coverage (replies on older comments
    /// the normal poll can't see). <see cref="ReplyScanFrequency.Off"/> = inline replies only.
    /// </summary>
    public ReplyScanFrequency ReplySweepFrequency { get; set; } = ReplyScanFrequency.Off;

    /// <summary>How many days back the deep reply sweep scans for new replies.</summary>
    public int ReplyWindowDays { get; set; } = 30;

    /// <summary>
    /// Watermark: only comments published after this instant are forwarded. Set on create and on each
    /// reactivation so a long-dormant mapping can't repost old comments once its dedup ledger has aged out.
    /// </summary>
    public DateTimeOffset CommentsSinceUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public string? LastError { get; set; }

    public YouTubeChannel? YouTubeChannel { get; set; }
    public SlackChannel? SlackChannel { get; set; }
    public ICollection<ProcessedComment> ProcessedComments { get; set; } = new List<ProcessedComment>();
}
