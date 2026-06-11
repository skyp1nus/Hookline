using Hookline.Infrastructure.Persistence;

using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Common;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Audit;

/// <summary>Writes audit entries, stamping the actor from the current user — unless the caller passes an
/// explicit <c>actor</c> (e.g. the moderating Slack user on the identity-bypassed provider callback,
/// where the current user is anonymous), which then takes precedence.</summary>
public sealed class AuditLog(SharedDbContext db, ICurrentUser currentUser) : IAuditLog
{
    public async Task WriteAsync(
        string action,
        string? module = null,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        string? actor = null,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            // An explicit actor (a provider-callback actor like the Slack user) wins; otherwise fall back
            // to the request principal (admin email / "system" for jobs / "anonymous").
            Actor = !string.IsNullOrWhiteSpace(actor)
                ? actor
                : currentUser.Email ?? (currentUser.IsSystem ? "system" : "anonymous"),
            ActorId = currentUser.UserId,
            Role = currentUser.Role?.ToString(),
            Module = module,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Detail = detail,
        });

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Pages over the shared <c>audit_logs</c> table, newest first, optionally filtered to one module.</summary>
public sealed class AuditLogReader(SharedDbContext db) : IAuditLogReader
{
    public async Task<PagedResult<AuditLogRecord>> ListAsync(
        string? module,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var req = new PageRequest(page, pageSize);

        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(a => a.Module == module);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip(req.Skip)
            .Take(req.SafePageSize)
            .Select(a => new AuditLogRecord(
                a.Id, a.Timestamp, a.Actor, a.Role, a.Module, a.Action, a.EntityType, a.EntityId, a.Detail))
            .ToListAsync(ct);

        return new PagedResult<AuditLogRecord>(items, req.SafePage, req.SafePageSize, total);
    }

    public async Task<int> CountSinceAsync(
        string? module,
        DateTimeOffset since,
        string? detailPrefix = null,
        CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking().Where(a => a.Timestamp >= since);

        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(a => a.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(detailPrefix))
        {
            // The level marker is folded into the detail prefix (e.g. "[Error] ..."). Postgres LIKE
            // treats '[' / ']' literally, so StartsWith matches the marker exactly.
            query = query.Where(a => a.Detail != null && a.Detail.StartsWith(detailPrefix));
        }

        return await query.CountAsync(ct);
    }
}
