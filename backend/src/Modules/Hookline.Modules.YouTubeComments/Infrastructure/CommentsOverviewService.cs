using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

// ── Overview panel DTOs (ASP.NET serializes camelCase by default → the TS field names match) ──

/// <summary>Forwarded / removed counts for one rolling window, across the whole Comments tool.</summary>
public sealed record CommentsWindowCounts(int Forwarded, int Removed);

/// <summary>One monitored channel's contribution to the Comments overview: all-time forwarded plus the
/// rolling windows, joined ProcessedComments → ChannelMapping → YouTubeChannel.</summary>
public sealed record CommentsChannelStat(
    string ChannelTitle,
    int Forwarded,
    int Forwarded24h,
    int Forwarded7d,
    int Forwarded30d,
    int Removed24h,
    int Removed7d,
    int Removed30d);

/// <summary>Today's Comments quota figure, read verbatim from <see cref="DashboardService"/> (NOT recomputed).</summary>
public sealed record CommentsQuotaDto(long Used, long Ceiling, double Percent);

/// <summary>The Comments half of the Overview page: all-time + windowed forwarded/removed totals, the
/// per-channel breakdown, and today's daily quota figure.</summary>
public sealed record CommentsOverviewDto(
    int TotalForwarded,
    CommentsWindowCounts Window24h,
    CommentsWindowCounts Window7d,
    CommentsWindowCounts Window30d,
    IReadOnlyList<CommentsChannelStat> PerChannel,
    CommentsQuotaDto Quota);

/// <summary>
/// Read-only cross-table aggregate for the Comments panel of the Overview page. Forwarded counts come from
/// <c>processed_comments</c>, removed counts from the <c>comment_moderations</c> ledger; both are summarised
/// over the last 24h / 7d / 30d (from <see cref="DateTimeOffset.UtcNow"/>) overall and per monitored channel.
/// The daily quota figure is consumed verbatim from <see cref="DashboardService"/> — this service never
/// recomputes the quota math. All queries are <c>AsNoTracking</c> + grouped (no N+1 per channel).
/// </summary>
public sealed class CommentsOverviewService(YouTubeCommentsDbContext db, DashboardService dashboard)
{
    public async Task<CommentsOverviewDto> GetAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24);
        var since7d = now.AddDays(-7);
        var since30d = now.AddDays(-30);

        var totalForwarded = await db.ProcessedComments.AsNoTracking().CountAsync(ct);

        // ── Forwarded windows, grouped by the owning YouTube channel in ONE query ──
        // ProcessedComments → ChannelMapping (MappingId) → YouTubeChannel. Conditional SUMs collapse the
        // per-window counts into a single grouped projection so there is no per-channel round-trip.
        var forwardedByChannel = await (
            from pc in db.ProcessedComments.AsNoTracking()
            join cm in db.ChannelMappings.AsNoTracking() on pc.MappingId equals cm.Id
            join yt in db.YouTubeChannels.AsNoTracking() on cm.YouTubeChannelId equals yt.Id
            where pc.ProcessedAt >= since30d
            group pc by new { yt.Id, yt.Title } into g
            select new
            {
                g.Key.Id,
                g.Key.Title,
                Fwd24h = g.Count(x => x.ProcessedAt >= since24h),
                Fwd7d = g.Count(x => x.ProcessedAt >= since7d),
                Fwd30d = g.Count(),
            }).ToListAsync(ct);

        // All-time forwarded per channel (no window filter) — also one grouped query.
        var totalForwardedByChannel = await (
            from pc in db.ProcessedComments.AsNoTracking()
            join cm in db.ChannelMappings.AsNoTracking() on pc.MappingId equals cm.Id
            join yt in db.YouTubeChannels.AsNoTracking() on cm.YouTubeChannelId equals yt.Id
            group pc by new { yt.Id, yt.Title } into g
            select new { g.Key.Id, g.Key.Title, Total = g.Count() }).ToListAsync(ct);

        // ── Removed windows, grouped by channel via the moderation → mapping → channel join ──
        var removedByChannel = await (
            from mod in db.CommentModerations.AsNoTracking()
            join cm in db.ChannelMappings.AsNoTracking() on mod.MappingId equals cm.Id
            join yt in db.YouTubeChannels.AsNoTracking() on cm.YouTubeChannelId equals yt.Id
            where mod.CreatedAt >= since30d
            group mod by new { yt.Id, yt.Title } into g
            select new
            {
                g.Key.Id,
                g.Key.Title,
                Rem24h = g.Count(x => x.CreatedAt >= since24h),
                Rem7d = g.Count(x => x.CreatedAt >= since7d),
                Rem30d = g.Count(),
            }).ToListAsync(ct);

        var totalByChannel = totalForwardedByChannel.ToDictionary(x => x.Id, x => x);
        var fwdByChannel = forwardedByChannel.ToDictionary(x => x.Id, x => x);
        var remByChannel = removedByChannel.ToDictionary(x => x.Id, x => x);

        // Union the channel ids seen across the three projections, then fold each into a per-channel row.
        var channelIds = totalByChannel.Keys
            .Union(fwdByChannel.Keys)
            .Union(remByChannel.Keys)
            .ToList();

        var perChannel = channelIds
            .Select(id =>
            {
                var title = totalByChannel.GetValueOrDefault(id)?.Title
                    ?? fwdByChannel.GetValueOrDefault(id)?.Title
                    ?? remByChannel.GetValueOrDefault(id)?.Title
                    ?? "—";
                var f = fwdByChannel.GetValueOrDefault(id);
                var r = remByChannel.GetValueOrDefault(id);
                return new CommentsChannelStat(
                    ChannelTitle: title,
                    Forwarded: totalByChannel.GetValueOrDefault(id)?.Total ?? 0,
                    Forwarded24h: f?.Fwd24h ?? 0,
                    Forwarded7d: f?.Fwd7d ?? 0,
                    Forwarded30d: f?.Fwd30d ?? 0,
                    Removed24h: r?.Rem24h ?? 0,
                    Removed7d: r?.Rem7d ?? 0,
                    Removed30d: r?.Rem30d ?? 0);
            })
            .OrderByDescending(c => c.Forwarded)
            .ThenBy(c => c.ChannelTitle)
            .ToList();

        // ── Overall windows: cheap conditional counts straight off each table ──
        var fwd24h = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= since24h, ct);
        var fwd7d = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= since7d, ct);
        var fwd30d = await db.ProcessedComments.AsNoTracking().CountAsync(c => c.ProcessedAt >= since30d, ct);

        var rem24h = await db.CommentModerations.AsNoTracking().CountAsync(m => m.CreatedAt >= since24h, ct);
        var rem7d = await db.CommentModerations.AsNoTracking().CountAsync(m => m.CreatedAt >= since7d, ct);
        var rem30d = await db.CommentModerations.AsNoTracking().CountAsync(m => m.CreatedAt >= since30d, ct);

        // Daily quota figure — consumed verbatim from DashboardService (the metered/estimated source of truth).
        var stats = await dashboard.GetStatsAsync(ct);

        return new CommentsOverviewDto(
            TotalForwarded: totalForwarded,
            Window24h: new CommentsWindowCounts(fwd24h, rem24h),
            Window7d: new CommentsWindowCounts(fwd7d, rem7d),
            Window30d: new CommentsWindowCounts(fwd30d, rem30d),
            PerChannel: perChannel,
            Quota: new CommentsQuotaDto(stats.EstimatedDailyUnits, stats.QuotaCeiling, stats.EstimatedPercent));
    }
}
