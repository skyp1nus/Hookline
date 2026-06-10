using Hookline.Modules.YouTubeComments.Domain;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Manages the recurring background poll (and deep reply sweep) for each active channel mapping. The
/// concrete implementation drives the shared <c>IJobScheduler</c>; this seam keeps the feature
/// services (e.g. MappingService) free of any scheduler-engine dependency.
/// </summary>
public interface IPollingScheduler
{
    /// <summary>Registers or refreshes the recurring poll for <paramref name="mappingId"/>. Idempotent.</summary>
    void Schedule(Guid mappingId, PollingFrequency frequency);

    /// <summary>Removes the recurring poll for <paramref name="mappingId"/> if one exists. Idempotent.</summary>
    void Remove(Guid mappingId);

    /// <summary>
    /// Registers or refreshes the recurring deep reply sweep, or removes it when
    /// <paramref name="frequency"/> is <see cref="ReplyScanFrequency.Off"/>. Idempotent.
    /// </summary>
    void ScheduleReplySweep(Guid mappingId, ReplyScanFrequency frequency);

    /// <summary>Removes the recurring reply sweep for <paramref name="mappingId"/> if one exists. Idempotent.</summary>
    void RemoveReplySweep(Guid mappingId);

    /// <summary>
    /// Reconciles the scheduler with the database on startup: registers a recurring poll (+ sweep) for
    /// every active mapping, and prunes orphan recurring jobs whose mapping was deleted or deactivated
    /// while the host was down. Makes jobs survive a restart and self-heal a fresh database.
    /// </summary>
    Task SyncAllAsync(CancellationToken ct = default);
}
