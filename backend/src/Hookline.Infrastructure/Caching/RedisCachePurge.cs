using Hookline.SharedKernel.Caching;

using StackExchange.Redis;

namespace Hookline.Infrastructure.Caching;

/// <summary>
/// Redis-backed <see cref="ICachePurge"/>. Walks each connected server endpoint, SCANs the prefix and
/// deletes the matches. Best-effort by contract: a connection/SCAN failure is swallowed (the shared Redis
/// runs <c>noeviction</c> but every app key self-expires, so a missed purge only delays cleanup — it must
/// never fail a data reset).
/// </summary>
public sealed class RedisCachePurge(IConnectionMultiplexer redis) : ICachePurge
{
    public async Task<long> PurgeByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return 0;
        }

        long removed = 0;
        try
        {
            var db = redis.GetDatabase();
            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                foreach (var key in server.Keys(pattern: prefix + "*", pageSize: 500))
                {
                    ct.ThrowIfCancellationRequested();
                    if (await db.KeyDeleteAsync(key))
                    {
                        removed++;
                    }
                }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Best-effort: a cache outage must never fail the surrounding data reset.
        }

        return removed;
    }
}
