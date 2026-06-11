using Hookline.SharedKernel.Common;

namespace Hookline.SharedKernel.Audit;

/// <summary>
/// Shared audit trail. Modules write entries through it; the "Logs" page reads
/// them with a per-module filter. The actor defaults to the current user, but a caller may stamp an
/// EXPLICIT <paramref name="actor"/> for actions whose real actor is not the request principal — e.g. a
/// provider callback (a Slack interactivity button) runs on the identity-bypassed <c>/slack</c> path
/// where the current user is anonymous, so the moderating Slack user is passed through here instead of
/// being lost to "anonymous".
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(
        string action,
        string? module = null,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        string? actor = null,
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

    /// <summary>
    /// Counts entries since <paramref name="since"/>, optionally scoped to one <paramref name="module"/>
    /// and to rows whose detail begins with <paramref name="detailPrefix"/> (the folded level marker,
    /// e.g. <c>[Error]</c>). Lets a dashboard surface an at-a-glance KPI without paging the whole trail.
    /// </summary>
    Task<int> CountSinceAsync(
        string? module,
        DateTimeOffset since,
        string? detailPrefix = null,
        CancellationToken ct = default);
}
