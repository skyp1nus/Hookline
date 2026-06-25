using Hookline.Modules.YouTubeComments.Domain;

namespace Hookline.Modules.YouTubeComments.Tests;

public sealed class PollingFrequencyTests
{
    [Theory]
    [InlineData(PollingFrequency.OneMinute, "* * * * *")]
    [InlineData(PollingFrequency.FiveMinutes, "*/5 * * * *")]
    [InlineData(PollingFrequency.FifteenMinutes, "*/15 * * * *")]
    [InlineData(PollingFrequency.ThirtyMinutes, "*/30 * * * *")]
    [InlineData(PollingFrequency.OneHour, "0 * * * *")]
    [InlineData(PollingFrequency.SixHours, "0 */6 * * *")]
    public void ToCron_maps_correctly(PollingFrequency frequency, string expected)
    {
        Assert.Equal(expected, frequency.ToCron());
    }

    [Theory]
    [InlineData(PollingFrequency.OneMinute, 1)]
    [InlineData(PollingFrequency.FiveMinutes, 5)]
    [InlineData(PollingFrequency.FifteenMinutes, 15)]
    [InlineData(PollingFrequency.ThirtyMinutes, 30)]
    [InlineData(PollingFrequency.OneHour, 60)]
    [InlineData(PollingFrequency.SixHours, 360)]
    public void ToInterval_returns_correct_minutes(PollingFrequency frequency, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), frequency.ToInterval());
    }
}

public sealed class ReplyScanFrequencyTests
{
    [Theory]
    [InlineData(ReplyScanFrequency.Hourly, "0 * * * *")]
    [InlineData(ReplyScanFrequency.EverySixHours, "0 */6 * * *")]
    [InlineData(ReplyScanFrequency.Daily, "0 4 * * *")]
    public void ToCron_maps_correctly(ReplyScanFrequency frequency, string expected)
    {
        Assert.Equal(expected, frequency.ToCron());
    }

    [Fact]
    public void ToCron_returns_null_for_Off()
    {
        Assert.Null(ReplyScanFrequency.Off.ToCron());
    }
}
