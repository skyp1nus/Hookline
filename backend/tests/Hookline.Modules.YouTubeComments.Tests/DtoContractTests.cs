using System.Text.Json;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Locks the read-path JSON contract the frontend types depend on (web/src/features/comments/types.ts):
/// camelCase property names and enums serialized as their NUMERIC value (the host has no
/// JsonStringEnumConverter). A DTO rename/reshape now fails here instead of silently blanking the UI.
/// </summary>
public class DtoContractTests
{
    // Mirrors the host's serializer (minimal APIs use Microsoft.AspNetCore.Http.Json defaults = Web).
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Web);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static void AssertHasAll(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
            Assert.True(root.TryGetProperty(key, out _), $"expected JSON property '{key}'");
    }

    [Fact]
    public void DashboardStats_uses_the_expected_camelCase_keys()
    {
        var root = Serialize(new DashboardStatsDto(1, 2, 3, 4, 5, 6, 7.5, 8, 9, 10));
        AssertHasAll(root,
            "activeMappings", "totalMappings", "commentsToday", "commentsLast24h", "quotaCeiling",
            "estimatedDailyUnits", "estimatedPercent", "errorsLast24h", "connectedWorkspaces",
            "channelCount");
    }

    [Fact]
    public void ActivityPoint_exposes_bucket_and_split_counts()
    {
        var root = Serialize(new ActivityPoint(DateTimeOffset.UnixEpoch, Forwarded: 7, Replies: 2, Removed: 1));
        AssertHasAll(root, "bucket", "forwarded", "replies", "removed");
        Assert.Equal(7, root.GetProperty("forwarded").GetInt32());
        Assert.Equal(2, root.GetProperty("replies").GetInt32());
        Assert.Equal(1, root.GetProperty("removed").GetInt32());
    }

    [Fact]
    public void ActivityTimeline_exposes_range_and_points()
    {
        var root = Serialize(new ActivityTimelineDto("24h", [new ActivityPoint(DateTimeOffset.UnixEpoch, 1, 0, 0)]));
        AssertHasAll(root, "range", "points");
        Assert.Equal("24h", root.GetProperty("range").GetString());
    }

    [Fact]
    public void ModerationStats_exposes_outcome_split_and_moderators()
    {
        var root = Serialize(new ModerationStatsDto(
            TotalRemoved: 10, Rejected: 7, AlreadyGone: 3, Removed24h: 1, Removed7d: 4, Removed30d: 9,
            PerModerator: [new ModeratorStat("@u", "U1", 1, 2, 3, 4)]));
        AssertHasAll(root,
            "totalRemoved", "rejected", "alreadyGone", "removed24h", "removed7d", "removed30d", "perModerator");
        var mod = root.GetProperty("perModerator")[0];
        AssertHasAll(mod, "name", "slackUserId", "removed24h", "removed7d", "removed30d", "removedAllTime");
    }

    [Fact]
    public void CommentsHealth_exposes_delivery_and_mappings()
    {
        var root = Serialize(new CommentsHealthDto(
            new DeliveryHealthDto(Pending: 5, Failing: 2, NearGiveUp: 1, OldestPendingAt: DateTimeOffset.UnixEpoch),
            [new MappingHealthDto(Guid.NewGuid(), "Channel", "#slack", true, 15, DateTimeOffset.UnixEpoch, null, 3)]));
        AssertHasAll(root, "delivery", "mappings");
        AssertHasAll(root.GetProperty("delivery"), "pending", "failing", "nearGiveUp", "oldestPendingAt");
        AssertHasAll(root.GetProperty("mappings")[0],
            "mappingId", "channelTitle", "slackChannelName", "isActive", "frequencyMinutes",
            "lastPolledAt", "lastError", "forwarded24h");
    }

    [Fact]
    public void Engagement_exposes_summary_videos_and_authors()
    {
        var root = Serialize(new EngagementDto(
            new EngagementSummary(Captured: 12, AvgLikes: 3.5, AvgCommentLength: 80.2, AvgForwardLatencySeconds: 42),
            PerVideo: [new VideoStat("V1", "Title", 9, 30)],
            TopAuthors: [new AuthorStat("@ann", "https://youtube.com/@ann", 5, 11)]));
        AssertHasAll(root, "summary", "perVideo", "topAuthors");
        AssertHasAll(root.GetProperty("summary"), "captured", "avgLikes", "avgCommentLength", "avgForwardLatencySeconds");
        AssertHasAll(root.GetProperty("perVideo")[0], "videoId", "videoTitle", "forwarded", "likes");
        AssertHasAll(root.GetProperty("topAuthors")[0], "name", "authorChannelUrl", "forwarded", "likes");
    }

    [Fact]
    public void History_exposes_days_and_dated_points()
    {
        var root = Serialize(new HistoryDto(90, [new HistoryPoint(new DateOnly(2026, 6, 17), 4, 1, 2)]));
        AssertHasAll(root, "days", "points");
        var pt = root.GetProperty("points")[0];
        AssertHasAll(pt, "date", "forwarded", "replies", "removed");
        // DateOnly serializes as an ISO date string the frontend parses.
        Assert.Equal("2026-06-17", pt.GetProperty("date").GetString());
    }

    [Fact]
    public void CommentsOverview_exposes_reply_split_and_alltime_removed()
    {
        var win = new CommentsWindowCounts(Forwarded: 10, Replies: 3, Removed: 2);
        var root = Serialize(new CommentsOverviewDto(
            TotalForwarded: 100, TotalReplies: 30, TotalRemoved: 12,
            Window24h: win, Window7d: win, Window30d: win,
            PerChannel: [], Quota: new CommentsQuotaDto(1, 2, 3)));
        AssertHasAll(root, "totalForwarded", "totalReplies", "totalRemoved", "window24h", "window7d", "window30d");
        AssertHasAll(root.GetProperty("window24h"), "forwarded", "replies", "removed");
    }

    [Fact]
    public void YouTubeChannelDto_uses_the_expected_camelCase_keys()
    {
        var root = Serialize(new YouTubeChannelDto(
            Guid.NewGuid(), "UC123", "Title", null, "@handle", DateTimeOffset.UnixEpoch, 3));
        AssertHasAll(root,
            "id", "youTubeChannelId", "title", "thumbnailUrl", "handle", "addedAt", "mappingCount");
    }

    [Fact]
    public void MappingDto_uses_camelCase_keys_and_serializes_enums_as_numbers()
    {
        var root = Serialize(new MappingDto(
            Guid.NewGuid(), Guid.NewGuid(), "Channel", null, Guid.NewGuid(), "#slack", "Workspace",
            PollingFrequency.FiveMinutes, true, false, ReplyScanFrequency.Daily, 30,
            null, null, DateTimeOffset.UnixEpoch));

        AssertHasAll(root,
            "id", "youTubeChannelId", "youTubeChannelTitle", "youTubeChannelThumbnailUrl",
            "slackChannelId", "slackChannelName", "slackWorkspaceName", "frequency", "isActive",
            "includeReplies", "replySweepFrequency", "replyWindowDays", "lastPolledAt", "lastError",
            "createdAt");

        // The frontend maps these numeric enum values to labels (pollingFrequencyLabel) — they must
        // serialize as numbers, not names.
        Assert.Equal(5, root.GetProperty("frequency").GetInt32());
        Assert.Equal(1440, root.GetProperty("replySweepFrequency").GetInt32());
    }
}
