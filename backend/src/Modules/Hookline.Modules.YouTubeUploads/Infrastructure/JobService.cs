using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record NewJob(
    string SlackEventId,
    string ChannelId,
    string UserId,
    string MessageTs,
    string DriveFileId,
    string? OriginalFileName,
    string? Title,
    string? Description,
    List<string> Tags,
    bool RequiresConfirmation,
    Guid? GoogleAccountId,
    string? ThumbnailUrl = null,
    string? ThumbnailMimeType = null);

public sealed record StatusSnapshot(
    UploadJob? Active,
    IReadOnlyList<UploadJob> Queued,
    IReadOnlyList<UploadJob> Recent,
    int UploadedLast24h);

/// <summary>Optional, AND-combined filters for the admin job history grid (all null = match all).</summary>
public sealed record JobHistoryFilter(
    JobState? State,
    string? Channel,
    string? Tag,
    Guid? Account,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Search);

/// <summary>
/// A history page already enriched with display names. The account label is resolved through the shared
/// <see cref="IGoogleConnections"/> accessor (never a cross-schema join — accounts live in <c>connections</c>),
/// and channel names from the module's own channel cache.
/// </summary>
public sealed record JobHistoryItem(
    UploadJob Job,
    string? ChannelName,
    string? GoogleAccountLabel);

/// <summary>Facet options for the filter UI — only values that actually occur in the jobs table.</summary>
public sealed record JobFilterOptions(
    IReadOnlyList<ChannelFacet> Channels,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AccountFacet> Accounts);

public sealed record ChannelFacet(string Id, string Name);
public sealed record AccountFacet(string Id, string Label);

public interface IJobService
{
    Task<UploadJob> CreateAsync(NewJob input, CancellationToken ct = default);
    Task<bool> ExistsForEventAsync(string slackEventId, CancellationToken ct = default);
    Task<bool> ExistsForChannelMessageAsync(string channelId, string ts, CancellationToken ct = default);
    Task<UploadJob?> GetAsync(Guid id, CancellationToken ct = default);
    Task TransitionAsync(UploadJob job, JobState to, string? note = null, CancellationToken ct = default);
    Task SaveAsync(UploadJob job, CancellationToken ct = default);
    Task<StatusSnapshot> GetStatusSnapshotAsync(string slackChannelId, int recentCount = 5, CancellationToken ct = default);
    Task<IReadOnlyList<UploadJob>> GetHistoryAsync(int take = 50, CancellationToken ct = default);
    Task<(IReadOnlyList<JobHistoryItem> Items, int Total)> GetHistoryPagedAsync(JobHistoryFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<JobFilterOptions> GetJobFilterOptionsAsync(CancellationToken ct = default);
    Task<(int UploadsToday, int UploadsLast24h, int ErrorsLast24h)> GetDashboardCountsAsync(CancellationToken ct = default);
}

public sealed class JobService(YouTubeUploadsDbContext db, IGoogleConnections googleAccounts) : IJobService
{
    public async Task<UploadJob> CreateAsync(NewJob i, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            SlackEventId = i.SlackEventId,
            SlackChannelId = i.ChannelId,
            SlackUserId = i.UserId,
            SlackMessageTs = i.MessageTs,
            DriveFileId = i.DriveFileId,
            GoogleAccountId = i.GoogleAccountId,
            OriginalFileName = i.OriginalFileName,
            Title = i.Title,
            Description = i.Description,
            Tags = i.Tags,
            ThumbnailUrl = i.ThumbnailUrl,
            ThumbnailMimeType = i.ThumbnailMimeType,
            RequiresConfirmation = i.RequiresConfirmation,
            Confirmed = i.RequiresConfirmation ? null : true,
            State = JobState.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };
        job.History.Add(new JobStateHistory { ToState = JobState.Queued, At = now, Note = "created" });
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public Task<bool> ExistsForEventAsync(string slackEventId, CancellationToken ct = default)
        => db.Jobs.AnyAsync(j => j.SlackEventId == slackEventId, ct);

    // One Slack post = one upload, even if Slack ever emits two events for it with different event_ids
    // (e.g. a bare message + a file_share for the same image post). ts is unique per message in a channel.
    public Task<bool> ExistsForChannelMessageAsync(string channelId, string ts, CancellationToken ct = default)
        => db.Jobs.AnyAsync(j => j.SlackChannelId == channelId && j.SlackMessageTs == ts, ct);

    public Task<UploadJob?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task TransitionAsync(UploadJob job, JobState to, string? note = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        job.History.Add(new JobStateHistory
        {
            JobId = job.Id,
            FromState = job.State,
            ToState = to,
            At = now,
            Note = note,
        });
        job.State = to;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveAsync(UploadJob job, CancellationToken ct = default)
    {
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<StatusSnapshot> GetStatusSnapshotAsync(string slackChannelId, int recentCount = 5, CancellationToken ct = default)
    {
        var active = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && (j.State == JobState.Downloading
                      || j.State == JobState.Uploading
                      || j.State == JobState.Processing))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var queued = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && j.State == JobState.Queued && (!j.RequiresConfirmation || j.Confirmed == true))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);

