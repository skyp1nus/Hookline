using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Features;

/// <summary>
/// When a shared Slack workspace is disconnected, DEACTIVATE every mapping whose Slack channel belongs
/// to it and tear down its recurring jobs — the mapping rows (and their dedup ledger) survive so a
/// reconnect can reactivate them; the channel cache is left in place (it re-syncs on reconnect). Mirrors
/// the architecture guide's §5 "deactivate on disconnect". The link is a plain workspace id (no
/// cross-schema FK), so this event is how it is severed. Recorded in the shared audit trail.
/// </summary>
public sealed class SlackWorkspaceDisconnectedHandler(
    YouTubeCommentsDbContext db,
    IPollingScheduler scheduler,
    ICommentsAudit audit,
    ILogger<SlackWorkspaceDisconnectedHandler> logger)
    : IIntegrationEventHandler<SlackWorkspaceDisconnected>
{
    public async Task HandleAsync(SlackWorkspaceDisconnected @event, CancellationToken ct = default)
    {
        var mappings = await db.ChannelMappings
            .Include(m => m.SlackChannel)
            .Where(m => m.SlackChannel!.WorkspaceId == @event.WorkspaceId && m.IsActive)
            .ToListAsync(ct);

        if (mappings.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var m in mappings)
            {
                m.IsActive = false;
                m.LastError = "Slack workspace disconnected";
                m.UpdatedAt = now;
                scheduler.Remove(m.Id);
                scheduler.RemoveReplySweep(m.Id);
            }
            await db.SaveChangesAsync(ct);
        }

        await audit.LogAsync(AuditLevel.Warning, "Slack",
            "Slack workspace disconnected", "SlackWorkspace", @event.WorkspaceId.ToString(),
            details: $"deactivated {mappings.Count} mapping(s)", ct: ct);
        logger.LogInformation("Slack workspace {Workspace} disconnected: deactivated {Mappings} mapping(s)",
            @event.WorkspaceId, mappings.Count);
    }
}
