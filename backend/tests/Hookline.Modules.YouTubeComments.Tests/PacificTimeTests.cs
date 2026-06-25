using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

public sealed class PacificTimeTests
{
    [Fact]
    public void Today_returns_valid_date()
    {
        var today = PacificTime.Today();

        Assert.True(today >= new DateOnly(2020, 1, 1));
        Assert.True(today <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void TodayKey_matches_yyyy_MM_dd_format()
    {
        var key = PacificTime.TodayKey();

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", key);
    }

    [Fact]
    public void TodayKey_parses_back_to_Today()
    {
        var key = PacificTime.TodayKey();
        var parsed = DateOnly.ParseExact(key, "yyyy-MM-dd");

        Assert.Equal(PacificTime.Today(), parsed);
    }

    [Fact]
    public void StartOfToday_is_in_the_past()
    {
        var start = PacificTime.StartOfToday();

        Assert.True(start <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void StartOfToday_is_within_last_24_hours()
    {
        var start = PacificTime.StartOfToday();

        Assert.True(DateTimeOffset.UtcNow - start < TimeSpan.FromHours(25));
    }

    [Fact]
    public void UntilMidnight_is_positive()
    {
        var remaining = PacificTime.UntilMidnight();

        Assert.True(remaining > TimeSpan.Zero);
    }

    [Fact]
    public void UntilMidnight_is_less_than_25_hours()
    {
        var remaining = PacificTime.UntilMidnight();

        Assert.True(remaining < TimeSpan.FromHours(25));
    }

    [Fact]
    public void StartOfToday_plus_UntilMidnight_is_roughly_next_midnight()
    {
        var start = PacificTime.StartOfToday();
        var remaining = PacificTime.UntilMidnight();
        var now = DateTimeOffset.UtcNow;
        var nextMidnightApprox = now + remaining;

        Assert.True(nextMidnightApprox > start);
        Assert.True(nextMidnightApprox - start >= TimeSpan.FromHours(23));
        Assert.True(nextMidnightApprox - start <= TimeSpan.FromHours(26));
    }
}