        // Recent finished jobs, capped at recentCount AND bounded to the last 24h so the status list
        // self-prunes — items older than a day drop off instead of lingering forever as the top-N.
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        var recent = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && j.UpdatedAt >= dayAgo
                     && (j.State == JobState.Done
                      || j.State == JobState.Cancelled
                      || j.State == JobState.Failed
                      || j.State == JobState.Blocked))
            .OrderByDescending(j => j.UpdatedAt)
            .Take(recentCount)
            .ToListAsync(ct);

        var uploadedLast24h = await db.Jobs.AsNoTracking()
            .CountAsync(j => j.SlackChannelId == slackChannelId
                          && j.State == JobState.Done
                          && j.UpdatedAt >= dayAgo, ct);

        return new StatusSnapshot(active, queued, recent, uploadedLast24h);
    }

    public async Task<IReadOnlyList<UploadJob>> GetHistoryAsync(int take = 50, CancellationToken ct = default)
        => await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<JobHistoryItem> Items, int Total)> GetHistoryPagedAsync(
        JobHistoryFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Jobs.AsNoTracking().AsQueryable();
        if (filter.State is not null) query = query.Where(j => j.State == filter.State.Value);
        if (!string.IsNullOrEmpty(filter.Channel)) query = query.Where(j => j.SlackChannelId == filter.Channel);
        // jsonb containment: Npgsql translates List<string>.Contains to the @> operator over the jsonb column.
        if (!string.IsNullOrEmpty(filter.Tag)) query = query.Where(j => j.Tags.Contains(filter.Tag));
        if (filter.Account is not null) query = query.Where(j => j.GoogleAccountId == filter.Account.Value);
        if (filter.From is not null) query = query.Where(j => j.CreatedAt >= filter.From.Value);
        if (filter.To is not null) query = query.Where(j => j.CreatedAt <= filter.To.Value);
        if (!string.IsNullOrEmpty(filter.Search))
        {
            var escaped = filter.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(j => EF.Functions.ILike((j.OriginalFileName ?? j.Title) ?? "", pattern, "\\"));
        }

        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Channel names from the module's own cache (no N+1, one query over the page's ids).
        var channelIds = jobs.Select(j => j.SlackChannelId).Distinct().ToList();
        var channelNames = (await db.SlackChannels.AsNoTracking()
                .Where(c => channelIds.Contains(c.SlackChannelId))
                .Select(c => new { c.SlackChannelId, c.Name })
                .ToListAsync(ct))
            .GroupBy(c => c.SlackChannelId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        // Account labels via the shared accessor (contract, not a join).
        var accountLabels = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.ChannelTitle);

        var items = jobs.Select(j => new JobHistoryItem(
            j,
            channelNames.GetValueOrDefault(j.SlackChannelId),
            j.GoogleAccountId is { } gid ? accountLabels.GetValueOrDefault(gid) : null)).ToList();

        return (items, total);
    }

    public async Task<JobFilterOptions> GetJobFilterOptionsAsync(CancellationToken ct = default)
    {
        var channelIds = await db.Jobs.AsNoTracking()
            .Select(j => j.SlackChannelId).Distinct().ToListAsync(ct);
        var channelNames = (await db.SlackChannels.AsNoTracking()
                .Where(c => channelIds.Contains(c.SlackChannelId))
                .Select(c => new { c.SlackChannelId, c.Name })
                .ToListAsync(ct))
            .GroupBy(c => c.SlackChannelId)
            .ToDictionary(g => g.Key, g => g.First().Name);
        var channels = channelIds
            .Select(id => new ChannelFacet(id, channelNames.GetValueOrDefault(id, id)))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        // SelectMany over a jsonb List<string> doesn't translate in Npgsql — unnest via raw SQL instead.
        var tags = await db.Database
            .SqlQueryRaw<string>("SELECT DISTINCT jsonb_array_elements_text(\"tags\") AS \"Value\" FROM youtube_uploads.upload_jobs")
            .ToListAsync(ct);
        tags.Sort(StringComparer.Ordinal);

        var accountIds = await db.Jobs.AsNoTracking()
            .Where(j => j.GoogleAccountId != null)
            .Select(j => j.GoogleAccountId!.Value).Distinct().ToListAsync(ct);
        var accountTitles = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.ChannelTitle);
        var accounts = accountIds
            .Select(id => new AccountFacet(id.ToString(), accountTitles.GetValueOrDefault(id, id.ToString())))
            .OrderBy(a => a.Label, StringComparer.Ordinal)
            .ToList();

        return new JobFilterOptions(channels, tags, accounts);
    }

    public async Task<(int UploadsToday, int UploadsLast24h, int ErrorsLast24h)> GetDashboardCountsAsync(
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dayAgo = now.AddHours(-24);
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var uploadsToday = await db.Jobs.CountAsync(j => j.State == JobState.Done && j.UpdatedAt >= todayStart, ct);
        var uploadsLast24h = await db.Jobs.CountAsync(j => j.State == JobState.Done && j.UpdatedAt >= dayAgo, ct);
        var errorsLast24h = await db.Jobs.CountAsync(j => j.State == JobState.Failed && j.UpdatedAt >= dayAgo, ct);
        return (uploadsToday, uploadsLast24h, errorsLast24h);
    }
}
