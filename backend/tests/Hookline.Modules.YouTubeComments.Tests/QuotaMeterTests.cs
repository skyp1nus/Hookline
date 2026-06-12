using System.Reflection;

using Hookline.Modules.YouTubeComments;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Options;

using StackExchange.Redis;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The dashboard quota figure is METERED from the REAL YouTube Data API units the poll + reply-sweep jobs
/// spent today — a single per-Pacific-Time-day Redis counter (one OAuth project ⇒ one counter), read by
/// <see cref="DashboardService.GetStatsAsync"/> against <c>DailyQuotaUnits</c>. These tests prove the
/// charge → read round-trip (using the SAME <see cref="RedisKeys.ChargeQuotaUnitsAsync"/> the jobs call),
/// the percentage math, and that the counter self-expires at the PT-midnight reset (a TTL is set on the
/// first write of the day — required because the shared Redis runs <c>--maxmemory-policy noeviction</c>).
/// </summary>
public class QuotaMeterTests
{
    [Fact]
    public void QuotaUnitsKey_is_ytc_prefixed_and_per_pt_day()
    {
        var key = RedisKeys.QuotaUnits("2026-06-12");

        Assert.StartsWith("ytc:", key);                                  // YouTubeComments prefix preserved
        Assert.Contains("quota:units:", key);
        Assert.Contains("2026-06-12", key);
        Assert.NotEqual(key, RedisKeys.QuotaUnits("2026-06-13"));        // different PT day ⇒ different counter
    }

