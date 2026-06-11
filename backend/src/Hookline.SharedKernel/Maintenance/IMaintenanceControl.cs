namespace Hookline.SharedKernel.Maintenance;

/// <summary>
/// A module's host-orchestrated maintenance surface. The host resolves every registered
/// <see cref="IMaintenanceControl"/> and fans a System "Danger Zone" operation out across all modules
/// WITHOUT referencing any module type — so the operation stays module-agnostic and the module-boundary
/// arch tests keep holding (a new module that registers this is picked up automatically). Each module
/// audits its own action through the shared trail.
/// </summary>
public interface IMaintenanceControl
{
    /// <summary>The owning module id (e.g. <c>"youtube-uploads"</c>), for the response + per-module audit.</summary>
    string Module { get; }

    /// <summary>
    /// Pause every active automation the module owns (mappings / routes), tearing down any per-mapping
    /// recurring jobs the module schedules. Idempotent. Returns the number of automations paused.
    /// </summary>
    Task<MaintenanceResult> PauseAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Wipe the module's OPERATIONAL data (run history / dedup / quota / retry state) while keeping
    /// configuration (mappings/routes), connections and secrets intact. Transactional within the module.
    /// Returns a per-table breakdown for the audit detail.
    /// </summary>
    Task<MaintenanceResult> ResetDataAsync(CancellationToken ct = default);
}

/// <summary>The outcome of a maintenance op for one module: a total affected count plus a human breakdown.</summary>
public sealed record MaintenanceResult(string Module, int Affected, string Detail);
