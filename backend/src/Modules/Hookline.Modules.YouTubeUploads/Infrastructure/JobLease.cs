using StackExchange.Redis;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>Exclusive per-job execution lease in Redis, so a re-delivered Hangfire job can never run
/// alongside the original. Held for the whole run and renewed in the background; expires on its own
/// if the process dies.</summary>
public interface IJobLease
{
    /// <summary>The held lease, or null when another execution already owns this job.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(Guid jobId);
}

public sealed class JobLease(IConnectionMultiplexer redis) : IJobLease
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(30);

    // Single backend instance: a lease stamped by an earlier process was orphaned by a kill.
    private static readonly string ProcessId = Guid.NewGuid().ToString("N");

    private const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private const string TakeoverScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('set', KEYS[1], ARGV[2], 'PX', ARGV[3]) else return false end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid jobId)
    {
        var db = redis.GetDatabase();
        var key = RedisKeys.Lease(jobId);
        var token = $"{ProcessId}:{Guid.NewGuid():N}";
        if (await db.StringSetAsync(key, token, Ttl, When.NotExists)) return new Held(db, key, token);

        var owner = (string?)await db.StringGetAsync(key);
        if (owner is null || owner.StartsWith(ProcessId, StringComparison.Ordinal)) return null;
        var took = await db.ScriptEvaluateAsync(TakeoverScript, [key], [owner, token, (long)Ttl.TotalMilliseconds]);
        return took.IsNull ? null : new Held(db, key, token);
    }

    private sealed class Held : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _renewLoop;

        public Held(IDatabase db, RedisKey key, RedisValue token)
        {
            _db = db;
            _key = key;
            _token = token;
            _renewLoop = RenewAsync();
        }

        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(RenewEvery);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    try { await _db.ScriptEvaluateAsync(RenewScript, [_key], [_token, (long)Ttl.TotalMilliseconds]); }
                    catch (Exception ex) when (ex is RedisException or TimeoutException) { /* the next tick retries well inside the TTL */ }
                }
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _renewLoop;
            _stop.Dispose();
            try { await _db.ScriptEvaluateAsync(ReleaseScript, [_key], [_token]); }
            catch (Exception ex) when (ex is RedisException or TimeoutException) { /* expires via TTL */ }
        }
    }
}
