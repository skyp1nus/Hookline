using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>A tracked YouTube channel, with a count of the mappings that target it.</summary>
public sealed record YouTubeChannelDto(
    Guid Id,
    string YouTubeChannelId,
    string Title,
    string? ThumbnailUrl,
    string? Handle,
    DateTimeOffset AddedAt,
    int MappingCount);

/// <summary>
/// A YouTube channel the operator can monitor: one owned by a connected Google account that has granted
/// the comment-management (force-ssl) scope. <see cref="AlreadyTracked"/> marks the ones already added.
/// </summary>
public sealed record ConnectedChannelOption(
    string YouTubeChannelId,
    string Title,
    string? ThumbnailUrl,
    bool AlreadyTracked);

/// <summary>Request to track one of the operator's connected YouTube channels (by its channel id).</summary>
public sealed record AddChannelRequest(string YouTubeChannelId);

/// <summary>Thrown when the requested channel is not a connected, comment-capable account (-> 400).</summary>
public sealed class ChannelResolutionException(string message) : Exception(message);

/// <summary>
/// Manages the tracked set of YouTube channels. Monitoring is OAuth-only, so a channel can be tracked
/// ONLY if a connected Google account OWNS it and has granted the force-ssl scope (the SAME credential
/// that powers monitoring + the Reject button — consistent gating by construction). Channels are picked
/// from <see cref="ListAvailableAsync"/> (the connected accounts' own channels), never resolved from an
/// arbitrary URL/@handle via an API key. When no connected account is comment-capable, the available
/// list is empty — the honest "connect Google to enable monitoring" gated state.
/// </summary>
public sealed class ChannelService(
    YouTubeCommentsDbContext db,
    IGoogleConnections googleConnections,
    ICommentsAudit audit)
{
    /// <summary>Lists every tracked channel, newest first, with the number of mappings targeting it.</summary>
    public async Task<YouTubeChannelDto[]> ListAsync(CancellationToken ct = default) =>
        await db.YouTubeChannels
            .AsNoTracking()
            .OrderByDescending(c => c.AddedAt)
            .Select(c => new YouTubeChannelDto(
                c.Id, c.YouTubeChannelId, c.Title, c.ThumbnailUrl, c.Handle, c.AddedAt, c.Mappings.Count))
            .ToArrayAsync(ct);

    /// <summary>
    /// The operator's own channels that CAN be monitored: each connected, active Google account that has
    /// a channel id and the force-ssl scope. Already-tracked channels are flagged (not hidden) so the UI
    /// can show them disabled. An empty list is the honest "connect a Google account with the
    /// comment-management permission to enable monitoring" gated state.
    /// </summary>
    public async Task<ConnectedChannelOption[]> ListAvailableAsync(CancellationToken ct = default)
    {
        var tracked = (await db.YouTubeChannels.AsNoTracking().Select(c => c.YouTubeChannelId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var options = new List<ConnectedChannelOption>();
        foreach (var capable in await ResolveCapableChannelsAsync(ct))
        {
            options.Add(new ConnectedChannelOption(
                capable.ChannelId, capable.Title, capable.ThumbnailUrl, tracked.Contains(capable.ChannelId)));
        }

        return options.OrderBy(o => o.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Tracks one of the operator's connected channels by its channel id. Validates that a connected,
    /// comment-capable account owns it (-> 400 otherwise), then upserts by the immutable channel id.
    /// No YouTube API call: the title/avatar come from the connected account's snapshot.
    /// </summary>
    public async Task<YouTubeChannelDto> AddAsync(string youTubeChannelId, CancellationToken ct = default)
    {
        var channelId = youTubeChannelId?.Trim() ?? string.Empty;
        if (channelId.Length == 0)
            throw new ChannelResolutionException("A connected YouTube channel is required.");

        var capable = (await ResolveCapableChannelsAsync(ct))
            .FirstOrDefault(c => string.Equals(c.ChannelId, channelId, StringComparison.Ordinal));
        if (capable is null)
            throw new ChannelResolutionException(
                "That channel isn't connected for monitoring. Connect its Google account (and grant the "
                + "comment-management permission) in Connections → Google, then add it here.");

        // Upsert by the immutable YouTube channel id: if we already track it, return as-is.
        var existing = await db.YouTubeChannels.FirstOrDefaultAsync(c => c.YouTubeChannelId == channelId, ct);
        if (existing is not null)
            return await ToDtoAsync(existing, ct);

        var entity = new YouTubeChannel
        {
            YouTubeChannelId = capable.ChannelId,
            Title = capable.Title,
            ThumbnailUrl = capable.ThumbnailUrl,
            Handle = null, // not part of the connected-account snapshot
            AddedAt = DateTimeOffset.UtcNow,
        };

        db.YouTubeChannels.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(AuditLevel.Information, "Channel",
            $"Added YouTube channel '{entity.Title}'", "YouTubeChannel", entity.Id.ToString(), ct: ct);

        return new YouTubeChannelDto(entity.Id, entity.YouTubeChannelId, entity.Title, entity.ThumbnailUrl, entity.Handle, entity.AddedAt, 0);
    }

    /// <summary>Deletes a channel (and its mappings, via cascade). Returns <c>false</c> when not found.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.YouTubeChannels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return false;

        db.YouTubeChannels.Remove(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(AuditLevel.Information, "Channel",
            $"Deleted YouTube channel '{entity.Title}'", "YouTubeChannel", id.ToString(), ct: ct);
        return true;
    }

    /// <summary>
    /// The connected, active Google accounts that own a channel AND hold the force-ssl scope — the set we
    /// can both monitor and moderate. Reads the shared store's scope snapshot (a contract call, not a
    /// join); mirrors <see cref="CommentModerationService.CanModerateAsync"/>'s capability test.
    /// </summary>
    private async Task<List<CapableChannel>> ResolveCapableChannelsAsync(CancellationToken ct)
    {
        var capable = new List<CapableChannel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var summary in await googleConnections.ListAsync(ct))
        {
            if (!summary.IsActive)
                continue;

            var detail = await googleConnections.GetAsync(summary.Id, ct);
            if (detail is not { IsActive: true, ChannelId: { Length: > 0 } channelId })
                continue;
            if (!detail.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(GoogleScopes.YouTubeForceSsl, StringComparer.Ordinal))
                continue;

            // One option per channel even if multiple accounts own it (the credential resolver picks one).
            if (seen.Add(channelId))
                capable.Add(new CapableChannel(channelId, detail.ChannelTitle, detail.AvatarUrl));
        }

        return capable;
    }

    private async Task<YouTubeChannelDto> ToDtoAsync(YouTubeChannel channel, CancellationToken ct)
    {
        var mappingCount = await db.ChannelMappings.CountAsync(m => m.YouTubeChannelId == channel.Id, ct);
        return new YouTubeChannelDto(channel.Id, channel.YouTubeChannelId, channel.Title, channel.ThumbnailUrl, channel.Handle, channel.AddedAt, mappingCount);
    }

    private sealed record CapableChannel(string ChannelId, string Title, string? ThumbnailUrl);
}
