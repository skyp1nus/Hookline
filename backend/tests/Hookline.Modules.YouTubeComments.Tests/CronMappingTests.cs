using Hookline.Modules.YouTubeComments.Domain;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The polling cadence + reply-sweep cadence are stored as enum names and translated to cron at
/// schedule time, so the exact member→cron mapping is load-bearing parity behaviour.
/// </summary>
public class CronMappingTests
{
    [Theory]
    [InlineData(PollingFrequency.OneMinute, "* * * * *")]
    [InlineData(PollingFrequency.FiveMinutes, "*/5 * * * *")]
    [InlineData(PollingFrequency.FifteenMinutes, "*/15 * * * *")]
    [InlineData(PollingFrequency.ThirtyMinutes, "*/30 * * * *")]
    [InlineData(PollingFrequency.OneHour, "0 * * * *")]
    [InlineData(PollingFrequency.SixHours, "0 */6 * * *")]
    public void PollingFrequency_maps_to_exact_cron(PollingFrequency freq, string cron) =>
        Assert.Equal(cron, freq.ToCron());

    [Fact]
    public void PollingFrequency_interval_is_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(360), PollingFrequency.SixHours.ToInterval());

    [Theory]
    [InlineData(ReplyScanFrequency.Hourly, "0 * * * *")]
    [InlineData(ReplyScanFrequency.EverySixHours, "0 */6 * * *")]
    [InlineData(ReplyScanFrequency.Daily, "0 4 * * *")]
    public void ReplyScanFrequency_maps_to_exact_cron(ReplyScanFrequency freq, string cron) =>
        Assert.Equal(cron, freq.ToCron());

    [Fact]
    public void ReplyScanFrequency_Off_is_null_sentinel() =>
        Assert.Null(ReplyScanFrequency.Off.ToCron());
}
