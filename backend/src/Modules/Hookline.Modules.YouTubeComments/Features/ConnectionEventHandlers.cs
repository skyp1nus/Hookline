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

        await audit.LogAsync("Warning", "Slack",
            "Slack workspace disconnected", "SlackWorkspace", @event.WorkspaceId.ToString(),
            details: $"deactivated {mappings.Count} mapping(s)", ct: ct);
        logger.LogInformation("Slack workspace {Workspace} disconnected: deactivated {Mappings} mapping(s)",
            @event.WorkspaceId, mappings.Count);
    }
}

/// <summary>
/// When a shared YouTube API key is deleted from the pool, prune the module-local <c>quota_usage</c>
/// rows that tracked its Pacific-day consumption (the key id is a plain value, no cross-schema FK).
/// Polling keeps working off the remaining keys.
/// </summary>
public sealed class YouTubeApiKeyDisconnectedHandler(
    YouTubeCommentsDbContext db,
    ICommentsAudit audit,
    ILogger<YouTubeApiKeyDisconnectedHandler> logger)
    : IIntegrationEventHandler<YouTubeApiKeyDisconnected>
{
    public async Task HandleAsync(YouTubeApiKeyDisconnected @event, CancellationToken ct = default)
    {
        var pruned = await db.QuotaUsages
            .Where(q => q.ApiKeyId == @event.KeyId)
            .ExecuteDeleteAsync(ct);

        await audit.LogAsync("Information", "Quota",
            $"API key disconnected: pruned {pruned} quota row(s)", "YouTubeApiKey", @event.KeyId.ToString(), ct: ct);
        logger.LogInformation("YouTube API key {Key} disconnected: pruned {Pruned} quota row(s)", @event.KeyId, pruned);
    }
}
