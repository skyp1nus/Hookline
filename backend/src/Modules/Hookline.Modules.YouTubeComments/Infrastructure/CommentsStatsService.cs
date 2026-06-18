using Hookline.Modules.YouTubeComments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

// ── Detailed Comments-dashboard DTOs (ASP.NET serializes camelCase → the TS field names match) ──

/// <summary>The rolling window a detailed-stats request covers, with its natural bucket granularity.</summary>
public enum StatsRange
{
    /// <summary>Last 24 hours, bucketed by UTC hour (24 points).</summary>
    Day,

    /// <summary>Last 7 days, bucketed by UTC day (7 points).</summary>
    Week,

    /// <summary>Last 30 days, bucketed by UTC day (30 points).</summary>
    Month,
}

/// <summary>One bucket of the activity timeline. <paramref name="Replies"/> is the reply subset of
/// <paramref name="Forwarded"/> (top-level = Forwarded − Replies); <paramref name="Removed"/> is comments
/// rejected on YouTube in the same bucket.</summary>
public sealed record ActivityPoint(DateTimeOffset Bucket, int Forwarded, int Replies, int Removed);

/// <summary>Forwarded/replies/removed over time for the requested range, every bucket present (0-filled).</summary>
public sealed record ActivityTimelineDto(string Range, IReadOnlyList<ActivityPoint> Points);

/// <summary>One Slack moderator's removed-comment counts across the rolling windows + all time.</summary>
public sealed record ModeratorStat(
    string Name,
    string? SlackUserId,
    int Removed24h,
    int Removed7d,
    int Removed30d,
    int RemovedAllTime);

/// <summary>Moderation breakdown: outcome split (we rejected it vs it was already gone on YouTube),
/// windowed removed totals, and the per-moderator leaderboard.</summary>
public sealed record ModerationStatsDto(
    int TotalRemoved,
    int Rejected,
    int AlreadyGone,
    int Removed24h,
    int Removed7d,
    int Removed30d,
    IReadOnlyList<ModeratorStat> PerModerator);

/// <summary>Durable-retry-queue health. Dead-lettered rows are dropped (not kept), so the queue shows the
/// live backlog: <paramref name="Pending"/> total, the <paramref name="Failing"/> subset that has failed at
/// least once, the <paramref name="NearGiveUp"/> subset one attempt from being dead-lettered, and the
/// <paramref name="OldestPendingAt"/> head-of-line age.</summary>
public sealed record DeliveryHealthDto(
    int Pending,
    int Failing,
    int NearGiveUp,
    DateTimeOffset? OldestPendingAt);

/// <summary>Per-mapping freshness + recent throughput for the health card.</summary>
public sealed record MappingHealthDto(
    Guid MappingId,
    string ChannelTitle,
    string SlackChannelName,
    bool IsActive,
    int FrequencyMinutes,
    DateTimeOffset? LastPolledAt,
    string? LastError,
    int Forwarded24h);

/// <summary>The operational-health panel: the delivery backlog plus a row per mapping.</summary>
public sealed record CommentsHealthDto(DeliveryHealthDto Delivery, IReadOnlyList<MappingHealthDto> Mappings);

/// <summary>One video's forwarded volume + total likes. <paramref name="VideoTitle"/> is null until at least
/// one engagement-enriched comment (post-capture) lands for the video.</summary>
public sealed record VideoStat(string VideoId, string? VideoTitle, int Forwarded, long Likes);

/// <summary>One comment author's forwarded volume + total likes (grouped by their channel URL identity).</summary>
public sealed record AuthorStat(string Name, string? AuthorChannelUrl, int Forwarded, long Likes);

/// <summary>Headline engagement figures. <paramref name="Captured"/> is how many forwarded comments carry the
/// engagement snapshot (rows written before the capture migration are excluded) — so the UI can be honest
/// that averages cover the enriched subset. <paramref name="AvgForwardLatencySeconds"/> is the mean
/// published→forwarded delay; null when nothing enriched is in range.</summary>
public sealed record EngagementSummary(
    int Captured,
    double AvgLikes,
    double AvgCommentLength,
    double? AvgForwardLatencySeconds);

