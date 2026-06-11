namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Central registry of the module's Redis key shapes (prefix <c>ytc:</c> — see
/// docs/redis-key-prefixes.md). The shared Redis runs <c>--maxmemory-policy noeviction</c>, so every
/// key here MUST self-expire. Durable dedup + quota live in Postgres (processed_comments / quota_usage);
/// Redis holds only ephemeral fast-path state.
/// </summary>
public static class RedisKeys
{
    public const string Prefix = "ytc:";

    /// <summary>Fast "this key is exhausted for today" cache, sparing a DB read in the hot acquire path.
    /// Authoritative state is still <c>quota_usage</c>; TTL bounded to Pacific midnight.</summary>
    public static string QuotaExhausted(Guid apiKeyId, string ptDate) => $"{Prefix}quota:exhausted:{apiKeyId}:{ptDate}";
}