    [Fact]
    public async Task Charge_then_read_round_trips_the_metered_total()
    {
        var redis = new FakeRedis();

        // Two runs spend real units (e.g. a poll = commentThreads.list + videos.list; a sweep = paged
        // scan + reply pages). The meter accumulates them under today's PT key.
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 2);
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 33);

        var read = await RedisKeys.ReadQuotaUnitsAsync(redis.Mux);
        Assert.Equal(35, read);
        Assert.Equal(35, redis.Get(RedisKeys.QuotaUnits(PacificTime.TodayKey())));
    }

    [Fact]
    public async Task Charge_ignores_non_positive_and_read_of_absent_key_is_zero()
    {
        var redis = new FakeRedis();

        Assert.Equal(0, await RedisKeys.ReadQuotaUnitsAsync(redis.Mux)); // nothing spent yet today
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 0);             // no-op
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, -5);            // no-op
        Assert.Equal(0, await RedisKeys.ReadQuotaUnitsAsync(redis.Mux));
        Assert.False(redis.Exists(RedisKeys.QuotaUnits(PacificTime.TodayKey())));
    }

    [Fact]
    public async Task First_charge_of_the_day_sets_a_self_expiring_ttl()
    {
        var redis = new FakeRedis();
        var key = RedisKeys.QuotaUnits(PacificTime.TodayKey());

        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 2);
        var ttlAfterFirst = redis.Ttl(key);
        Assert.True(ttlAfterFirst.HasValue, "first charge must bound the key's lifetime (noeviction Redis)");

        // TTL covers up to the next PT midnight plus the 1h grace, and is strictly positive.
        Assert.True(ttlAfterFirst!.Value > TimeSpan.Zero);
        Assert.True(ttlAfterFirst.Value <= PacificTime.UntilMidnight() + TimeSpan.FromHours(1) + TimeSpan.FromSeconds(5));

        // A later charge the same day must NOT reset/extend the TTL (only the first write sets it).
        redis.ClearTtl(key);
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 5);
        Assert.False(redis.Ttl(key).HasValue, "subsequent same-day charge must not re-arm the TTL");
        Assert.Equal(7, redis.Get(key));
    }

    [Fact]
    public async Task Dashboard_reports_the_metered_units_and_percent()
    {
        var redis = new FakeRedis();
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 250); // real spend so far today

        using var db = TestDb.Create();
        var options = Options.Create(new YouTubeCommentsOptions { DailyQuotaUnits = 1000 });
        var service = new DashboardService(db, new FakeSlackConnections(), new StubAuditLogReader(), redis.Mux, options);

        var stats = await service.GetStatsAsync();

        Assert.Equal(1000, stats.QuotaCeiling);
        Assert.Equal(250, stats.EstimatedDailyUnits);      // metered actual, not a cadence guess
        Assert.Equal(25.0, stats.EstimatedPercent);        // 250 / 1000 = 25%
    }

    [Fact]
    public async Task Dashboard_percent_clamps_at_100_when_spend_exceeds_the_ceiling()
    {
        var redis = new FakeRedis();
        await RedisKeys.ChargeQuotaUnitsAsync(redis.Mux, 12000); // overspent the 10k ceiling

        using var db = TestDb.Create();
        var options = Options.Create(new YouTubeCommentsOptions { DailyQuotaUnits = 10000 });
        var service = new DashboardService(db, new FakeSlackConnections(), new StubAuditLogReader(), redis.Mux, options);

        var stats = await service.GetStatsAsync();

        Assert.Equal(12000, stats.EstimatedDailyUnits);
        Assert.Equal(100.0, stats.EstimatedPercent); // clamped, never > 100
    }

    [Fact]
    public async Task Dashboard_reports_zero_when_nothing_metered_yet()
    {
        var redis = new FakeRedis(); // no charges today

        using var db = TestDb.Create();
        var options = Options.Create(new YouTubeCommentsOptions { DailyQuotaUnits = 10000 });
        var service = new DashboardService(db, new FakeSlackConnections(), new StubAuditLogReader(), redis.Mux, options);

        var stats = await service.GetStatsAsync();

        Assert.Equal(0, stats.EstimatedDailyUnits);
        Assert.Equal(0d, stats.EstimatedPercent);
    }

    // ---------------------------------------------------------------------------------------------
    // Minimal in-memory Redis stand-in. The metering surface the module touches is just three calls:
    // StringIncrementAsync, KeyExpireAsync, StringGetAsync (via IConnectionMultiplexer.GetDatabase()).
    // IDatabase / IConnectionMultiplexer are huge interfaces, so we synthesize them with DispatchProxy
    // and only handle those three methods — anything else is never invoked by the code under test. The
    // synthesized multiplexer is exposed via .Mux (user-defined conversions TO an interface are illegal).
    // ---------------------------------------------------------------------------------------------
    private sealed class FakeRedis
    {
        private readonly RedisStore _store = new();
        private readonly IConnectionMultiplexer _mux;

        public FakeRedis() => _mux = FakeMultiplexer.Create(_store);

        /// <summary>The synthesized multiplexer — pass this into any <see cref="IConnectionMultiplexer"/>
        /// parameter (the metering helpers + the DashboardService ctor).</summary>
        public IConnectionMultiplexer Mux => _mux;

        public long Get(string key) => _store.Values.GetValueOrDefault(key);
        public bool Exists(string key) => _store.Values.ContainsKey(key);
        public TimeSpan? Ttl(string key) => _store.Ttls.TryGetValue(key, out var t) ? t : null;
        public void ClearTtl(string key) => _store.Ttls.Remove(key);
    }

    /// <summary>Backing dictionary + TTL map shared between the proxied database and the assertions.</summary>
    internal sealed class RedisStore
    {
        public readonly Dictionary<string, long> Values = new(StringComparer.Ordinal);
        public readonly Dictionary<string, TimeSpan> Ttls = new(StringComparer.Ordinal);
    }

    /// <summary>DispatchProxy for <see cref="IConnectionMultiplexer"/> — only <c>GetDatabase</c> is handled.</summary>
    public class FakeMultiplexer : DispatchProxy
    {
        private IDatabase _db = null!;

        internal static IConnectionMultiplexer Create(RedisStore store)
        {
            var proxy = (FakeMultiplexer)(object)Create<IConnectionMultiplexer, FakeMultiplexer>()!;
            proxy._db = FakeDatabase.Create(store);
            return (IConnectionMultiplexer)(object)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase) ? _db : null;
    }

    /// <summary>DispatchProxy for <see cref="IDatabase"/> — handles the three metering calls only.</summary>
    public class FakeDatabase : DispatchProxy
    {
        private RedisStore _store = null!;

        internal static IDatabase Create(RedisStore store)
        {
            var proxy = (FakeDatabase)(object)Create<IDatabase, FakeDatabase>()!;
            proxy._store = store;
            return (IDatabase)(object)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IDatabaseAsync.StringIncrementAsync):
                {
                    var key = ((RedisKey)args![0]!).ToString()!;
                    var by = (long)args[1]!;
                    var after = _store.Values.GetValueOrDefault(key) + by;
                    _store.Values[key] = after;
                    return Task.FromResult(after);
                }
                case nameof(IDatabaseAsync.KeyExpireAsync):
                {
                    var key = ((RedisKey)args![0]!).ToString()!;
                    if (args[1] is TimeSpan ttl) _store.Ttls[key] = ttl;
                    return Task.FromResult(true);
                }
                case nameof(IDatabaseAsync.StringGetAsync):
                {
                    var key = ((RedisKey)args![0]!).ToString()!;
                    var value = _store.Values.TryGetValue(key, out var v) ? (RedisValue)v : RedisValue.Null;
                    return Task.FromResult(value);
                }
                default:
                    return null; // not invoked by the code under test
            }
        }
    }
}