/// <summary>Engagement analytics: headline summary, the busiest videos, and the most-forwarded authors.</summary>
public sealed record EngagementDto(
    EngagementSummary Summary,
    IReadOnlyList<VideoStat> PerVideo,
    IReadOnlyList<AuthorStat> TopAuthors);

/// <summary>One day of the long-horizon history (served from the durable <c>comment_daily_stats</c> rollup).</summary>
public sealed record HistoryPoint(DateOnly Date, int Forwarded, int Replies, int Removed);

/// <summary>Daily forwarded/replies/removed over the last <paramref name="Days"/> COMPLETE days, read from
/// the nightly rollup so it is unaffected by the retention trim of the raw rows. Today is excluded (it is
/// still accumulating and is covered by the live activity timeline).</summary>
public sealed record HistoryDto(int Days, IReadOnlyList<HistoryPoint> Points);

/// <summary>
/// Read-only aggregation behind the detailed Comments dashboard: the forwarded/removed activity timeline
/// (24h hourly · 7d/30d daily), the moderation outcome + per-moderator breakdown, and the delivery/queue
/// + per-mapping health panel. Every query is <c>AsNoTracking</c> and grouped/conditional-counted so a
/// panel is a handful of round-trips, never an N+1. Forwarded comes from <c>processed_comments</c>,
/// removed from the <c>comment_moderations</c> ledger — the same sources the Overview panel uses.
/// </summary>
public sealed class CommentsStatsService(YouTubeCommentsDbContext db, IOptions<YouTubeCommentsOptions> options)
{
    private readonly int _maxAttempts = options.Value.Delivery.MaxAttempts;

    /// <summary>Parses the <c>?range=</c> query value; anything unrecognised falls back to 24h.</summary>
    public static StatsRange ParseRange(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "7d" or "week" => StatsRange.Week,
        "30d" or "month" => StatsRange.Month,
        _ => StatsRange.Day,
    };

    /// <summary>
    /// Forwarded (with its reply subset) and removed counts bucketed across the requested range. Buckets
    /// are pre-seeded so every slot is present in order, 0 where nothing fell in it — the chart never has
    /// to reason about gaps.
    /// </summary>
    public async Task<ActivityTimelineDto> GetActivityAsync(StatsRange range, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var (earliest, count, bucketOf, advance) = BucketPlan(range, now);

        // Two narrow projections over the window, bucketed in memory (the window is small + indexed on the
        // timestamp columns, so this is a bounded scan — not a full-table read).
        var forwarded = await db.ProcessedComments.AsNoTracking()
            .Where(c => c.ProcessedAt >= earliest)
            .Select(c => new { c.ProcessedAt, IsReply = c.ParentCommentId != null })
            .ToListAsync(ct);

        var removed = await db.CommentModerations.AsNoTracking()
            .Where(m => m.CreatedAt >= earliest)
            .Select(m => m.CreatedAt)
            .ToListAsync(ct);

        var fwd = new int[count];
        var rep = new int[count];
        var rem = new int[count];

        foreach (var f in forwarded)
        {
            var i = IndexOf(bucketOf(f.ProcessedAt), earliest, range, count);
            if (i < 0) continue;
            fwd[i]++;
            if (f.IsReply) rep[i]++;
        }

        foreach (var ts in removed)
        {
            var i = IndexOf(bucketOf(ts), earliest, range, count);
            if (i >= 0) rem[i]++;
        }

        var points = new ActivityPoint[count];
        var bucket = earliest;
        for (var i = 0; i < count; i++)
        {
            points[i] = new ActivityPoint(bucket, fwd[i], rep[i], rem[i]);
            bucket = advance(bucket);
        }

        return new ActivityTimelineDto(RangeLabel(range), points);
    }

