using Hookline.Modules.YouTubeComments.Domain;

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

/// <summary>Request to add a channel by raw id, <c>@handle</c>, or youtube.com URL.</summary>
public sealed record AddChannelRequest(string Input);

/// <summary>Thrown when a channel can't be resolved or no key has quota (-> 400).</summary>
public sealed class ChannelResolutionException(string message) : Exception(message);

/// <summary>
/// Manages the tracked set of YouTube channels: listing them with their mapping counts, and adding a
/// channel by resolving a raw id, <c>@handle</c>, or youtube.com URL against the YouTube Data API
/// (consuming a leased key's quota) before upserting it by channel id.
/// </summary>
public sealed class ChannelService(
    YouTubeCommentsDbContext db,
    IYouTubeClient youtube,
    IYouTubeApiKeyProvider keys,
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
    /// Resolves <paramref name="input"/> to a YouTube channel and upserts it. Acquires an API key with
    /// available quota, calls the YouTube API, records the quota consumed, then returns the existing or
    /// newly-created channel. Throws <see cref="ChannelResolutionException"/> (-> 400) when no key has
    /// quota or the input doesn't resolve to a channel.
    /// </summary>
    public async Task<YouTubeChannelDto> AddAsync(string input, CancellationToken ct = default)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ChannelResolutionException("A channel id, handle, or URL is required.");

        // A /c/CUSTOM URL costs 101 units (search.list + channels.list); everything else costs 1.
        // Acquire enough for the worst case so the lookup never aborts mid-flight on quota.
        var lease = await keys.AcquireAsync(unitsNeeded: 101, ct)
            ?? throw new ChannelResolutionException("No YouTube API key with available quota");

        var result = await youtube.GetChannelAsync(lease.ApiKey, trimmed, ct);

        // Always record the quota actually consumed, even when the channel didn't resolve.
        if (result.UnitsUsed > 0)
            await keys.RecordUsageAsync(lease.Id, result.UnitsUsed, ct);

        if (result.Channel is null)
            throw new ChannelResolutionException($"Could not resolve channel from '{trimmed}'");

        var info = result.Channel;

        // Upsert by the immutable YouTube channel id: if we already track it, return as-is.
        var existing = await db.YouTubeChannels.FirstOrDefaultAsync(c => c.YouTubeChannelId == info.ChannelId, ct);
        if (existing is not null)
            return await ToDtoAsync(existing, ct);

        var entity = new YouTubeChannel
        {
            YouTubeChannelId = info.ChannelId,
            Title = info.Title,
            ThumbnailUrl = info.ThumbnailUrl,
            Handle = info.Handle,
            AddedAt = DateTimeOffset.UtcNow,
        };

        db.YouTubeChannels.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Information", "Channel",
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

        await audit.LogAsync("Information", "Channel",
            $"Deleted YouTube channel '{entity.Title}'", "YouTubeChannel", id.ToString(), ct: ct);
        return true;
    }

    private async Task<YouTubeChannelDto> ToDtoAsync(YouTubeChannel channel, CancellationToken ct)
    {
        var mappingCount = await db.ChannelMappings.CountAsync(m => m.YouTubeChannelId == channel.Id, ct);
        return new YouTubeChannelDto(channel.Id, channel.YouTubeChannelId, channel.Title, channel.ThumbnailUrl, channel.Handle, channel.AddedAt, mappingCount);
    }
}
