namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Single source of truth for the Pacific-Time "quota day". YouTube's Data API quota resets at
/// midnight Pacific, so the dashboard's estimated-quota meter and the "comments today" filter both bucket
/// on this date. One TZ computation for the whole module.
/// </summary>
public static class PacificTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Today's Pacific calendar date — matches YouTube's daily quota-reset boundary.</summary>
    public static DateOnly Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    /// <summary>The exact UTC instant the current Pacific day began (for <c>ProcessedAt &gt;= start</c> "today" filters).</summary>
    public static DateTimeOffset StartOfToday()
    {
        var midnight = Today().ToDateTime(TimeOnly.MinValue); // unspecified-kind PT wall clock
        var offset = Zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset).ToUniversalTime();
    }

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
    }
}
