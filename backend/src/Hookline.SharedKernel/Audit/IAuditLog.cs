using Hookline.SharedKernel.Common;

namespace Hookline.SharedKernel.Audit;

/// <summary>
/// Shared audit trail. Modules write entries through it; the "Logs" page reads
/// them with a per-module filter. The actor is taken from the current user.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(
        string action,
        string? module = null,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        CancellationToken ct = default);
}

/// <summary>One audit-trail row as surfaced to the shared System→Logs page (no internal types leak).</summary>
public sealed record AuditLogRecord(
    long Id,
    DateTimeOffset Timestamp,
    string Actor,
    string? Role,
    string? Module,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Detail);

/// <summary>
/// Read side of the shared audit trail. The host's System→Logs endpoint pages over it,
/// optionally filtered to one module — every module reuses the same Logs surface.
/// </summary>
public interface IAuditLogReader
{
    Task<PagedResult<AuditLogRecord>> ListAsync(
        string? module,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
