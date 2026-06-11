using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Jobs;
using Hookline.SharedKernel.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// <see cref="IPollingScheduler"/> over the shared <see cref="IJobScheduler"/>. Each active mapping
/// maps to one recurring poll job <c>ytc:poll:{id}</c> (and, when replies are swept, one
/// <c>ytc:reply-sweep:{id}</c>) whose cron cadence derives from the mapping's frequency. The deterministic
/// ids keep AddOrUpdate idempotent and let <see cref="SyncAllAsync"/> prune orphans by id.
/// </summary>
public sealed class HangfirePollingScheduler(
    IJobScheduler scheduler,
    YouTubeCommentsDbContext db,
    ILogger<HangfirePollingScheduler> logger) : IPollingScheduler
{
    private const string PollPrefix = "ytc:poll:";
    private const string ReplySweepPrefix = "ytc:reply-sweep:";

    /// <summary>Deterministic recurring-job id for a mapping's poll.</summary>
    public static string JobId(Guid mappingId) => $"{PollPrefix}{mappingId}";

    /// <summary>Deterministic recurring-job id for a mapping's deep reply sweep.</summary>
    public static string ReplySweepJobId(Guid mappingId) => $"{ReplySweepPrefix}{mappingId}";

    public void Schedule(Guid mappingId, PollingFrequency frequency)
    {
        var id = JobId(mappingId);
        scheduler.AddOrUpdateRecurring<PollCommentsJob>(
            id, job => job.RunAsync(mappingId, CancellationToken.None), frequency.ToCron());
        logger.LogInformation("Scheduled polling job {JobId} ({Cron})", id, frequency.ToCron());
    }

    public void Remove(Guid mappingId)
    {
        var id = JobId(mappingId);
        scheduler.RemoveRecurring(id);
        logger.LogInformation("Removed polling job {JobId}", id);
    }

    public void ScheduleReplySweep(Guid mappingId, ReplyScanFrequency frequency)
    {
        var id = ReplySweepJobId(mappingId);
        var cron = frequency.ToCron();
        if (cron is null)
        {
            scheduler.RemoveRecurring(id);
            return;
        }

        scheduler.AddOrUpdateRecurring<DeepReplySweepJob>(
            id, job => job.RunAsync(mappingId, CancellationToken.None), cron);
        logger.LogInformation("Scheduled reply sweep {JobId} ({Cron})", id, cron);
    }

    public void RemoveReplySweep(Guid mappingId)
    {
        var id = ReplySweepJobId(mappingId);
        scheduler.RemoveRecurring(id);
        logger.LogInformation("Removed reply sweep {JobId}", id);
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        var active = await db.ChannelMappings
            .AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new { m.Id, m.Frequency, m.IncludeReplies, m.ReplySweepFrequency })
            .ToListAsync(ct);

        var activeIds = active.Select(m => m.Id).ToHashSet();

        foreach (var m in active)
        {
            Schedule(m.Id, m.Frequency);
            if (m.IncludeReplies && m.ReplySweepFrequency != ReplyScanFrequency.Off)
                ScheduleReplySweep(m.Id, m.ReplySweepFrequency);
            else
                RemoveReplySweep(m.Id);
        }

        // Prune orphan recurring jobs whose mapping was deleted/deactivated while the host was down.
        // ListRecurring returns ids across the whole host, so only touch ids in this module's namespace.
        var pruned = 0;
        foreach (var id in scheduler.ListRecurring())
        {
            var mappingId = ParseMappingId(id);
            if (mappingId is { } mid && !activeIds.Contains(mid))
            {
                scheduler.RemoveRecurring(id);
                pruned++;
            }
        }

        logger.LogInformation("Synced {Active} active polling job(s), pruned {Pruned} orphan(s) on startup", active.Count, pruned);
    }

    /// <summary>Extracts the mapping id from a <c>ytc:poll:{guid}</c> / <c>ytc:reply-sweep:{guid}</c> id, else null.</summary>
    private static Guid? ParseMappingId(string id)
    {
        string? raw =
            id.StartsWith(PollPrefix, StringComparison.Ordinal) ? id[PollPrefix.Length..] :
            id.StartsWith(ReplySweepPrefix, StringComparison.Ordinal) ? id[ReplySweepPrefix.Length..] :
            null;

        return raw is not null && Guid.TryParse(raw, out var g) ? g : null;
    }
}
