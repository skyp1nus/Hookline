using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>A channel mapping flattened with the display names of its endpoints.</summary>
public sealed record MappingDto(
    Guid Id,
    Guid YouTubeChannelId,
    string YouTubeChannelTitle,
    string? YouTubeChannelThumbnailUrl,
    Guid SlackChannelId,
    string SlackChannelName,
    string SlackWorkspaceName,
    PollingFrequency Frequency,
    bool IsActive,
    bool IncludeReplies,
    ReplyScanFrequency ReplySweepFrequency,
    int ReplyWindowDays,
    DateTimeOffset? LastPolledAt,
    string? LastError,
    DateTimeOffset CreatedAt);

/// <summary>Request to create a mapping between a tracked YouTube channel and a Slack channel.</summary>
public sealed record CreateMappingRequest(
    Guid YouTubeChannelId,
    Guid SlackChannelId,
    PollingFrequency Frequency,
    bool IncludeReplies = false,
    ReplyScanFrequency ReplySweepFrequency = ReplyScanFrequency.Off,
    int ReplyWindowDays = 30);

/// <summary>Partial update of a mapping: any non-null field is applied.</summary>
public sealed record UpdateMappingRequest(
    PollingFrequency? Frequency,
    bool? IsActive,
    bool? IncludeReplies,
    ReplyScanFrequency? ReplySweepFrequency,
    int? ReplyWindowDays);

/// <summary>The selectable endpoints for building a mapping in the UI.</summary>
public sealed record MappingOptionsDto(
    IReadOnlyList<ChannelOption> YouTubeChannels,
    IReadOnlyList<SlackChannelOption> SlackChannels);

/// <summary>A YouTube channel option for the mapping form.</summary>
public sealed record ChannelOption(Guid Id, string Title);

/// <summary>A Slack channel option for the mapping form, qualified by its workspace.</summary>
public sealed record SlackChannelOption(Guid Id, string Name, string WorkspaceName, bool IsPrivate);

/// <summary>Thrown when a mapping already exists for the same (YouTube, Slack) pair (-> 409).</summary>
public sealed class MappingConflictException(string message) : Exception(message);

