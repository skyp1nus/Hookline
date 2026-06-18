using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Covers the nightly rollup (<see cref="CommentRollupService"/>) and the long-horizon history read
/// (<see cref="CommentsStatsService.GetHistoryAsync"/>): a day's raw activity is aggregated per mapping
/// into <c>comment_daily_stats</c>, re-rolling is idempotent, and history reads the durable archive
/// 0-filled (today excluded). In-memory EF provider (<see cref="TestDb"/>).
/// </summary>
public class CommentRollupTests
{
    [Fact]
    public async Task Rollup_aggregates_a_day_per_mapping_and_is_idempotent()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db, "Chan");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-2);
        var at = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, TimeSpan.Zero);

        AddProcessed(db, mappingId, "C1", at, likeCount: 5);
        AddProcessed(db, mappingId, "C2", at, likeCount: 15);
        AddProcessed(db, mappingId, "R1", at, parentId: "C1", likeCount: 0);
        // A comment on the NEXT day — must not bleed into this day's rollup.
        AddProcessed(db, mappingId, "C3", at.AddDays(1));
        db.CommentModerations.Add(Moderation(mappingId, "C1", at, CommentModeration.StatusRejected));
        db.CommentModerations.Add(Moderation(mappingId, "C2", at, CommentModeration.StatusAlreadyGone));
        await db.SaveChangesAsync();

        var svc = new CommentRollupService(db);
        await svc.RollupDayAsync(day);

        var row = await db.CommentDailyStats.SingleAsync(x => x.Date == day && x.MappingId == mappingId);
        Assert.Equal("Chan", row.ChannelTitle);
        Assert.Equal(3, row.Forwarded);   // C1, C2, R1 (C3 is next day)
        Assert.Equal(1, row.Replies);     // R1
        Assert.Equal(20, row.SumLikes);   // 5 + 15 + 0
        Assert.Equal(3, row.EnrichedCount);
        Assert.Equal(2, row.Removed);
        Assert.Equal(1, row.Rejected);
        Assert.Equal(1, row.AlreadyGone);

        // Re-roll → still exactly one row, same numbers (no double count).
        await svc.RollupDayAsync(day);
        var again = await db.CommentDailyStats.Where(x => x.Date == day).ToListAsync();
        Assert.Single(again);
        Assert.Equal(3, again[0].Forwarded);
    }

    [Fact]
    public async Task Rollup_replaces_a_day_that_became_empty()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db, "Chan");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-2);

        // Pre-seed a stale rollup row, then roll a day with no raw activity → the stale row is cleared.
        db.CommentDailyStats.Add(new CommentDailyStat { Date = day, MappingId = mappingId, ChannelTitle = "Old", Forwarded = 9 });
        await db.SaveChangesAsync();

        await new CommentRollupService(db).RollupDayAsync(day);

        Assert.Empty(await db.CommentDailyStats.Where(x => x.Date == day).ToListAsync());
    }

    [Fact]
    public async Task History_reads_rollup_zero_filled_and_excludes_today()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db, "Chan");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Rollup rows on day-1 and day-3; day-2 absent (must 0-fill); a row "today" must be excluded.
        db.CommentDailyStats.AddRange(
            new CommentDailyStat { Date = today.AddDays(-1), MappingId = mappingId, ChannelTitle = "Chan", Forwarded = 4, Replies = 1, Removed = 2 },
            new CommentDailyStat { Date = today.AddDays(-3), MappingId = mappingId, ChannelTitle = "Chan", Forwarded = 7, Replies = 0, Removed = 1 },
            new CommentDailyStat { Date = today, MappingId = mappingId, ChannelTitle = "Chan", Forwarded = 99 });
        await db.SaveChangesAsync();

        var svc = new CommentsStatsService(db, Options.Create(new YouTubeCommentsOptions()));
        var result = await svc.GetHistoryAsync(7);

        Assert.Equal(7, result.Days);
        Assert.Equal(7, result.Points.Count);                 // from today-7 .. today-1
        Assert.DoesNotContain(result.Points, p => p.Date == today); // today excluded
        Assert.Equal(today.AddDays(-1), result.Points[^1].Date);    // ends at yesterday

        Assert.Equal(4, result.Points.Single(p => p.Date == today.AddDays(-1)).Forwarded);
        Assert.Equal(7, result.Points.Single(p => p.Date == today.AddDays(-3)).Forwarded);
        Assert.Equal(0, result.Points.Single(p => p.Date == today.AddDays(-2)).Forwarded); // 0-filled
        Assert.DoesNotContain(result.Points, p => p.Forwarded == 99); // today's 99 not included
    }

    [Theory]
    [InlineData(null, 90)]
    [InlineData(10, 10)]
    [InlineData(0, 1)]
    [InlineData(9999, 365)]
    public void ParseHistoryDays_clamps(int? input, int expected) =>
        Assert.Equal(expected, CommentsStatsService.ParseHistoryDays(input));

    // ── seed helpers ──

    private static async Task<Guid> SeedMappingAsync(YouTubeCommentsDbContext db, string channelTitle)
    {
        var yt = new YouTubeChannel { YouTubeChannelId = $"UC{Guid.NewGuid():N}"[..24], Title = channelTitle };
        var ch = new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = $"C{Guid.NewGuid():N}"[..10], Name = "general" };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(ch);
        var mapping = new ChannelMapping { YouTubeChannelId = yt.Id, SlackChannelId = ch.Id, IsActive = true };
        db.ChannelMappings.Add(mapping);
        await db.SaveChangesAsync();
        return mapping.Id;
    }

    private static void AddProcessed(
        YouTubeCommentsDbContext db, Guid mappingId, string commentId, DateTimeOffset at,
        string? parentId = null, long? likeCount = null) =>
        db.ProcessedComments.Add(new ProcessedComment
        {
            MappingId = mappingId,
            CommentId = commentId,
            VideoId = "V1",
            ProcessedAt = at,
            ParentCommentId = parentId,
            LikeCount = likeCount,
        });

    private static CommentModeration Moderation(Guid mappingId, string commentId, DateTimeOffset at, string status) =>
        new() { MappingId = mappingId, CommentId = commentId, Status = status, CreatedAt = at };
}
