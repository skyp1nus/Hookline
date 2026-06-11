namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// Central registry of Redis key shapes so TTL/semantics stay consistent. All keys use the
/// YouTubeUploads prefix <c>ytu:</c> (see docs/redis-key-prefixes.md). The shared Redis runs
/// <c>--maxmemory-policy noeviction</c>, so every key here MUST self-expire.
/// </summary>
public static class RedisKeys
{
    public const string Prefix = "ytu:";

    /// <summary>Slack event_id dedup marker (TTL ~10 min — covers Slack retry window).</summary>
    public static string Dedup(string eventId) => $"{Prefix}dedup:slack:{eventId}";

    /// <summary>Per-job cancellation flag (checked atomically before each worker step). TTL 24h.</summary>
    public static string Cancel(Guid jobId) => $"{Prefix}cancel:job:{jobId}";

    /// <summary>Live Slack status-message <c>ts</c> for a channel (delete+repost on queue change).
    /// Keyed by channel id; refreshed-with-TTL on every write (noeviction → must self-expire).</summary>
    public static string StatusTs(string channelId) => $"{Prefix}status:ts:{channelId}";

    /// <summary>YouTube uploads (<c>videos.insert</c> calls) a project made on a given Pacific-Time
    /// date. videos.insert has its OWN daily bucket per project (Google default 100/day), separate from
    /// the unit pool below — so this counter, not the unit math, gates daily upload capacity. Keyed by the
    /// project id + PT date; every account on the same project shares it; resets at PT midnight.</summary>
    public static string UploadCount(Guid projectId, string ptDate)
        => $"{Prefix}uploads:youtube:{projectId}:{ptDate}";

    /// <summary>YouTube units used by a project (Google Cloud project) on a given Pacific-Time date for
    /// NON-upload endpoints (list/search/etc.) — the separate ~10k/day pool. Keyed by project id + PT
    /// date; shared by every account on the project; resets at PT midnight. Surfaced as an info meter only.</summary>
    public static string Quota(Guid projectId, string ptDate)
        => $"{Prefix}quota:youtube:{projectId}:{ptDate}";

    /// <summary>Generic per-day API usage counter for monitoring daily spend. <paramref name="scope"/> is a
    /// project id (Google APIs) or "slack" (Slack Web API); <paramref name="metric"/> is e.g.
    /// "drive.queries", "drive.bytes", "slack.chat.postMessage". Resets at PT midnight.</summary>
    public static string Usage(string scope, string metric, string ptDate)
        => $"{Prefix}usage:{scope}:{metric}:{ptDate}";

    /// <summary>Per-day SET of all <c>"{scope}|{metric}"</c> pairs seen today — lets the usage reader MGET
    /// every counter without a Redis SCAN. Bounded to PT midnight like the counters it indexes.</summary>
    public static string UsageIndex(string ptDate)
        => $"{Prefix}usage:index:{ptDate}";
}
