using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

// ── Overview panel DTOs (ASP.NET serializes camelCase by default → the TS field names match) ──

/// <summary>Upload outcomes for one rolling window, split by terminal state.</summary>
public sealed record UploadsWindowCounts(int Done, int Failed, int Canceled);

/// <summary>One Google account's contribution to the Uploads overview: total successful uploads plus the
/// done/failed/canceled split, resolved from <c>GoogleAccountId</c> via the shared Connections accessor.</summary>
public sealed record UploadsAccountStat(string AccountTitle, int Total, int Done, int Failed, int Canceled);

/// <summary>Today's videos.insert bucket usage summed across every project (NOT the unit pool).</summary>
public sealed record UploadsBucketDto(int Used, int Limit);

/// <summary>The Uploads half of the Overview page: all-time successful uploads, the per-account breakdown,
/// the windowed outcome splits, and today's videos.insert bucket usage.</summary>
public sealed record UploadsOverviewDto(
    int TotalUploads,
    UploadsWindowCounts Window24h,
    UploadsWindowCounts Window7d,
    UploadsWindowCounts Window30d,
    IReadOnlyList<UploadsAccountStat> PerAccount,
    UploadsBucketDto Bucket);

/// <summary>
/// Read-only aggregate for the Uploads panel of the Overview page. Finished jobs are <c>upload_jobs</c> rows
/// in a terminal state (Done/Failed/Cancelled) — there is no separate history table — so the outcome split
/// over the last 24h / 7d / 30d (from <see cref="DateTimeOffset.UtcNow"/>, by <c>UpdatedAt</c>) and the
/// per-account breakdown are both grouped queries off the Jobs table (<c>AsNoTracking</c>, no N+1). The
/// per-account label resolves <c>GoogleAccountId</c> via <see cref="IGoogleConnections"/>; today's
/// videos.insert bucket usage sums <see cref="IQuotaService"/> across every project.
/// </summary>
public sealed class UploadsOverviewService(
    YouTubeUploadsDbContext db,
    IGoogleConnections googleAccounts,
    IQuotaService quota,
    GoogleProjectsService projects)
{
    public async Task<UploadsOverviewDto> GetAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);
        var since7d = now.AddDays(-7);
        var since30d = now.AddDays(-30);

        var totalUploads = await db.Jobs.AsNoTracking().CountAsync(j => j.State == JobState.Done, ct);

        // ── Outcome windows: one query pulls the last-30d terminal rows; the 24h/7d/30d × {done,failed,
        //    canceled} matrix is then folded in memory (a single DB round-trip, not nine count queries) ──
        var terminal = await db.Jobs.AsNoTracking()
            .Where(j => (j.State == JobState.Done || j.State == JobState.Failed || j.State == JobState.Cancelled)
                     && j.UpdatedAt >= since30d)
            .Select(j => new TerminalRow(j.State, j.UpdatedAt))
            .ToListAsync(ct);

        var window24h = WindowCounts(terminal.Where(j => j.UpdatedAt >= since24h));
        var window7d = WindowCounts(terminal.Where(j => j.UpdatedAt >= since7d));
        var window30d = WindowCounts(terminal);

        // ── Per-account breakdown: all-time terminal jobs grouped by GoogleAccountId (one grouped query) ──
        var byAccount = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Done || j.State == JobState.Failed || j.State == JobState.Cancelled)
            .GroupBy(j => j.GoogleAccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Done = g.Count(x => x.State == JobState.Done),
                Failed = g.Count(x => x.State == JobState.Failed),
                Canceled = g.Count(x => x.State == JobState.Cancelled),
            })
            .ToListAsync(ct);

        var accountTitles = (await googleAccounts.ListAsync(ct)).ToDictionary(a => a.Id, a => a.ChannelTitle);

        var perAccount = byAccount
            .Select(g => new UploadsAccountStat(
                AccountTitle: g.AccountId is { } id ? accountTitles.GetValueOrDefault(id, "—") : "—",
                Total: g.Done,
                Done: g.Done,
                Failed: g.Failed,
                Canceled: g.Canceled))
            .OrderByDescending(a => a.Done)
            .ThenBy(a => a.AccountTitle)
            .ToList();

        // ── Today's videos.insert bucket: sum the per-project upload counters (NOT the unit pool) ──
        var projectList = await projects.ListAsync(ct);
        var usedUploads = 0;
        var uploadLimit = 0;
        foreach (var project in projectList)
        {
            var status = await quota.GetStatusAsync(project.Id);
            usedUploads += status.UsedUploads;
            uploadLimit += status.TotalUploads;
        }

        return new UploadsOverviewDto(
            TotalUploads: totalUploads,
            Window24h: window24h,
            Window7d: window7d,
            Window30d: window30d,
            PerAccount: perAccount,
            Bucket: new UploadsBucketDto(usedUploads, uploadLimit));
    }

    private static UploadsWindowCounts WindowCounts(IEnumerable<TerminalRow> rows)
    {
        var done = 0;
        var failed = 0;
        var canceled = 0;
        foreach (var row in rows)
        {
            switch (row.State)
            {
                case JobState.Done: done++; break;
                case JobState.Failed: failed++; break;
                case JobState.Cancelled: canceled++; break;
            }
        }

        return new UploadsWindowCounts(done, failed, canceled);
    }

    /// <summary>A terminal job's state + finish time — the only fields the window-count fold needs.</summary>
    private sealed record TerminalRow(JobState State, DateTimeOffset UpdatedAt);
}
