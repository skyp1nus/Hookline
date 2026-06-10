using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeUploads.Features;

/// <summary>
/// When a shared Google account is disconnected, DEACTIVATE its module-local binding (account ↔ project)
/// — rotation skips inactive bindings, but the row survives so the account↔project history isn't erased —
/// and drop the channel mappings pointing at it. Mirrors the guide's §5 "deactivate on disconnect" pattern.
/// The binding holds a plain account id (no cross-schema FK), so this event is how the link is severed.
/// The disconnect is recorded in the shared audit trail.
/// </summary>
public sealed class GoogleAccountDisconnectedHandler(
    YouTubeUploadsDbContext db,
    ChannelMappingService mappings,
    IAuditLog audit,
    ILogger<GoogleAccountDisconnectedHandler> logger)
    : IIntegrationEventHandler<GoogleAccountDisconnected>
{
    public async Task HandleAsync(GoogleAccountDisconnected @event, CancellationToken ct = default)
    {
        // Soft-deactivate (not delete): rotation already filters on IsActive, so this removes the account
        // from the upload pool while preserving the binding row for audit/history.
        var bindings = await db.GoogleAccountBindings
            .Where(b => b.AccountId == @event.AccountId && b.IsActive).ToListAsync(ct);
        if (bindings.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var b in bindings) { b.IsActive = false; b.UpdatedAt = now; }
            await db.SaveChangesAsync(ct);
        }

        var removed = await mappings.RemoveByAccountAsync(@event.AccountId, ct);
        await audit.WriteAsync("connection.google.disconnected", module: "youtube-uploads",
            entityType: "google_account", entityId: @event.AccountId.ToString(),
            detail: $"deactivated {bindings.Count} binding(s), dropped {removed} mapping(s)", ct: ct);
        logger.LogInformation("Google account {Account} disconnected: deactivated {Bindings} binding(s), {Mappings} mapping(s)",
            @event.AccountId, bindings.Count, removed);
    }
}

/// <summary>When a shared Slack workspace is disconnected, drop its channel mappings + cached channels.
/// The channel list is a rebuildable cache (re-synced from Slack on reconnect) so it is hard-deleted; the
/// disconnect action itself is preserved in the shared audit trail.</summary>
public sealed class SlackWorkspaceDisconnectedHandler(
    YouTubeUploadsDbContext db,
    ChannelMappingService mappings,
    IAuditLog audit,
    ILogger<SlackWorkspaceDisconnectedHandler> logger)
    : IIntegrationEventHandler<SlackWorkspaceDisconnected>
{
    public async Task HandleAsync(SlackWorkspaceDisconnected @event, CancellationToken ct = default)
    {
        var removed = await mappings.RemoveByWorkspaceAsync(@event.WorkspaceId, ct);

        var channels = await db.SlackChannels.Where(c => c.WorkspaceId == @event.WorkspaceId).ToListAsync(ct);
        if (channels.Count > 0)
        {
            db.SlackChannels.RemoveRange(channels);
            await db.SaveChangesAsync(ct);
        }

        await audit.WriteAsync("connection.slack.disconnected", module: "youtube-uploads",
            entityType: "slack_workspace", entityId: @event.WorkspaceId.ToString(),
            detail: $"dropped {removed} mapping(s), {channels.Count} cached channel(s)", ct: ct);
        logger.LogInformation("Slack workspace {Workspace} disconnected: dropped {Mappings} mapping(s), {Channels} channel(s)",
            @event.WorkspaceId, removed, channels.Count);
    }
}
