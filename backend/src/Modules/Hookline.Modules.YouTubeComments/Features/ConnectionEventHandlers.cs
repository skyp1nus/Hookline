using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Features;

/// <summary>
/// When a shared Slack workspace is disconnected, HARD-REMOVE every mapping whose Slack channel belongs to
/// it (and tear down its recurring jobs) and delete the workspace's cached channels. Disconnect is now a
/// real teardown — the workspace row + its encrypted bot token are already gone from the shared store — so
/// nothing module-local should survive to dangle against a workspace that no longer exists. Deleting the
/// mapping rows cascades their children (processed-comment dedup ledger, pending deliveries, moderation
/// records) at the database; the cached channels are a rebuildable cache (re-synced from Slack on a fresh
/// reconnect). The link is a plain workspace id (no cross-schema FK), so this event is how it is severed.
/// The disconnect action itself is preserved in the shared audit trail.
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
        // ALL mappings for the workspace (no IsActive filter): the workspace is gone, so inactive ones must
        // be removed too rather than left pointing at a deleted workspace.
        var mappings = await db.ChannelMappings
            .Include(m => m.SlackChannel)
            .Where(m => m.SlackChannel!.WorkspaceId == @event.WorkspaceId)
            .ToListAsync(ct);

        foreach (var m in mappings)
        {
            scheduler.Remove(m.Id);
            scheduler.RemoveReplySweep(m.Id);
        }

        if (mappings.Count > 0)
        {
            // Children (ProcessedComments / PendingDeliveries / CommentModerations) cascade at the DB.
            db.ChannelMappings.RemoveRange(mappings);
        }

        var channels = await db.SlackChannels
            .Where(c => c.WorkspaceId == @event.WorkspaceId)
            .ToListAsync(ct);
        if (channels.Count > 0)
        {
            db.SlackChannels.RemoveRange(channels);
        }

        if (mappings.Count > 0 || channels.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        await audit.LogAsync(AuditLevel.Warning, "Slack",
            "Slack workspace disconnected", "SlackWorkspace", @event.WorkspaceId.ToString(),
            details: $"removed {mappings.Count} mapping(s), {channels.Count} channel(s)", ct: ct);
        logger.LogInformation("Slack workspace {Workspace} disconnected: removed {Mappings} mapping(s), {Channels} channel(s)",
            @event.WorkspaceId, mappings.Count, channels.Count);
    }
}
