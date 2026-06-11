namespace Hookline.SharedKernel.Caching;

/// <summary>
/// Best-effort cache invalidation by key prefix. Backed by Redis in production; lets a module clear its
/// own <c>ytu:*</c> / <c>ytc:*</c> namespace during a data reset without taking a direct
/// StackExchange.Redis dependency (keeping the module → SharedKernel-only boundary). Every key the modules
/// write self-expires, so a missed purge (cache unreachable) is non-fatal — implementations MUST NOT throw
/// for a cache outage.
/// </summary>
public interface ICachePurge
{
    /// <summary>
    /// Delete every key starting with <paramref name="prefix"/>. Returns the number removed (0 if the cache
    /// was unreachable). Never throws for a cache outage.
    /// </summary>
    Task<long> PurgeByPrefixAsync(string prefix, CancellationToken ct = default);
}
