using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>Aggregated KPIs for the dashboard landing page.</summary>
public sealed record DashboardStatsDto(
    int ActiveMappings,
    int TotalMappings,
    int CommentsToday,
    int CommentsLast24h,
    long TotalQuotaLimit,
    long TotalQuotaUsedToday,
    double QuotaUsedPercent,
    int ErrorsLast24h,
    int ConnectedWorkspaces,
    int ApiKeyCount,
    int ChannelCount);

/// <summary>A single hour bucket in the "comments processed" timeline. <paramref name="Bucket"/> is the start of the UTC hour.</summary>
public sealed record CommentsTimelinePoint(DateTimeOffset Bucket, int Count);

/// <summary>
/// Read-only aggregation over the operational tables for the dashboard. "Today" boundaries use Pacific
/// Time (matching quota tracking); rolling windows use the last 24 hours from <see cref="DateTimeOffset.UtcNow"/>.
/// API keys + Slack workspaces are counted via the shared Connections accessors.
/// </summary>
public sealed class DashboardService(
    YouTubeCommentsDbContext db,
    IYouTubeApiKeyConnections keys,
    ISlackConnections slackConnections,
    IAuditLogReader auditLog,
    IOptions<YouTubeCommentsOptions> options)
{
    private readonly int _dailyLimit = options.Value.DailyQuotaUnits;

    /// <summary>Computes the dashboard KPI snapshot in a handful of aggregate queries.</summary>
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);
        var startOfTodayPt = PacificTime.StartOfToday();
        var todayPt = PacificTime.Today();

        var totalMappings = await db.ChannelMappings.AsNoTracking().CountAsync(ct);
        var activeMappings = await db.ChannelMappings.AsNoTracking().CountAsync(m => m.IsActive, ct);

        var commentsToday = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= startOfTodayPt, ct);
        var commentsLast24h = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= since24h, ct);

        var allKeys = await keys.ListAsync(ct);
        // Meter capacity and usage over the SAME key set (the active keys). The limit is a uniform
        // per-key ceiling × active-key count; summing usage over only those keys keeps the numerator and
        // denominator consistent, so a disabled/removed key that still has usage recorded for today
        // can't push the meter past 100%. Clamp defensively (a final RecordUsage can overshoot a key's
        // ceiling by one call's units).
        var activeKeyIds = allKeys.Where(k => k.IsActive).Select(k => k.Id).ToList();
        var activeKeyCount = activeKeyIds.Count;
        var totalQuotaLimit = (long)activeKeyCount * _dailyLimit;
        var totalQuotaUsedToday = activeKeyCount == 0
            ? 0L
            : await db.QuotaUsages.AsNoTracking()
                .Where(q => q.UsageDate == todayPt && activeKeyIds.Contains(q.ApiKeyId))
                .SumAsync(q => (long)q.UnitsUsed, ct);
        var quotaUsedPercent = totalQuotaLimit > 0
            ? Math.Round(Math.Min(100d, (double)totalQuotaUsedToday / totalQuotaLimit * 100), 1)
            : 0;

        // Audit lives in the shared trail; count this module's error-level rows (the level is folded
        // into the detail as a "[Error] …" marker — see CommentsAudit) in the last 24h so the KPI
        // reflects reality instead of a flat 0. The prefix comes from the same builder the writer uses.
        var errorsLast24h = await auditLog.CountSinceAsync(
            CommentsAudit.ModuleName, since24h, detailPrefix: CommentsAudit.DetailPrefix(AuditLevel.Error), ct);

        var workspaces = await slackConnections.ListAsync(ct);
        var connectedWorkspaces = workspaces.Count(w => w.IsActive);
        var channelCount = await db.YouTubeChannels.AsNoTracking().CountAsync(ct);

        return new DashboardStatsDto(
            ActiveMappings: activeMappings,
            TotalMappings: totalMappings,
            CommentsToday: commentsToday,
            CommentsLast24h: commentsLast24h,
            TotalQuotaLimit: totalQuotaLimit,
            TotalQuotaUsedToday: totalQuotaUsedToday,
            QuotaUsedPercent: quotaUsedPercent,
            ErrorsLast24h: errorsLast24h,
            ConnectedWorkspaces: connectedWorkspaces,
            ApiKeyCount: allKeys.Count,
            ChannelCount: channelCount);
    }

    /// <summary>
    /// Returns 24 consecutive hourly buckets covering the last 24 hours, aligned to the start of each
    /// UTC hour. Every bucket is present (count 0 where no comments fell in it), ordered ascending.
    /// </summary>
    public async Task<CommentsTimelinePoint[]> GetCommentsTimelineAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var currentHourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var earliest = currentHourStart.AddHours(-23);

        var timestamps = await db.ProcessedComments.AsNoTracking()
            .Where(c => c.ProcessedAt >= earliest)
            .Select(c => c.ProcessedAt)
            .ToListAsync(ct);

        var counts = new Dictionary<DateTimeOffset, int>(24);
        foreach (var ts in timestamps)
        {
            var utc = ts.ToUniversalTime();
            var bucket = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
            counts[bucket] = counts.GetValueOrDefault(bucket) + 1;
        }

        var points = new CommentsTimelinePoint[24];
        for (var i = 0; i < 24; i++)
        {
            var bucket = earliest.AddHours(i);
            points[i] = new CommentsTimelinePoint(bucket, counts.GetValueOrDefault(bucket));
        }

        return points;
    }
}
