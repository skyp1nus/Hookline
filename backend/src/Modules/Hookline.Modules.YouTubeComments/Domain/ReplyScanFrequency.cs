namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// Cadence of the deep reply sweep that catches replies the normal poll misses (replies on older
/// comments, or beyond the few the API returns inline). <see cref="Off"/> disables the sweep entirely
/// — only the free inline replies from the normal poll are forwarded then.
/// </summary>
public enum ReplyScanFrequency
{
    Off = 0,
    Hourly = 60,
    EverySixHours = 360,
    Daily = 1440,
}

public static class ReplyScanFrequencyExtensions
{
    /// <summary>Cron expression (5-field) for the recurring sweep, or <c>null</c> when <see cref="ReplyScanFrequency.Off"/>.</summary>
    public static string? ToCron(this ReplyScanFrequency frequency) => frequency switch
    {
        ReplyScanFrequency.Hourly => "0 * * * *",
        ReplyScanFrequency.EverySixHours => "0 */6 * * *",
        ReplyScanFrequency.Daily => "0 4 * * *",
        _ => null,
    };
}
