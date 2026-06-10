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
        var root = Serialize(new DashboardStatsDto(1, 2, 3, 4, 5, 6, 7.5, 8, 9, 10, 11));
        AssertHasAll(root,
            "activeMappings", "totalMappings", "commentsToday", "commentsLast24h", "totalQuotaLimit",
            "totalQuotaUsedToday", "quotaUsedPercent", "errorsLast24h", "connectedWorkspaces",
            "apiKeyCount", "channelCount");
    }

    [Fact]
    public void CommentsTimelinePoint_exposes_bucket_and_numeric_count()
    {
        var root = Serialize(new CommentsTimelinePoint(DateTimeOffset.UnixEpoch, 7));
        AssertHasAll(root, "bucket", "count");
        Assert.Equal(7, root.GetProperty("count").GetInt32());
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
