using Hookline.Infrastructure.Persistence;

using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;

namespace Hookline.Infrastructure.Audit;

/// <summary>Writes audit entries, stamping the actor from the current user.</summary>
public sealed class AuditLog(SharedDbContext db, ICurrentUser currentUser) : IAuditLog
{
    public async Task WriteAsync(
        string action,
        string? module = null,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = currentUser.Email ?? (currentUser.IsSystem ? "system" : "anonymous"),
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
