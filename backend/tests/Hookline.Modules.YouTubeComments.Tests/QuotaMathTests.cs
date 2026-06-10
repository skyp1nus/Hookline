using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The dashboard quota meter must measure usage against the SAME key set it measures capacity against
/// (the active keys). Capacity is active-keys × the uniform per-key ceiling; summing usage over only those
/// keys keeps numerator and denominator consistent so a disabled/removed key that still has usage recorded
/// for today can't distort the percentage, and the percent is clamped to 100.
/// </summary>
public class QuotaMathTests
{
    private static DashboardService Build(YouTubeCommentsDbContext db, FakeKeyConnections keys, int dailyLimit = 10000) =>
        new(db, keys, new FakeSlackConnections(), new StubAuditLogReader(),
            Options.Create(new YouTubeCommentsOptions { DailyQuotaUnits = dailyLimit }));

    private static void SeedUsage(YouTubeCommentsDbContext db, Guid keyId, int units) =>
        db.QuotaUsages.Add(new QuotaUsage { ApiKeyId = keyId, UsageDate = PacificTime.Today(), UnitsUsed = units });

    [Fact]
    public async Task Capacity_and_usage_are_measured_over_active_keys_only()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var active1 = keys.Seed("k1", active: true);
        var active2 = keys.Seed("k2", active: true);
        var disabled = keys.Seed("k3", active: false);

        SeedUsage(db, active1, 3000);
        SeedUsage(db, active2, 2000);
        SeedUsage(db, disabled, 9999); // must NOT count — its key isn't part of the active capacity
        await db.SaveChangesAsync();

        var stats = await Build(db, keys).GetStatsAsync();

        Assert.Equal(20000, stats.TotalQuotaLimit);        // 2 active × 10000
        Assert.Equal(5000, stats.TotalQuotaUsedToday);     // 3000 + 2000, disabled key excluded
        Assert.Equal(25.0, stats.QuotaUsedPercent);
        Assert.Equal(3, stats.ApiKeyCount);                // count is over all keys
    }

    [Fact]
    public async Task Percent_is_clamped_to_100_when_a_key_overshoots_its_ceiling()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var only = keys.Seed("k1", active: true);
        SeedUsage(db, only, 15000); // a final RecordUsage overshot the 10000 ceiling
        await db.SaveChangesAsync();

        var stats = await Build(db, keys).GetStatsAsync();

        Assert.Equal(10000, stats.TotalQuotaLimit);
        Assert.Equal(15000, stats.TotalQuotaUsedToday); // report the real usage…
        Assert.Equal(100.0, stats.QuotaUsedPercent);    // …but never a meter past 100%
    }

    [Fact]
    public async Task No_active_keys_yields_zero_capacity_and_zero_percent()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var disabled = keys.Seed("k1", active: false);
        SeedUsage(db, disabled, 4000);
        await db.SaveChangesAsync();

        var stats = await Build(db, keys).GetStatsAsync();

        Assert.Equal(0, stats.TotalQuotaLimit);
        Assert.Equal(0, stats.TotalQuotaUsedToday);
        Assert.Equal(0, stats.QuotaUsedPercent); // guarded divide-by-zero
    }
}
