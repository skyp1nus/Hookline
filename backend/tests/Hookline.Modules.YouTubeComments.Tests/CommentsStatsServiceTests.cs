using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Covers the detailed-dashboard aggregates: the activity timeline's range/bucketing + reply split, the
/// moderation outcome split + per-moderator leaderboard, and the delivery/mapping health panel. All run
/// against the in-memory EF provider (<see cref="TestDb"/>).
/// </summary>
public class CommentsStatsServiceTests
{
    private static CommentsStatsService Svc(YouTubeCommentsDbContext db) =>
        new(db, Options.Create(new YouTubeCommentsOptions()));

    // ── range parsing ──

    [Theory]
    [InlineData("24h", StatsRange.Day)]
    [InlineData(null, StatsRange.Day)]
    [InlineData("garbage", StatsRange.Day)]
    [InlineData("7d", StatsRange.Week)]
    [InlineData("week", StatsRange.Week)]
    [InlineData("30d", StatsRange.Month)]
    [InlineData("MONTH", StatsRange.Month)]
    public void ParseRange_maps_query_values(string? value, StatsRange expected) =>
        Assert.Equal(expected, CommentsStatsService.ParseRange(value));

    // ── activity timeline ──

    [Fact]
    public async Task Activity_day_has_24_buckets_and_splits_replies_and_drops_out_of_window()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db);
        var now = DateTimeOffset.UtcNow;

        AddProcessed(db, mappingId, "C1", now.AddHours(-1));                       // top-level
        AddProcessed(db, mappingId, "C2", now.AddHours(-2));                       // top-level
        AddProcessed(db, mappingId, "R1", now.AddMinutes(-30), parentId: "C1");    // reply
        AddProcessed(db, mappingId, "OLD", now.AddHours(-30));                     // out of 24h window
        db.CommentModerations.Add(Moderation(mappingId, "C2", now.AddMinutes(-10)));
        await db.SaveChangesAsync();

        var result = await Svc(db).GetActivityAsync(StatsRange.Day);

        Assert.Equal("24h", result.Range);
        Assert.Equal(24, result.Points.Count);
        Assert.Equal(3, result.Points.Sum(p => p.Forwarded)); // OLD excluded
        Assert.Equal(1, result.Points.Sum(p => p.Replies));
        Assert.Equal(1, result.Points.Sum(p => p.Removed));
    }

    [Fact]
    public async Task Activity_week_has_7_daily_buckets()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db);
        var now = DateTimeOffset.UtcNow;

        AddProcessed(db, mappingId, "A", now.AddHours(-1));
        AddProcessed(db, mappingId, "B", now.AddDays(-3));
        AddProcessed(db, mappingId, "OLD", now.AddDays(-10)); // out of 7d window
        await db.SaveChangesAsync();

        var result = await Svc(db).GetActivityAsync(StatsRange.Week);

        Assert.Equal("7d", result.Range);
        Assert.Equal(7, result.Points.Count);
        Assert.Equal(2, result.Points.Sum(p => p.Forwarded));
    }

    [Fact]
    public async Task Activity_month_has_30_daily_buckets()
    {
        using var db = TestDb.Create();
        var result = await Svc(db).GetActivityAsync(StatsRange.Month);
        Assert.Equal("30d", result.Range);
        Assert.Equal(30, result.Points.Count);
        Assert.All(result.Points, p => Assert.Equal(0, p.Forwarded));
    }

    // ── moderation breakdown ──

    [Fact]
    public async Task Moderation_splits_outcomes_and_ranks_moderators()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db);
        var now = DateTimeOffset.UtcNow;

        db.CommentModerations.AddRange(
            Moderation(mappingId, "C1", now.AddHours(-1), "U1", "ann", CommentModeration.StatusRejected),
            Moderation(mappingId, "C2", now.AddDays(-2), "U1", "ann", CommentModeration.StatusRejected),
            Moderation(mappingId, "C3", now.AddHours(-2), "U2", "bob", CommentModeration.StatusRejected),
            Moderation(mappingId, "C4", now.AddHours(-3), "U3", "cara", CommentModeration.StatusAlreadyGone));
        await db.SaveChangesAsync();

        var result = await Svc(db).GetModerationAsync();

        Assert.Equal(4, result.TotalRemoved);
        Assert.Equal(3, result.Rejected);
        Assert.Equal(1, result.AlreadyGone);
        Assert.Equal(3, result.Removed24h); // C2 is 2 days ago
        Assert.Equal(4, result.Removed7d);

        Assert.Equal(3, result.PerModerator.Count);
        var top = result.PerModerator[0];
        Assert.Equal("ann", top.Name);
        Assert.Equal("U1", top.SlackUserId);
        Assert.Equal(2, top.RemovedAllTime);
        Assert.Equal(1, top.Removed24h); // only one of ann's two is within 24h
    }

    // ── health panel ──

    [Fact]
    public async Task Health_reports_queue_backlog_and_orders_errored_mappings_first()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;

        var healthy = await SeedMappingAsync(db, channelTitle: "Healthy", lastPolledAt: now.AddMinutes(-5));
        var errored = await SeedMappingAsync(db, channelTitle: "Errored", lastPolledAt: now.AddMinutes(-1), lastError: "boom");

        AddProcessed(db, healthy, "H1", now.AddHours(-1));
        AddProcessed(db, healthy, "H2", now.AddHours(-2));

        // MaxAttempts default = 8 → nearThreshold = 7.
        db.PendingDeliveries.AddRange(
            Pending(healthy, "P0", attempts: 0, createdAt: now.AddMinutes(-50)),
            Pending(healthy, "P1", attempts: 1, createdAt: now.AddMinutes(-30)),
            Pending(errored, "P7", attempts: 7, createdAt: now.AddMinutes(-10)));
        await db.SaveChangesAsync();

        var result = await Svc(db).GetHealthAsync();

        Assert.Equal(3, result.Delivery.Pending);
        Assert.Equal(2, result.Delivery.Failing);     // attempts > 0
        Assert.Equal(1, result.Delivery.NearGiveUp);  // attempts >= 7
        Assert.Equal(now.AddMinutes(-50), result.Delivery.OldestPendingAt);

        Assert.Equal(2, result.Mappings.Count);
        Assert.Equal("Errored", result.Mappings[0].ChannelTitle); // errored sorts first
        Assert.Equal("boom", result.Mappings[0].LastError);

        var healthyRow = result.Mappings.Single(m => m.ChannelTitle == "Healthy");
        Assert.Equal(2, healthyRow.Forwarded24h);
        Assert.Equal(15, healthyRow.FrequencyMinutes);
    }

    // ── engagement ──

    [Fact]
    public async Task Engagement_counts_all_forwarded_per_video_but_averages_only_enriched()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db);
        var now = DateTimeOffset.UtcNow;

        // Enriched rows (post-capture): like counts, author, published-at.
        AddProcessed(db, mappingId, "C1", now.AddHours(-1), videoId: "V1", videoTitle: "Vlog",
            authorName: "Ann", authorChannelUrl: "url/ann", likeCount: 10, publishedAt: now.AddHours(-2), commentLength: 100);
        AddProcessed(db, mappingId, "C2", now.AddHours(-1), videoId: "V1", videoTitle: "Vlog",
            authorName: "Ann", authorChannelUrl: "url/ann", likeCount: 20, publishedAt: now.AddHours(-3), commentLength: 200);
        AddProcessed(db, mappingId, "C3", now.AddHours(-1), videoId: "V2", videoTitle: "Short",
            authorName: "Bob", authorChannelUrl: "url/bob", likeCount: 30, publishedAt: now.AddHours(-1), commentLength: 60);
        // Legacy row (pre-capture): no engagement snapshot — counts for per-video forwarded, excluded from averages.
        AddProcessed(db, mappingId, "OLD", now.AddHours(-1), videoId: "V1");
        await db.SaveChangesAsync();

        var result = await Svc(db).GetEngagementAsync();

        // Captured = the 3 enriched rows (OLD excluded).
        Assert.Equal(3, result.Summary.Captured);
        Assert.Equal(20, result.Summary.AvgLikes); // (10+20+30)/3
        Assert.Equal(120, result.Summary.AvgCommentLength); // (100+200+60)/3
        Assert.NotNull(result.Summary.AvgForwardLatencySeconds);

        // Per-video forwarded counts ALL rows (V1 has 3 incl. legacy); likes sum the enriched.
        var v1 = result.PerVideo.Single(v => v.VideoId == "V1");
        Assert.Equal(3, v1.Forwarded);
        Assert.Equal("Vlog", v1.VideoTitle);
        Assert.Equal(30, v1.Likes); // 10 + 20 (legacy has no like_count)

        // Authors: only enriched rows; Ann leads with 2.
        Assert.Equal(2, result.TopAuthors.Count);
        Assert.Equal("Ann", result.TopAuthors[0].Name);
        Assert.Equal(2, result.TopAuthors[0].Forwarded);
        Assert.Equal(30, result.TopAuthors[0].Likes);
    }

    [Fact]
    public async Task Engagement_excludes_partially_populated_rows_from_enriched_metrics()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db);
        var now = DateTimeOffset.UtcNow;

        // Fully enriched (LikeCount set) — the canonical enriched flag.
        AddProcessed(db, mappingId, "E1", now.AddHours(-1), authorName: "Ann", authorChannelUrl: "url/ann",
            likeCount: 10, publishedAt: now.AddHours(-2), commentLength: 100);
        // Partially populated: author/length/published present but LikeCount NULL → must be excluded from
        // Captured, authors, avgLength and latency (it is NOT engagement-enriched by the canonical flag).
        AddProcessed(db, mappingId, "P1", now.AddHours(-1), authorName: "Bob", authorChannelUrl: "url/bob",
            likeCount: null, publishedAt: now.AddHours(-5), commentLength: 9999);
        await db.SaveChangesAsync();

        var result = await Svc(db).GetEngagementAsync();

        Assert.Equal(1, result.Summary.Captured);          // only E1
        Assert.Equal(10, result.Summary.AvgLikes);
        Assert.Equal(100, result.Summary.AvgCommentLength); // 9999 excluded
        Assert.Single(result.TopAuthors);                   // Bob (no LikeCount) excluded
        Assert.Equal("Ann", result.TopAuthors[0].Name);
    }

    // ── seed helpers ──

    private static async Task<Guid> SeedMappingAsync(
        YouTubeCommentsDbContext db,
        string channelTitle = "Chan",
        DateTimeOffset? lastPolledAt = null,
        string? lastError = null)
    {
        var yt = new YouTubeChannel { YouTubeChannelId = $"UC{Guid.NewGuid():N}"[..24], Title = channelTitle };
        var ch = new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = $"C{Guid.NewGuid():N}"[..10], Name = "general" };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(ch);
        var mapping = new ChannelMapping
        {
            YouTubeChannelId = yt.Id,
            SlackChannelId = ch.Id,
            Frequency = PollingFrequency.FifteenMinutes,
            IsActive = true,
            LastPolledAt = lastPolledAt,
            LastError = lastError,
        };
        db.ChannelMappings.Add(mapping);
        await db.SaveChangesAsync();
        return mapping.Id;
    }

    private static void AddProcessed(
        YouTubeCommentsDbContext db, Guid mappingId, string commentId, DateTimeOffset at,
        string? parentId = null, string videoId = "V1", string? videoTitle = null,
        string? authorName = null, string? authorChannelUrl = null,
        long? likeCount = null, DateTimeOffset? publishedAt = null, int? commentLength = null) =>
        db.ProcessedComments.Add(new ProcessedComment
        {
            MappingId = mappingId,
            CommentId = commentId,
            VideoId = videoId,
            ProcessedAt = at,
            ParentCommentId = parentId,
            VideoTitle = videoTitle,
            AuthorName = authorName,
            AuthorChannelUrl = authorChannelUrl,
            LikeCount = likeCount,
            PublishedAt = publishedAt,
            CommentLength = commentLength,
        });

    private static CommentModeration Moderation(
        Guid mappingId, string commentId, DateTimeOffset at,
        string? userId = null, string? userName = null, string status = CommentModeration.StatusRejected) =>
        new()
        {
            MappingId = mappingId,
            CommentId = commentId,
            Status = status,
            SlackUserId = userId,
            SlackUserName = userName,
            CreatedAt = at,
        };

    private static PendingDelivery Pending(Guid mappingId, string commentId, int attempts, DateTimeOffset createdAt) =>
        new()
        {
            MappingId = mappingId,
            CommentId = commentId,
            VideoId = "V1",
            PayloadJson = "{}",
            AttemptCount = attempts,
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
        };
}
