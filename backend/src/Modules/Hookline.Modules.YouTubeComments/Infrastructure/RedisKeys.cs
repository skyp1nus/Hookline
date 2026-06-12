namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Central registry of the module's Redis key shapes (prefix <c>ytc:</c> — see
/// docs/redis-key-prefixes.md). The shared Redis runs <c>--maxmemory-policy noeviction</c>, so every
/// key here MUST self-expire. Durable dedup lives in Postgres (processed_comments); Redis holds only
/// ephemeral fast-path state. The reset path purges this whole prefix.
/// </summary>
public static class RedisKeys
{
    public const string Prefix = "ytc:";
}
