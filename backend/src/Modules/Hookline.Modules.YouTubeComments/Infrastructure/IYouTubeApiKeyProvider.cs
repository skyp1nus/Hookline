namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// A leased API key handed to a caller for a unit of work, along with its decrypted key material
/// and the remaining daily quota at the moment it was acquired.
/// </summary>
public sealed record ApiKeyLease(Guid Id, string Name, string ApiKey, int RemainingQuota);

/// <summary>
/// Selects an active API key with the most remaining Pacific-Time daily quota and records consumption.
/// The key identity + secret live in the shared Connections store; the per-key daily accounting lives
/// in the module-local <c>quota_usage</c> table, keyed by the Pacific-Time day to match YouTube's reset.
/// </summary>
public interface IYouTubeApiKeyProvider
{
    /// <summary>
    /// Acquires the key with the most remaining quota that still has at least
    /// <paramref name="unitsNeeded"/> units available today, or <c>null</c> when none qualify.
    /// </summary>
    Task<ApiKeyLease?> AcquireAsync(int unitsNeeded = 1, CancellationToken ct = default);

    /// <summary>Records that <paramref name="units"/> quota units were consumed against the given key today.</summary>
    Task RecordUsageAsync(Guid apiKeyId, int units, CancellationToken ct = default);

    /// <summary>
    /// Marks the key as exhausted for the rest of the current Pacific-Time day by pinning today's
    /// usage to the daily quota limit, so subsequent <see cref="AcquireAsync"/> calls skip it (used
    /// when YouTube reports <c>quotaExceeded</c> before our local counter reached the limit).
    /// </summary>
    Task MarkExhaustedAsync(Guid apiKeyId, CancellationToken ct = default);
}
