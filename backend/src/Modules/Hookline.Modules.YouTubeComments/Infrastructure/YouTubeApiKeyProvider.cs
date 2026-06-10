using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Picks the active API key with the most remaining Pacific-Time daily quota and records usage.
/// Keys come from the shared Connections store (<see cref="IYouTubeApiKeyConnections"/>); the daily
/// counter lives in the module-local <c>quota_usage</c> table. All keys share a uniform daily unit
/// limit from config (<see cref="YouTubeCommentsOptions.DailyQuotaUnits"/>) — the shared key record
/// carries no per-key limit (the YouTube Data API default is 10000/day for every key).
/// </summary>
public sealed class YouTubeApiKeyProvider(
    YouTubeCommentsDbContext db,
    IYouTubeApiKeyConnections keys,
    IOptions<YouTubeCommentsOptions> options) : IYouTubeApiKeyProvider
{
    private readonly int _dailyLimit = options.Value.DailyQuotaUnits;

    public async Task<ApiKeyLease?> AcquireAsync(int unitsNeeded = 1, CancellationToken ct = default)
    {
        var today = PacificTime.Today();

        var active = await keys.ListActiveAsync(ct);
        if (active.Count == 0)
            return null;

        var ids = active.Select(k => k.Id).ToList();
        var usedToday = await db.QuotaUsages
            .AsNoTracking()
            .Where(q => q.UsageDate == today && ids.Contains(q.ApiKeyId))
            .ToDictionaryAsync(q => q.ApiKeyId, q => q.UnitsUsed, ct);

        var best = active
            .Select(k => new { k.Id, k.Name, Remaining = _dailyLimit - usedToday.GetValueOrDefault(k.Id) })
            .Where(c => c.Remaining >= unitsNeeded)
            .OrderByDescending(c => c.Remaining)
            .FirstOrDefault();

        if (best is null)
            return null;

        // Resolve the decrypted key only for the winner.
        var apiKey = await keys.GetApiKeyAsync(best.Id, ct);
        if (string.IsNullOrEmpty(apiKey))
            return null;

        return new ApiKeyLease(best.Id, best.Name, apiKey, best.Remaining);
    }

    public async Task RecordUsageAsync(Guid apiKeyId, int units, CancellationToken ct = default)
    {
        var today = PacificTime.Today();
        var now = DateTimeOffset.UtcNow;

        var usage = await db.QuotaUsages.FirstOrDefaultAsync(q => q.ApiKeyId == apiKeyId && q.UsageDate == today, ct);
        if (usage is null)
        {
            db.QuotaUsages.Add(new QuotaUsage { ApiKeyId = apiKeyId, UsageDate = today, UnitsUsed = units, UpdatedAt = now });
        }
        else
        {
            usage.UnitsUsed += units;
            usage.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public Task MarkInvalidAsync(Guid apiKeyId, CancellationToken ct = default) =>
        // Disable the key in the shared pool; ListActiveAsync then excludes it from rotation.
        keys.ToggleAsync(apiKeyId, isActive: false, ct);

    public async Task MarkExhaustedAsync(Guid apiKeyId, CancellationToken ct = default)
    {
        var today = PacificTime.Today();
        var now = DateTimeOffset.UtcNow;

        // Pin today's usage at (or above) the daily limit so AcquireAsync's remaining-quota filter
        // skips this key until the Pacific-Time day rolls over.
        var usage = await db.QuotaUsages.FirstOrDefaultAsync(q => q.ApiKeyId == apiKeyId && q.UsageDate == today, ct);
        if (usage is null)
        {
            db.QuotaUsages.Add(new QuotaUsage { ApiKeyId = apiKeyId, UsageDate = today, UnitsUsed = _dailyLimit, UpdatedAt = now });
        }
        else if (usage.UnitsUsed < _dailyLimit)
        {
            usage.UnitsUsed = _dailyLimit;
            usage.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
