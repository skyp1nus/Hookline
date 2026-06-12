using StackExchange.Redis;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Central registry of the module's Redis key shapes (prefix <c>ytc:</c> — see
/// docs/redis-key-prefixes.md). The shared Redis runs <c>--maxmemory-policy noeviction</c>, so every
/// key here MUST self-expire. Durable dedup lives in Postgres (processed_comments); Redis holds only
/// ephemeral fast-path state. The reset path purges this whole prefix.
/// </summary>
public static class RedisKeys
{
    public const string Prefix = "ytc:";

    /// <summary>
    /// Per-Pacific-Time-day counter of the REAL YouTube Data API units this module spent (the live
    /// per-call tally the poll + reply-sweep jobs accumulate: commentThreads.list +1, videos.list +1,
    /// comments.list reply page +1). It is METERED actual spend — not a cadence estimate — read by the
    /// dashboard against <c>YouTubeComments:DailyQuotaUnits</c>. Resets at PT midnight and MUST self-expire
    /// (the shared Redis runs <c>--maxmemory-policy noeviction</c>): writers set a TTL on the first write of
    /// the day. Comments assumes a SINGLE OAuth project, so there is ONE counter for the whole module; if
    /// multiple Google projects are ever exposed to Comments this key must gain a project-id segment
    /// (mirroring the Uploads per-project quota keys).
    /// </summary>
    public static string QuotaUnits(string ptDate) => Prefix + "quota:units:" + ptDate;

    /// <summary>
    /// Best-effort meter of real YouTube Data API units spent THIS Pacific-Time day. Increments
    /// <see cref="QuotaUnits"/> by <paramref name="units"/>; on the FIRST write of the PT day (the
    /// increment returns exactly <paramref name="units"/>, i.e. the key was absent) it bounds the key's
    /// lifetime to just past the next PT midnight so the counter self-expires at the quota reset. Mirrors
    /// the Uploads <c>QuotaService.ChargeUnitsAsync</c> semantics: a non-positive charge is a no-op and any
    /// Redis hiccup is swallowed — metering must NEVER break a poll or sweep.
    /// </summary>
    public static async Task ChargeQuotaUnitsAsync(IConnectionMultiplexer redis, long units)
    {
        if (units <= 0) return;
        try
        {
            var db = redis.GetDatabase();
            var key = QuotaUnits(PacificTime.TodayKey());
            var after = await db.StringIncrementAsync(key, units);
            if (after == units) // first charge of this PT day -> bound the key's lifetime to the reset
                await db.KeyExpireAsync(key, PacificTime.UntilMidnight() + TimeSpan.FromHours(1));
        }
        catch { /* best-effort meter — a Redis hiccup must never break the calling job */ }
    }

    /// <summary>
    /// Reads today's metered unit count (<see cref="QuotaUnits"/>); 0 when the key is absent (no spend yet
    /// today) or on any Redis error — the dashboard renders an honest zero rather than failing the page.
    /// </summary>
    public static async Task<long> ReadQuotaUnitsAsync(IConnectionMultiplexer redis)
    {
        try
        {
            var val = await redis.GetDatabase().StringGetAsync(QuotaUnits(PacificTime.TodayKey()));
            return val.TryParse(out long units) ? units : 0;
        }
        catch { return 0; }
    }
}
