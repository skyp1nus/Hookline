using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Caching;
using Hookline.SharedKernel.Maintenance;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Features;

/// <summary>
/// The comments module's slice of the System "Danger Zone" fan-out (host-orchestrated via
/// <see cref="IMaintenanceControl"/>). Pause = flip every active mapping to paused AND tear down its
/// per-mapping recurring poll + reply sweep (mirrors <c>MappingService</c>'s pause). Reset = wipe the
/// operational state (dedup ledger, retry queue, quota counters) and advance every mapping's watermark to
/// now (so a wiped dedup ledger can't replay old comments), keeping the mappings/channels, connections and
/// audit trail intact.
/// </summary>
public sealed class CommentsMaintenanceControl(
    YouTubeCommentsDbContext db,
    IPollingScheduler scheduler,
    ICachePurge cache,
    ICommentsAudit audit) : IMaintenanceControl
{
    public string Module => "youtube-comments";

    public async Task<MaintenanceResult> PauseAllAsync(CancellationToken ct = default)
    {
        var active = await db.ChannelMappings.Where(m => m.IsActive).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var m in active)
        {
            m.IsActive = false;
            m.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        // Tear down each paused mapping's recurring jobs (idempotent) — same as a single pause.
        foreach (var m in active)
        {
            scheduler.Remove(m.Id);
            scheduler.RemoveReplySweep(m.Id);
        }

        await audit.LogAsync(AuditLevel.Warning, "maintenance.pause-all",
            $"Paused {active.Count} mapping(s)", "ChannelMapping", ct: ct);
        return new MaintenanceResult(Module, active.Count, $"{active.Count} mapping(s) paused");
    }

    public async Task<MaintenanceResult> ResetDataAsync(CancellationToken ct = default)
    {
        var processed = await db.ProcessedComments.ToListAsync(ct);
        db.ProcessedComments.RemoveRange(processed);
        var pending = await db.PendingDeliveries.ToListAsync(ct);
        db.PendingDeliveries.RemoveRange(pending);
        var quota = await db.QuotaUsages.ToListAsync(ct);
        db.QuotaUsages.RemoveRange(quota);

        // Keep mappings, but advance the watermark to now + clear last-run state so the wiped dedup ledger
        // can't replay old comments into Slack.
        var now = DateTimeOffset.UtcNow;
        var mappings = await db.ChannelMappings.ToListAsync(ct);
        foreach (var m in mappings)
        {
            m.CommentsSinceUtc = now;
            m.LastError = null;
            m.LastPolledAt = null;
        }

        await db.SaveChangesAsync(ct); // one transaction

        var purged = await cache.PurgeByPrefixAsync(RedisKeys.Prefix, ct);

        var detail = $"processed={processed.Count}, pending={pending.Count}, quota={quota.Count}, watermarks={mappings.Count}, cache={purged}";
        await audit.LogAsync(AuditLevel.Warning, "maintenance.reset", detail, ct: ct);
        return new MaintenanceResult(Module, processed.Count + pending.Count + quota.Count, detail);
    }
}
