using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeUploads.Features;

/// <summary>
/// When a shared Google account is disconnected, drop its module-local binding (account ↔ project)
/// and any channel mappings pointing at it — mirrors the guide's §5 "deactivate on disconnect" pattern.
/// The binding holds a plain account id (no cross-schema FK), so this event is how the link is severed.
/// </summary>
public sealed class GoogleAccountDisconnectedHandler(
    YouTubeUploadsDbContext db,
    ChannelMappingService mappings,
    ILogger<GoogleAccountDisconnectedHandler> logger)
    : IIntegrationEventHandler<GoogleAccountDisconnected>
{
    public async Task HandleAsync(GoogleAccountDisconnected @event, CancellationToken ct = default)
    {
        var bindings = await db.GoogleAccountBindings.Where(b => b.AccountId == @event.AccountId).ToListAsync(ct);
        if (bindings.Count > 0)
        {
            db.GoogleAccountBindings.RemoveRange(bindings);
            await db.SaveChangesAsync(ct);
        }

        var removed = await mappings.RemoveByAccountAsync(@event.AccountId, ct);
        logger.LogInformation("Google account {Account} disconnected: dropped {Bindings} binding(s), {Mappings} mapping(s)",
            @event.AccountId, bindings.Count, removed);
    }
}

/// <summary>When a shared Slack workspace is disconnected, drop its channel mappings + cached channels.</summary>
public sealed class SlackWorkspaceDisconnectedHandler(
    YouTubeUploadsDbContext db,
    ChannelMappingService mappings,
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

        logger.LogInformation("Slack workspace {Workspace} disconnected: dropped {Mappings} mapping(s), {Channels} channel(s)",
            @event.WorkspaceId, removed, channels.Count);
    }
}
