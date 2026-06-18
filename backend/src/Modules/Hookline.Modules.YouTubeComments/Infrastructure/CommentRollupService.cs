using Hookline.Modules.YouTubeComments.Domain;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Builds the nightly <c>comment_daily_stats</c> rollup: for a UTC day it aggregates that day's
/// <c>processed_comments</c> + <c>comment_moderations</c> per mapping and writes one durable row each, so
/// long-horizon trends survive the retention cleanup of the raw rows. A re-roll of the same day is
/// idempotent (the day's existing rows are replaced), which lets the nightly job overlap the cleanup job's
/// belt-and-braces re-roll without double counting.
/// </summary>
public sealed class CommentRollupService(YouTubeCommentsDbContext db)
{
    /// <summary>Rolls up a single UTC day, replacing any existing rows for that day. Returns the row count written.</summary>
    public async Task<int> RollupDayAsync(DateOnly day, CancellationToken ct = default)
    {
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);

        var forwarded = await db.ProcessedComments.AsNoTracking()
            .Where(c => c.ProcessedAt >= start && c.ProcessedAt < end)
            .GroupBy(c => c.MappingId)
            .Select(g => new
            {
                MappingId = g.Key,
                Forwarded = g.Count(),
                Replies = g.Count(c => c.ParentCommentId != null),
                SumLikes = g.Sum(c => c.LikeCount),
                Enriched = g.Count(c => c.LikeCount != null),
            })
            .ToListAsync(ct);

        var removed = await db.CommentModerations.AsNoTracking()
            .Where(m => m.CreatedAt >= start && m.CreatedAt < end)
            .GroupBy(m => m.MappingId)
            .Select(g => new
            {
                MappingId = g.Key,
                Removed = g.Count(),
                Rejected = g.Count(m => m.Status == CommentModeration.StatusRejected),
                AlreadyGone = g.Count(m => m.Status == CommentModeration.StatusAlreadyGone),
            })
            .ToListAsync(ct);

        var mappingIds = forwarded.Select(x => x.MappingId)
            .Union(removed.Select(x => x.MappingId))
            .ToList();

        if (mappingIds.Count == 0)
        {
            // Nothing happened this day — still clear any stale rows so a re-roll of a now-empty day is correct.
            await ReplaceDayAsync(day, [], ct);
            return 0;
        }

        var titles = await (
            from m in db.ChannelMappings.AsNoTracking()
            join yt in db.YouTubeChannels.AsNoTracking() on m.YouTubeChannelId equals yt.Id
            where mappingIds.Contains(m.Id)
            select new { m.Id, yt.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);

        var fwdById = forwarded.ToDictionary(x => x.MappingId);
        var remById = removed.ToDictionary(x => x.MappingId);

        var rows = mappingIds.Select(id =>
        {
            var f = fwdById.GetValueOrDefault(id);
            var r = remById.GetValueOrDefault(id);
            return new CommentDailyStat
            {
                Date = day,
                MappingId = id,
                ChannelTitle = titles.GetValueOrDefault(id) ?? "—",
                Forwarded = f?.Forwarded ?? 0,
                Replies = f?.Replies ?? 0,
                SumLikes = f?.SumLikes ?? 0,
                EnrichedCount = f?.Enriched ?? 0,
                Removed = r?.Removed ?? 0,
                Rejected = r?.Rejected ?? 0,
                AlreadyGone = r?.AlreadyGone ?? 0,
            };
        }).ToList();

        await ReplaceDayAsync(day, rows, ct);
        return rows.Count;
    }

    /// <summary>Rolls up every UTC day in <paramref name="from"/>..<paramref name="to"/> inclusive.</summary>
    public async Task<int> RollupRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var total = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
            total += await RollupDayAsync(day, ct);
        return total;
    }

    /// <summary>Re-rolls the last <paramref name="days"/> COMPLETE UTC days (yesterday backwards). Today is
    /// excluded because it is still accumulating and is served live by the dashboard.</summary>
    public Task<int> RollupTrailingDaysAsync(int days, CancellationToken ct = default)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var from = yesterday.AddDays(-(Math.Max(1, days) - 1));
        return RollupRangeAsync(from, yesterday, ct);
    }

    /// <summary>Replaces a day's rollup rows in one save (load-existing + remove + add) — works on both the
    /// relational provider and the in-memory test provider (no <c>ExecuteDelete</c>).</summary>
    private async Task ReplaceDayAsync(DateOnly day, IReadOnlyList<CommentDailyStat> rows, CancellationToken ct)
    {
        var existing = await db.CommentDailyStats.Where(x => x.Date == day).ToListAsync(ct);
        if (existing.Count > 0)
            db.CommentDailyStats.RemoveRange(existing);
        if (rows.Count > 0)
            db.CommentDailyStats.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }
}
