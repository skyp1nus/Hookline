using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Caching;
using Hookline.SharedKernel.Maintenance;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Features;

/// <summary>
/// The uploads module's slice of the System "Danger Zone" fan-out (host-orchestrated via
/// <see cref="IMaintenanceControl"/>). Pause = flip every route to paused (the event-driven ingest gate
/// does the rest — no recurring jobs to tear down). Reset = wipe the operational run history (jobs +
/// per-job state history) and the module's Redis namespace, keeping routes, projects/bindings, settings,
/// connections and the audit trail intact.
/// </summary>
public sealed class UploadsMaintenanceControl(
    YouTubeUploadsDbContext db,
    ICachePurge cache,
    IAuditLog audit) : IMaintenanceControl
{
    public string Module => "youtube-uploads";

    public async Task<MaintenanceResult> PauseAllAsync(CancellationToken ct = default)
    {
        var active = await db.ChannelMappings.Where(m => m.IsActive).ToListAsync(ct);
        foreach (var m in active)
        {
            m.IsActive = false;
        }

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("maintenance.pause-all", module: Module, entityType: "channel_mapping",
            detail: $"paused {active.Count} route(s)", ct: ct);
        return new MaintenanceResult(Module, active.Count, $"{active.Count} route(s) paused");
    }

    public async Task<MaintenanceResult> ResetDataAsync(CancellationToken ct = default)
    {
        // Operational data only. Delete history before jobs (history FKs the job). One SaveChanges = one
        // transaction. RemoveRange (not ExecuteDelete) so the same path is exercised under the in-memory
        // provider in unit tests; volumes are bounded by the retention job in practice.
        var history = await db.JobHistory.ToListAsync(ct);
        db.JobHistory.RemoveRange(history);
        var jobs = await db.Jobs.ToListAsync(ct);
        db.Jobs.RemoveRange(jobs);
        await db.SaveChangesAsync(ct);

        var purged = await cache.PurgeByPrefixAsync(RedisKeys.Prefix, ct);

        var detail = $"jobs={jobs.Count}, history={history.Count}, cache={purged}";
        await audit.WriteAsync("maintenance.reset", module: Module, detail: detail, ct: ct);
        return new MaintenanceResult(Module, jobs.Count + history.Count, detail);
    }
}
