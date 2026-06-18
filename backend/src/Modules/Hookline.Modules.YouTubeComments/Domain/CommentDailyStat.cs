namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// A nightly per-mapping rollup of one UTC day's activity, so long-horizon trends survive the retention
/// <c>CleanupJob</c> that trims the raw <c>processed_comments</c> / <c>comment_moderations</c> rows.
/// Keyed (<see cref="Date"/>, <see cref="MappingId"/>) with NO foreign key — the row is kept even after
/// its mapping (or channel) is deleted, and <see cref="ChannelTitle"/> is denormalized at rollup time so
/// history can still be labelled. Counts mirror the live aggregates (forwarded/replies/removed + the
/// moderation outcome split) plus the engagement totals captured that day.
/// </summary>
public class CommentDailyStat
{
    /// <summary>The UTC day this rollup covers.</summary>
    public DateOnly Date { get; set; }

    public Guid MappingId { get; set; }

    /// <summary>The owning YouTube channel's title at rollup time (denormalized so history outlives deletion).</summary>
    public string ChannelTitle { get; set; } = default!;

    public int Forwarded { get; set; }

    /// <summary>Reply subset of <see cref="Forwarded"/>.</summary>
    public int Replies { get; set; }

    public int Removed { get; set; }
    public int Rejected { get; set; }
    public int AlreadyGone { get; set; }

    /// <summary>Sum of like counts across the day's engagement-enriched forwarded comments.</summary>
    public long SumLikes { get; set; }

    /// <summary>How many of the day's forwarded comments carried the engagement snapshot.</summary>
    public int EnrichedCount { get; set; }
}
