namespace Hookline.SharedKernel.Common;

/// <summary>
/// Single source of truth for the Pacific-Time "quota day". YouTube's Data API quota resets at
/// midnight Pacific, so every module's metered-quota meter, per-day units counter, and daily filters
/// bucket on this date. One TZ computation shared across the entire solution.
/// </summary>
public static class PacificTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Today's Pacific calendar date — matches YouTube's daily quota-reset boundary.</summary>
    public static DateOnly Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    /// <summary>Today's PT date as <c>yyyy-MM-dd</c> — the suffix of per-day metered-units Redis keys.</summary>
    public static string TodayKey()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).ToString("yyyy-MM-dd");

    /// <summary>The exact UTC instant the current Pacific day began (for "today" filters).</summary>
    public static DateTimeOffset StartOfToday()
    {
        var midnight = Today().ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset).ToUniversalTime();
    }

    /// <summary>Time remaining until the next PT midnight (used to bound Redis key lifetimes so they
    /// self-expire at the quota reset). Computes the next-midnight instant with its own zone offset so
    /// the span stays correct across DST transitions.</summary>
    public static TimeSpan UntilMidnight()
    {
        var nowPt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
        var nextMidnightLocal = nowPt.Date.AddDays(1);
        var nextMidnight = new DateTimeOffset(nextMidnightLocal, Zone.GetUtcOffset(nextMidnightLocal));
        return nextMidnight - nowPt;
    }

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
    }
}
