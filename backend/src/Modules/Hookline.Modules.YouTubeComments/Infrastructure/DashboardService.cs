using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Aggregated KPIs for the dashboard landing page. The quota figure is an APPROXIMATION computed from
/// each active mapping's configured cadence (not metered actual usage) against the single OAuth
/// project's daily ceiling — see <see cref="DashboardService.GetStatsAsync"/>.
/// </summary>
public sealed record DashboardStatsDto(
    int ActiveMappings,
    int TotalMappings,
    int CommentsToday,
    int CommentsLast24h,
    long QuotaCeiling,
    long EstimatedDailyUnits,
    double EstimatedPercent,
    int ErrorsLast24h,
    int ConnectedWorkspaces,
    int ChannelCount);

/// <summary>A single hour bucket in the "comments processed" timeline. <paramref name="Bucket"/> is the start of the UTC hour.</summary>
public sealed record CommentsTimelinePoint(DateTimeOffset Bucket, int Count);

/// <summary>
/// Read-only aggregation over the operational tables for the dashboard. "Today" boundaries use Pacific
/// Time (matching YouTube's quota reset); rolling windows use the last 24 hours from
/// <see cref="DateTimeOffset.UtcNow"/>. Slack workspaces are counted via the shared Connections accessor.
/// </summary>
public sealed class DashboardService(
    YouTubeCommentsDbContext db,
    ISlackConnections slackConnections,
    IAuditLogReader auditLog,
    IOptions<YouTubeCommentsOptions> options)
{
    private readonly int _dailyLimit = options.Value.DailyQuotaUnits;

    // Approximate quota cost-per-call (YouTube Data API v3 quota table). A poll issues
    // commentThreads.list (1u) + videos.list (1u) ≈ 2u. The reply sweep's realized cost varies widely
    // (paged thread scan + per-popular-comment reply pages), so SweepUnitEstimate is a deliberately
    // CONSERVATIVE upper guess: the meter should OVER-estimate, never under — better to look near the
    // 10,000 cap early than to silently blow past it thinking there's headroom.
    private const int PollUnitCost = 2;
    private const int SweepUnitEstimate = 30;

    /// <summary>Computes the dashboard KPI snapshot in a handful of aggregate queries.</summary>
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);
        var startOfTodayPt = PacificTime.StartOfToday();

        var totalMappings = await db.ChannelMappings.AsNoTracking().CountAsync(ct);
        var activeMappings = await db.ChannelMappings.AsNoTracking().CountAsync(m => m.IsActive, ct);

        var commentsToday = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= startOfTodayPt, ct);
        var commentsLast24h = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= since24h, ct);

        // Estimated daily quota: with API keys gone there is no per-key usage ledger, so PROJECT spend
        // from each active mapping's configured cadence (polls/day × poll cost + sweeps/day × sweep cost).
        // This is an over-estimating approximation, NOT metered usage — labelled "≈ estimated" in the UI.
        var cadences = await db.ChannelMappings.AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new { m.Frequency, m.IncludeReplies, m.ReplySweepFrequency })
            .ToListAsync(ct);

        long estimatedDailyUnits = 0;
        foreach (var m in cadences)
        {
            var pollsPerDay = 1440 / (int)m.Frequency; // Frequency value is the interval in minutes
            estimatedDailyUnits += (long)pollsPerDay * PollUnitCost;
            if (m.IncludeReplies && m.ReplySweepFrequency != ReplyScanFrequency.Off)
            {
                var sweepsPerDay = 1440 / (int)m.ReplySweepFrequency; // sweep value is the interval in minutes
                estimatedDailyUnits += (long)sweepsPerDay * SweepUnitEstimate;
            }
        }

        // Single OAuth project ⇒ one shared daily ceiling (Google default 10,000). If a SECOND Google
        // project is ever connected, this ceiling must rise to the SUM across projects — today there is
        // exactly one, so DailyQuotaUnits is the whole ceiling.
        long quotaCeiling = _dailyLimit;
        var estimatedPercent = quotaCeiling > 0
            ? Math.Round(Math.Min(100d, (double)estimatedDailyUnits / quotaCeiling * 100), 1)
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
            QuotaCeiling: quotaCeiling,
            EstimatedDailyUnits: estimatedDailyUnits,
            EstimatedPercent: estimatedPercent,
            ErrorsLast24h: errorsLast24h,
            ConnectedWorkspaces: connectedWorkspaces,
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