    /// <summary>Moderation outcome split + per-moderator leaderboard (windowed + all-time).</summary>
    public async Task<ModerationStatsDto> GetModerationAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);
        var since7d = now.AddDays(-7);
        var since30d = now.AddDays(-30);

        // Outcome split + windowed totals — cheap scalar counts off the ledger (the repo's proven idiom).
        var mods = db.CommentModerations.AsNoTracking();
        var total = await mods.CountAsync(ct);
        var rejected = await mods.CountAsync(m => m.Status == CommentModeration.StatusRejected, ct);
        var alreadyGone = await mods.CountAsync(m => m.Status == CommentModeration.StatusAlreadyGone, ct);
        var rem24h = await mods.CountAsync(m => m.CreatedAt >= since24h, ct);
        var rem7d = await mods.CountAsync(m => m.CreatedAt >= since7d, ct);
        var rem30d = await mods.CountAsync(m => m.CreatedAt >= since30d, ct);

        // Per-moderator: group by the immutable Slack user id (the stable identity). The display name is a
        // representative recorded name via Max() — names rarely change, and this is a label only, so we
        // avoid a per-group correlated "latest by CreatedAt" subquery that wouldn't translate cleanly.
        var perModerator = await db.CommentModerations.AsNoTracking()
            .GroupBy(m => m.SlackUserId)
            .Select(g => new
            {
                SlackUserId = g.Key,
                Name = g.Max(m => m.SlackUserName),
                Rem24h = g.Count(m => m.CreatedAt >= since24h),
                Rem7d = g.Count(m => m.CreatedAt >= since7d),
                Rem30d = g.Count(m => m.CreatedAt >= since30d),
                All = g.Count(),
            })
            .OrderByDescending(x => x.All)
            .ToListAsync(ct);

        var moderators = perModerator
            .Select(x => new ModeratorStat(
                Name: string.IsNullOrEmpty(x.Name)
                    ? (string.IsNullOrEmpty(x.SlackUserId) ? "Unknown" : x.SlackUserId)
                    : x.Name!,
                SlackUserId: x.SlackUserId,
                Removed24h: x.Rem24h,
                Removed7d: x.Rem7d,
                Removed30d: x.Rem30d,
                RemovedAllTime: x.All))
            .ToList();

        return new ModerationStatsDto(
            TotalRemoved: total,
            Rejected: rejected,
            AlreadyGone: alreadyGone,
            Removed24h: rem24h,
            Removed7d: rem7d,
            Removed30d: rem30d,
            PerModerator: moderators);
    }

    /// <summary>Delivery-queue backlog + per-mapping freshness/throughput.</summary>
    public async Task<CommentsHealthDto> GetHealthAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);

        // ── Delivery backlog: cheap scalar counts over the (small) pending queue ──
        var nearThreshold = Math.Max(1, _maxAttempts - 1);
        var pending = db.PendingDeliveries.AsNoTracking();
        var pendingCount = await pending.CountAsync(ct);
        var failing = await pending.CountAsync(p => p.AttemptCount > 0, ct);
        var nearGiveUp = await pending.CountAsync(p => p.AttemptCount >= nearThreshold, ct);
        var oldest = pendingCount > 0
            ? await pending.MinAsync(p => (DateTimeOffset?)p.CreatedAt, ct)
            : null;

        var delivery = new DeliveryHealthDto(
            Pending: pendingCount,
            Failing: failing,
            NearGiveUp: nearGiveUp,
            OldestPendingAt: oldest);

        // ── Per-mapping freshness (the columns are already loaded on a normal mappings read) ──
        var mappings = await (
            from m in db.ChannelMappings.AsNoTracking()
            join yt in db.YouTubeChannels.AsNoTracking() on m.YouTubeChannelId equals yt.Id
            join sc in db.SlackChannels.AsNoTracking() on m.SlackChannelId equals sc.Id
            select new
            {
                m.Id,
                ChannelTitle = yt.Title,
                SlackChannelName = sc.Name,
                m.IsActive,
                m.Frequency,
                m.LastPolledAt,
                m.LastError,
            }).ToListAsync(ct);

        // Recent throughput per mapping: one grouped count, folded in by id (no per-mapping round-trip).
        var fwd24hByMapping = await db.ProcessedComments.AsNoTracking()
            .Where(c => c.ProcessedAt >= since24h)
            .GroupBy(c => c.MappingId)
            .Select(g => new { MappingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MappingId, x => x.Count, ct);

        var mappingHealth = mappings
            .Select(m => new MappingHealthDto(
                MappingId: m.Id,
                ChannelTitle: m.ChannelTitle,
                SlackChannelName: m.SlackChannelName,
                IsActive: m.IsActive,
                FrequencyMinutes: (int)m.Frequency,
                LastPolledAt: m.LastPolledAt,
                LastError: m.LastError,
                Forwarded24h: fwd24hByMapping.GetValueOrDefault(m.Id)))
            // Surface trouble first: errored mappings, then stalest poll, then by name.
            .OrderByDescending(m => m.LastError != null)
            .ThenBy(m => m.LastPolledAt ?? DateTimeOffset.MinValue)
            .ThenBy(m => m.ChannelTitle)
            .ToList();

        return new CommentsHealthDto(delivery, mappingHealth);
    }

    /// <summary>
    /// Engagement analytics over the captured snapshot: busiest videos, top authors, and headline averages
    /// (likes, comment length, published→forwarded latency). Per-video forwarded counts are accurate for ALL
    /// rows; likes/title/authors/averages cover only the engagement-enriched subset (rows written after the
    /// capture migration) — <see cref="EngagementSummary.Captured"/> reports that subset's size for honesty.
    /// The latency average is bounded to the last 30 days so it never pulls the full ledger into memory.
    /// </summary>
    public async Task<EngagementDto> GetEngagementAsync(CancellationToken ct = default)
    {
        // ── Per-video: forwarded count for every video, likes/title from the enriched subset ──
        // The like-sum coalesce (?? 0) is applied AFTER materialization so the aggregate stays a plain
        // SUM the provider can translate.
        var perVideoRaw = await db.ProcessedComments.AsNoTracking()
            .GroupBy(c => c.VideoId)
            .Select(g => new
            {
                VideoId = g.Key,
                Title = g.Max(c => c.VideoTitle),
                Forwarded = g.Count(),
                Likes = g.Sum(c => c.LikeCount),
            })
            .OrderByDescending(v => v.Forwarded)
            .ThenByDescending(v => v.Likes)
            .Take(10)
            .ToListAsync(ct);

        var perVideo = perVideoRaw
            .Select(v => new VideoStat(v.VideoId, v.Title, v.Forwarded, v.Likes ?? 0))
            .ToList();

        // ── Top authors: enriched rows only (need both the author identity AND the engagement snapshot,
        //    so the leaderboard matches the documented enriched subset); group by channel URL ──
        var topAuthorsRaw = await db.ProcessedComments.AsNoTracking()
            .Where(c => c.AuthorChannelUrl != null && c.LikeCount != null)
            .GroupBy(c => c.AuthorChannelUrl)
            .Select(g => new
            {
                AuthorChannelUrl = g.Key,
                Name = g.Max(c => c.AuthorName),
                Forwarded = g.Count(),
                Likes = g.Sum(c => c.LikeCount),
            })
            .OrderByDescending(a => a.Forwarded)
            .ThenByDescending(a => a.Likes)
            .Take(10)
            .ToListAsync(ct);

        var topAuthors = topAuthorsRaw
            .Select(a => new AuthorStat(a.Name ?? "Unknown", a.AuthorChannelUrl, a.Forwarded, a.Likes ?? 0))
            .ToList();

        // ── Headline summary over the enriched subset (LikeCount != null is the canonical "enriched" flag;
        //    every average filters on it so they all describe exactly the Captured rows) ──
        var enriched = db.ProcessedComments.AsNoTracking().Where(c => c.LikeCount != null);
        var captured = await enriched.CountAsync(ct);
        var avgLikes = captured > 0 ? await enriched.AverageAsync(c => (double)c.LikeCount!.Value, ct) : 0;
        var avgLength = captured > 0
            ? await enriched.Where(c => c.CommentLength != null)
                .AverageAsync(c => (double)c.CommentLength!.Value, ct)
            : 0;

        // Latency: pull the (small, 30d-bounded) enriched pairs and average published→forwarded in memory
        // (EF can't translate a DateTimeOffset difference to seconds).
        double? avgLatency = null;
        if (captured > 0)
        {
            var since30d = DateTimeOffset.UtcNow.AddDays(-30);
            var pairs = await enriched
                .Where(c => c.PublishedAt != null && c.ProcessedAt >= since30d)
                .Select(c => new { c.PublishedAt, c.ProcessedAt })
                .ToListAsync(ct);

            var deltas = pairs
                .Select(p => (p.ProcessedAt - p.PublishedAt!.Value).TotalSeconds)
                .Where(s => s >= 0)
                .ToList();
            if (deltas.Count > 0)
                avgLatency = Math.Round(deltas.Average(), 1);
        }

        var summary = new EngagementSummary(
            Captured: captured,
            AvgLikes: Math.Round(avgLikes, 1),
            AvgCommentLength: Math.Round(avgLength, 1),
            AvgForwardLatencySeconds: avgLatency);

        return new EngagementDto(summary, perVideo, topAuthors);
    }

    /// <summary>Clamps the <c>?days=</c> history span to 1..365 (default 90).</summary>
    public static int ParseHistoryDays(int? value) => value is null ? 90 : Math.Clamp(value.Value, 1, 365);

    /// <summary>
    /// Long-horizon daily history from the durable <c>comment_daily_stats</c> rollup, 0-filled across every
    /// complete day in range (today is excluded — it is still accumulating and served by the live timeline).
    /// Unaffected by retention because it reads the archive, not the raw rows.
    /// </summary>
    public async Task<HistoryDto> GetHistoryAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var from = yesterday.AddDays(-(days - 1));

        var byDate = (await db.CommentDailyStats.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= yesterday)
            .GroupBy(s => s.Date)
            .Select(g => new
            {
                Date = g.Key,
                Forwarded = g.Sum(s => s.Forwarded),
                Replies = g.Sum(s => s.Replies),
                Removed = g.Sum(s => s.Removed),
            })
            .ToListAsync(ct))
            .ToDictionary(x => x.Date);

        var points = new List<HistoryPoint>(days);
        for (var day = from; day <= yesterday; day = day.AddDays(1))
        {
            var row = byDate.GetValueOrDefault(day);
            points.Add(new HistoryPoint(day, row?.Forwarded ?? 0, row?.Replies ?? 0, row?.Removed ?? 0));
        }

        return new HistoryDto(days, points);
    }

    // ── bucketing helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The earliest bucket start, bucket count, a timestamp→bucket-start mapper, and a next-bucket
    /// stepper for the requested range. 24h aligns to the UTC hour; 7d/30d align to the UTC day.</summary>
    private static (DateTimeOffset Earliest, int Count, Func<DateTimeOffset, DateTimeOffset> BucketOf, Func<DateTimeOffset, DateTimeOffset> Advance)
        BucketPlan(StatsRange range, DateTimeOffset now)
    {
        if (range == StatsRange.Day)
        {
            var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
            return (currentHour.AddHours(-23), 24, HourStart, b => b.AddHours(1));
        }

        var days = range == StatsRange.Week ? 7 : 30;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        return (today.AddDays(-(days - 1)), days, DayStart, b => b.AddDays(1));
    }

    private static DateTimeOffset HourStart(DateTimeOffset ts)
    {
        var u = ts.ToUniversalTime();
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset DayStart(DateTimeOffset ts)
    {
        var u = ts.ToUniversalTime();
        return new DateTimeOffset(u.Year, u.Month, u.Day, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Bucket index of <paramref name="bucketStart"/> within the seeded range, or -1 if out of band.</summary>
    private static int IndexOf(DateTimeOffset bucketStart, DateTimeOffset earliest, StatsRange range, int count)
    {
        var i = range == StatsRange.Day
            ? (int)Math.Round((bucketStart - earliest).TotalHours)
            : (int)Math.Round((bucketStart - earliest).TotalDays);
        return i >= 0 && i < count ? i : -1;
    }

    private static string RangeLabel(StatsRange range) => range switch
    {
        StatsRange.Week => "7d",
        StatsRange.Month => "30d",
        _ => "24h",
    };
}
