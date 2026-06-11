using System.Globalization;

using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

// ── Design-shape DTOs the frontend ytu hooks consume (queue.jsx / uploads.jsx). The BFF prepends
//    /api, so these are served at /api/youtube-uploads/{jobs|upload-history|upload-mappings}. ──

public sealed record QueueJobDto(
    string Id, string Title, string Status, int Progress, string Source, double SizeMB,
    string Target, string Channel, string Account, long Elapsed, long? Eta, string By,
    string? VideoUrl, string? FinishedAgo, string? Error);

public sealed record HistoryItemDto(
    string Id, string Title, string Account, string Slack, double SizeMB, long? Duration,
    string Privacy, string Status, string Finished, string By, int? Views, string? VideoUrl, string? Error);

public sealed record MappingViewDto(
    string Id, string Slack, string Workspace, string Account, string Key,
    string Privacy, string Playlist, bool Active, int Up24);

/// <summary>
/// Read model that maps the module's domain into the exact shapes the uploads UI expects, so the
/// ytu hooks swap <c>mockFetch(DATA.x)</c> → <c>api.get('/youtube-uploads/…')</c> with zero component changes.
/// </summary>
public sealed class UploadsReadService(
    YouTubeUploadsDbContext db,
    IProgressTracker progress,
    IGoogleConnections googleAccounts,
    ChannelMappingService mappings,
    UploadSettingsService settings)
{
    /// <summary>Active + queued + last-24h finished jobs across all channels, mapped to the queue card shape.</summary>
    public async Task<IReadOnlyList<QueueJobDto>> GetQueueAsync(CancellationToken ct = default)
    {
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Queued || j.State == JobState.Downloading
                     || j.State == JobState.Uploading || j.State == JobState.Processing
                     || j.State == JobState.Blocked
                     || ((j.State == JobState.Done || j.State == JobState.Failed || j.State == JobState.Cancelled)
                         && j.UpdatedAt >= dayAgo))
            .OrderByDescending(j => j.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);

        var channelNames = await ChannelNamesAsync(jobs.Select(j => j.SlackChannelId), ct);
        var accountTitles = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.ChannelTitle);
        var now = DateTimeOffset.UtcNow;

        return jobs.Select(j =>
        {
            var live = progress.Get(j.Id);
            var pct = live?.Percent ?? (j.State == JobState.Done ? 100 : Percent(j.BytesTransferred, j.BytesTotal));
            var terminal = j.State is JobState.Done or JobState.Failed or JobState.Cancelled or JobState.Blocked;
            var elapsedFrom = j.DownloadStartedAt ?? j.CreatedAt;
            var elapsed = (long)Math.Max(0, ((terminal ? j.UpdatedAt : now) - elapsedFrom).TotalSeconds);

            return new QueueJobDto(
                j.Id.ToString(),
                j.Title ?? j.OriginalFileName ?? "Untitled",
                StatusLabel(j.State),
                Math.Clamp(pct, 0, 100),
                $"Drive · {j.OriginalFileName ?? j.Title ?? "video"}",
                Megabytes(j.BytesTotal),
                "YouTube",
                channelNames.GetValueOrDefault(j.SlackChannelId, j.SlackChannelId),
                j.GoogleAccountId is { } gid ? accountTitles.GetValueOrDefault(gid, "—") : "—",
                elapsed,
                null,
                string.IsNullOrEmpty(j.SlackUserId) ? "—" : j.SlackUserId,
                j.YouTubeUrl,
                terminal ? RelativeAgo(now - j.UpdatedAt) : null,
                j.ErrorMessage);
        }).ToList();
    }

    /// <summary>Finished uploads (done/failed/cancelled), newest first, mapped to the history-row shape.</summary>
    public async Task<IReadOnlyList<HistoryItemDto>> GetHistoryAsync(int take = 100, CancellationToken ct = default)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Done || j.State == JobState.Failed || j.State == JobState.Cancelled)
            .OrderByDescending(j => j.UpdatedAt)
            .Take(take)
            .ToListAsync(ct);

        var channelNames = await ChannelNamesAsync(jobs.Select(j => j.SlackChannelId), ct);
        var accountTitles = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.ChannelTitle);
        var privacy = Capitalize((await settings.GetUploadSettingsAsync(ct)).Visibility);

        return jobs.Select(j =>
        {
            long? duration = j.State == JobState.Done && j.DownloadStartedAt is { } start
                ? (long)Math.Max(0, (j.UpdatedAt - start).TotalSeconds)
                : null;
            return new HistoryItemDto(
                j.Id.ToString(),
                j.Title ?? j.OriginalFileName ?? "Untitled",
                j.GoogleAccountId is { } gid ? accountTitles.GetValueOrDefault(gid, "—") : "—",
                channelNames.GetValueOrDefault(j.SlackChannelId, j.SlackChannelId),
                Megabytes(j.BytesTotal),
                duration,
                j.State == JobState.Done ? privacy : "—",
                StatusLabel(j.State),
                j.UpdatedAt.UtcDateTime.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(j.SlackUserId) ? "—" : j.SlackUserId,
                null,
                j.YouTubeUrl,
                j.ErrorMessage);
        }).ToList();
    }

    /// <summary>Channel→account mappings, mapped to the mapping-row shape (active toggle + 24h count).</summary>
    public async Task<IReadOnlyList<MappingViewDto>> GetMappingsAsync(CancellationToken ct = default)
    {
        var list = await mappings.ListAsync(ct);
        var privacy = Capitalize((await settings.GetUploadSettingsAsync(ct)).Visibility);
        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);

        var up24 = (await db.Jobs.AsNoTracking()
                .Where(j => j.State == JobState.Done && j.UpdatedAt >= dayAgo)
                .Select(j => j.SlackChannelId)
                .ToListAsync(ct))
            .GroupBy(c => c)
            .ToDictionary(g => g.Key, g => g.Count());

        return list.Select(m => new MappingViewDto(
            m.Id.ToString(),
            m.SlackChannelName,
            m.SlackWorkspaceName,
            m.GoogleAccountLabel,
            m.GoogleAccountChannelId ?? "—",
            privacy,
            "—",
            m.IsActive,
            up24.GetValueOrDefault(m.SlackChannelId))).ToList();
    }

    private async Task<Dictionary<string, string>> ChannelNamesAsync(IEnumerable<string> channelIds, CancellationToken ct)
    {
        var ids = channelIds.Distinct().ToList();
        return (await db.SlackChannels.AsNoTracking()
                .Where(c => ids.Contains(c.SlackChannelId))
                .Select(c => new { c.SlackChannelId, c.Name })
                .ToListAsync(ct))
            .GroupBy(c => c.SlackChannelId)
            .ToDictionary(g => g.Key, g => g.First().Name);
    }

    private static int Percent(long done, long total) => total <= 0 ? 0 : (int)Math.Clamp(done * 100 / total, 0, 100);

    private static double Megabytes(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);

    private static string StatusLabel(JobState s) => s switch
    {
        JobState.Queued or JobState.Blocked => "queued",
        JobState.Downloading => "downloading",
        JobState.Uploading => "uploading",
        JobState.Processing => "processing",
        JobState.Done => "done",
        JobState.Failed => "failed",
        JobState.Cancelled => "canceled",
        _ => "queued",
    };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string RelativeAgo(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }
}
