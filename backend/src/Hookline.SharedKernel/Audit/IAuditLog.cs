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
