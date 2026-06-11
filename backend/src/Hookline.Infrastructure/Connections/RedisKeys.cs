namespace Hookline.Infrastructure.Connections;

/// <summary>
/// Redis key-prefix registry. One shared Redis; every key is namespaced per owner so
/// modules can't collide. Keep this in sync with docs/redis-key-prefixes.md.
/// <list type="bullet">
///   <item><c>conn:*</c> — Connections (OAuth state, transient caches)</item>
///   <item><c>auth:*</c> — Auth (session/rate-limit helpers)</item>
///   <item><c>ytu:*</c> — YouTube Uploads module</item>
///   <item><c>ytc:*</c> — YouTube Comments module</item>
/// </list>
/// </summary>
public static class RedisKeys
{
    public const string ConnectionsPrefix = "conn:";
    public const string AuthPrefix = "auth:";

    public static string OAuthState(string provider, string state) => $"{ConnectionsPrefix}oauth:{provider}:{state}";
}