/// <summary>
/// CRUD over channel mappings (the link between a tracked YouTube channel and a Slack channel, with a
/// polling cadence). Slack workspace display names are resolved from the shared Connections store —
/// the module-local rows carry only the workspace id (no cross-schema join).
/// </summary>
public sealed class MappingService(
    YouTubeCommentsDbContext db,
    IPollingScheduler scheduler,
    ICommentsAudit audit,
    ISlackConnections slackConnections)
{
    /// <summary>Lists every mapping, newest first, flattened with its endpoint display names.</summary>
    public async Task<MappingDto[]> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.ChannelMappings
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Select(Projection)
            .ToListAsync(ct);

        var names = await WorkspaceNamesAsync(ct);
        return rows.Select(r => ToDto(r, names)).ToArray();
    }

    /// <summary>Returns the selectable YouTube channels and Slack channels for the mapping form.</summary>
    public async Task<MappingOptionsDto> GetOptionsAsync(CancellationToken ct = default)
    {
        var youtubeChannels = await db.YouTubeChannels
            .AsNoTracking()
            .OrderBy(c => c.Title)
            .Select(c => new ChannelOption(c.Id, c.Title))
            .ToListAsync(ct);

        var slackRows = await db.SlackChannels
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.WorkspaceId, c.IsPrivate })
            .ToListAsync(ct);

        var names = await WorkspaceNamesAsync(ct);
        var slackChannels = slackRows
            .Select(c => new SlackChannelOption(c.Id, c.Name, names.GetValueOrDefault(c.WorkspaceId, "—"), c.IsPrivate))
            .ToList();

        return new MappingOptionsDto(youtubeChannels, slackChannels);
    }

    /// <summary>
    /// Creates a mapping after validating both foreign keys exist (-> 400) and that no mapping already
    /// exists for the same pair (-> 409 via <see cref="MappingConflictException"/>).
    /// </summary>
    public async Task<MappingDto> CreateAsync(CreateMappingRequest req, CancellationToken ct = default)
    {
        if (!await db.YouTubeChannels.AnyAsync(c => c.Id == req.YouTubeChannelId, ct))
            throw new InvalidOperationException($"YouTube channel {req.YouTubeChannelId} not found.");
        if (!await db.SlackChannels.AnyAsync(c => c.Id == req.SlackChannelId, ct))
            throw new InvalidOperationException($"Slack channel {req.SlackChannelId} not found.");

        var duplicate = await db.ChannelMappings.AnyAsync(
            m => m.YouTubeChannelId == req.YouTubeChannelId && m.SlackChannelId == req.SlackChannelId, ct);
        if (duplicate)
            throw new MappingConflictException("A mapping for this YouTube channel and Slack channel already exists.");

        var now = DateTimeOffset.UtcNow;
        var entity = new ChannelMapping
        {
            YouTubeChannelId = req.YouTubeChannelId,
            SlackChannelId = req.SlackChannelId,
            Frequency = req.Frequency,
            IncludeReplies = req.IncludeReplies,
            ReplySweepFrequency = req.ReplySweepFrequency,
            ReplyWindowDays = ClampWindow(req.ReplyWindowDays),
            IsActive = true,
            // Watermark: only forward comments published from now on, so a newly mapped channel
            // doesn't flood Slack with its entire recent backlog.
            CommentsSinceUtc = now,
            CreatedAt = now,
        };

        db.ChannelMappings.Add(entity);
        await db.SaveChangesAsync(ct);

        if (entity.IsActive)
        {
            scheduler.Schedule(entity.Id, entity.Frequency);
            ReconcileReplySweep(entity);
        }

        await audit.LogAsync(AuditLevel.Information, "Mapping",
            $"Created mapping ({entity.Frequency}, replies: {entity.IncludeReplies})",
            "ChannelMapping", entity.Id.ToString(), ct: ct);

        return await GetDtoAsync(entity.Id, ct)
            ?? throw new InvalidOperationException("Mapping vanished immediately after creation.");
    }

    /// <summary>Applies any provided fields to a mapping. Returns the updated DTO, or <c>null</c> when not found.</summary>
    public async Task<MappingDto?> UpdateAsync(Guid id, UpdateMappingRequest req, CancellationToken ct = default)
    {
        var entity = await db.ChannelMappings.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null)
            return null;

        if (req.Frequency.HasValue) entity.Frequency = req.Frequency.Value;
        if (req.IncludeReplies.HasValue) entity.IncludeReplies = req.IncludeReplies.Value;
        if (req.ReplySweepFrequency.HasValue) entity.ReplySweepFrequency = req.ReplySweepFrequency.Value;
        if (req.ReplyWindowDays.HasValue) entity.ReplyWindowDays = ClampWindow(req.ReplyWindowDays.Value);
        if (req.IsActive.HasValue)
        {
            // Reactivating a dormant mapping advances the watermark to now so it can't repost old
            // comments whose dedup rows the retention job may have already pruned.
            if (req.IsActive.Value && !entity.IsActive)
                entity.CommentsSinceUtc = DateTimeOffset.UtcNow;
            entity.IsActive = req.IsActive.Value;
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        if (entity.IsActive)
        {
            scheduler.Schedule(entity.Id, entity.Frequency);
            ReconcileReplySweep(entity);
        }
        else
        {
            scheduler.Remove(entity.Id);
            scheduler.RemoveReplySweep(entity.Id);
        }

        await audit.LogAsync(AuditLevel.Information, "Mapping",
            $"Updated mapping (active: {entity.IsActive}, {entity.Frequency}, replies: {entity.IncludeReplies})",
            "ChannelMapping", entity.Id.ToString(), ct: ct);

        return await GetDtoAsync(id, ct);
    }

    /// <summary>Deletes a mapping. Returns <c>false</c> when not found.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.ChannelMappings.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null)
            return false;

        db.ChannelMappings.Remove(entity);
        await db.SaveChangesAsync(ct);

        scheduler.Remove(id);
        scheduler.RemoveReplySweep(id);

        await audit.LogAsync(AuditLevel.Information, "Mapping", "Deleted mapping", "ChannelMapping", id.ToString(), ct: ct);
        return true;
    }

    private async Task<MappingDto?> GetDtoAsync(Guid id, CancellationToken ct)
    {
        var row = await db.ChannelMappings
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        var names = await WorkspaceNamesAsync(ct);
        return ToDto(row, names);
    }

    private async Task<Dictionary<Guid, string>> WorkspaceNamesAsync(CancellationToken ct)
    {
        var workspaces = await slackConnections.ListAsync(ct);
        return workspaces.ToDictionary(w => w.Id, w => w.TeamName);
    }

    private static MappingDto ToDto(MappingRow r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.YouTubeChannelId, r.YtTitle, r.YtThumb, r.SlackChannelId, r.SlackName,
        names.GetValueOrDefault(r.WorkspaceId, "—"), r.Frequency, r.IsActive, r.IncludeReplies,
        r.ReplySweepFrequency, r.ReplyWindowDays, r.LastPolledAt, r.LastError, r.CreatedAt);

    private static readonly System.Linq.Expressions.Expression<Func<ChannelMapping, MappingRow>> Projection =
        m => new MappingRow(
            m.Id, m.YouTubeChannelId, m.YouTubeChannel!.Title, m.YouTubeChannel.ThumbnailUrl,
            m.SlackChannelId, m.SlackChannel!.Name, m.SlackChannel.WorkspaceId,
            m.Frequency, m.IsActive, m.IncludeReplies, m.ReplySweepFrequency, m.ReplyWindowDays,
            m.LastPolledAt, m.LastError, m.CreatedAt);

    private void ReconcileReplySweep(ChannelMapping m)
    {
        if (m.IncludeReplies && m.ReplySweepFrequency != ReplyScanFrequency.Off)
            scheduler.ScheduleReplySweep(m.Id, m.ReplySweepFrequency);
        else
            scheduler.RemoveReplySweep(m.Id);
    }

    private static int ClampWindow(int days) => Math.Clamp(days, 1, 90);

    private sealed record MappingRow(
        Guid Id, Guid YouTubeChannelId, string YtTitle, string? YtThumb,
        Guid SlackChannelId, string SlackName, Guid WorkspaceId,
        PollingFrequency Frequency, bool IsActive, bool IncludeReplies,
        ReplyScanFrequency ReplySweepFrequency, int ReplyWindowDays,
        DateTimeOffset? LastPolledAt, string? LastError, DateTimeOffset CreatedAt);
}
