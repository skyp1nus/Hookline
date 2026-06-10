namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>Polling cadence. Underlying value is the interval in minutes.</summary>
public enum PollingFrequency
{
    OneMinute = 1,
    FiveMinutes = 5,
    FifteenMinutes = 15,
    ThirtyMinutes = 30,
    OneHour = 60,
    SixHours = 360,
}

public static class PollingFrequencyExtensions
{
    /// <summary>Cron expression (5-field) for the recurring poll job.</summary>
    public static string ToCron(this PollingFrequency frequency) => frequency switch
    {
        PollingFrequency.OneMinute => "* * * * *",
        PollingFrequency.FiveMinutes => "*/5 * * * *",
        PollingFrequency.FifteenMinutes => "*/15 * * * *",
        PollingFrequency.ThirtyMinutes => "*/30 * * * *",
        PollingFrequency.OneHour => "0 * * * *",
        PollingFrequency.SixHours => "0 */6 * * *",
        _ => "*/15 * * * *",
    };

    public static TimeSpan ToInterval(this PollingFrequency frequency)
        => TimeSpan.FromMinutes((int)frequency);
}
