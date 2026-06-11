using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Multiple API keys are rotated by REMAINING Pacific-Time daily quota (highest-remaining wins, not
/// round-robin). A key reported exhausted is skipped until the PT day rolls over; when none qualify,
/// no lease is issued. Quota is tracked in the module-local <c>quota_usage</c> table keyed by the
/// Pacific day; keys + decrypted material come from the shared Connections accessor.
/// </summary>
public class QuotaRotationTests
{
    private static YouTubeApiKeyProvider Provider(YouTubeCommentsDbContext db, FakeKeyConnections keys, int limit = 10000) =>
        new(db, keys, Options.Create(new YouTubeCommentsOptions { DailyQuotaUnits = limit }));

    private static async Task SetUsedAsync(YouTubeCommentsDbContext db, Guid keyId, int used)
    {
        db.QuotaUsages.Add(new QuotaUsage { ApiKeyId = keyId, UsageDate = PacificTime.Today(), UnitsUsed = used });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Acquire_picks_key_with_most_remaining_quota()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var low = keys.Seed("low", active: true, key: "KEY-LOW");    // listed first, less headroom
        var high = keys.Seed("high", active: true, key: "KEY-HIGH"); // most headroom
        await SetUsedAsync(db, low, 9000);
        await SetUsedAsync(db, high, 10);

        var lease = await Provider(db, keys).AcquireAsync();

        Assert.NotNull(lease);
        Assert.Equal(high, lease!.Id);
        Assert.Equal("KEY-HIGH", lease.ApiKey); // decrypted key resolved for the winner only
        Assert.Equal(10000 - 10, lease.RemainingQuota);
    }

    [Fact]
    public async Task MarkExhausted_pins_usage_so_next_acquire_skips_the_key()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var only = keys.Seed("only", active: true);

        var provider = Provider(db, keys);
        await provider.MarkExhaustedAsync(only);

        Assert.Null(await provider.AcquireAsync()); // pinned at the daily limit → no remaining quota
        var row = await db.QuotaUsages.SingleAsync();
        Assert.Equal(10000, row.UnitsUsed);
    }

    [Fact]
    public async Task MarkInvalid_deactivates_the_key_so_it_leaves_rotation()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var dead = keys.Seed("revoked", active: true);
        var alive = keys.Seed("good", active: true, key: "KEY-GOOD");

        var provider = Provider(db, keys);
        await provider.MarkInvalidAsync(dead); // YouTube rejected it (e.g. keyInvalid)

        var lease = await provider.AcquireAsync();
        Assert.NotNull(lease);
        Assert.Equal(alive, lease!.Id); // the dead key is gone from the candidate set; the good one wins

        await provider.MarkInvalidAsync(alive);
        Assert.Null(await provider.AcquireAsync()); // no active keys left → no lease, degrades gracefully
    }

    [Fact]
    public async Task RecordUsage_accumulates_for_the_pacific_day()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var id = keys.Seed("k", active: true);
        var provider = Provider(db, keys);

        await provider.RecordUsageAsync(id, 3);
        await provider.RecordUsageAsync(id, 4);

        var row = await db.QuotaUsages.SingleAsync(q => q.ApiKeyId == id && q.UsageDate == PacificTime.Today());
        Assert.Equal(7, row.UnitsUsed);
    }

    [Fact]
    public async Task Acquire_returns_null_when_all_keys_exhausted()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var a = keys.Seed("a", active: true);
        var b = keys.Seed("b", active: true);
        await SetUsedAsync(db, a, 10000);
        await SetUsedAsync(db, b, 10000);

        Assert.Null(await Provider(db, keys).AcquireAsync());
    }

    [Fact]
    public async Task Acquire_ignores_inactive_keys()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        keys.Seed("disabled", active: false); // plenty of quota but disabled

        Assert.Null(await Provider(db, keys).AcquireAsync());
    }

    [Fact]
    public async Task Acquire_respects_units_needed()
    {
        using var db = TestDb.Create();
        var keys = new FakeKeyConnections();
        var id = keys.Seed("k", active: true);
        await SetUsedAsync(db, id, 9950); // only 50 left

        Assert.Null(await Provider(db, keys).AcquireAsync(unitsNeeded: 101)); // can't satisfy a /c/ lookup
        Assert.NotNull(await Provider(db, keys).AcquireAsync(unitsNeeded: 1));
    }
}
