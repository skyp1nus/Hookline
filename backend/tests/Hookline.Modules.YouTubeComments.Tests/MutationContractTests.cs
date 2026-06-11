using System.Text.Json;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Locks the WRITE-path JSON contract: every mutation request the frontend sends and every response it
/// reads must serialize with the host's Web serializer (camelCase keys, enums as their underlying NUMERIC
/// value — no JsonStringEnumConverter). The buttons call these shapes over the wire, so a DTO rename or an
/// enum-as-string regression fails here instead of silently breaking add/edit/toggle/delete in the UI.
/// Mirrors the read-path approach in <see cref="DtoContractTests"/>.
/// </summary>
public class MutationContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialize<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Web)).RootElement.Clone();

    private static void AssertHasAll(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
            Assert.True(root.TryGetProperty(key, out _), $"expected JSON property '{key}'");
    }

    // ── API keys ──

    [Fact]
    public void CreateApiKeyRequest_uses_camelCase_keys()
    {
        var root = Serialize(new CreateApiKeyRequest("Primary", "AIzaSyExampleKey"));
        AssertHasAll(root, "name", "apiKey");
        Assert.Equal("AIzaSyExampleKey", root.GetProperty("apiKey").GetString());
    }

    [Fact]
    public void ApiKeyDto_uses_camelCase_keys_with_numeric_quota_fields()
    {
        var root = Serialize(new ApiKeyDto(
            Guid.NewGuid(), "Primary", "AIza…9999", 10000, true, DateTimeOffset.UnixEpoch, 1500, 8500));
        AssertHasAll(root,
            "id", "name", "keyHint", "dailyQuotaLimit", "isActive", "createdAt", "todayUnitsUsed", "remainingQuota");
        Assert.Equal(10000, root.GetProperty("dailyQuotaLimit").GetInt32());
        Assert.Equal(1500, root.GetProperty("todayUnitsUsed").GetInt32());
        Assert.Equal(8500, root.GetProperty("remainingQuota").GetInt32());
    }

    // ── channels ──

    [Fact]
    public void AddChannelRequest_uses_camelCase_keys()
    {
        var root = Serialize(new AddChannelRequest("@SomeHandle"));
        AssertHasAll(root, "input");
        Assert.Equal("@SomeHandle", root.GetProperty("input").GetString());
    }

    // ── mappings ──

    [Fact]
    public void CreateMappingRequest_uses_camelCase_keys_and_numeric_enums()
    {
        var root = Serialize(new CreateMappingRequest(
            Guid.NewGuid(), Guid.NewGuid(), PollingFrequency.FiveMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Daily, ReplyWindowDays: 30));

        AssertHasAll(root,
            "youTubeChannelId", "slackChannelId", "frequency", "includeReplies",
            "replySweepFrequency", "replyWindowDays");

        // Enums travel as numbers; the backend binds them straight back to the enum.
        Assert.Equal(5, root.GetProperty("frequency").GetInt32());
        Assert.Equal(1440, root.GetProperty("replySweepFrequency").GetInt32());
        Assert.True(root.GetProperty("includeReplies").GetBoolean());
        Assert.Equal(30, root.GetProperty("replyWindowDays").GetInt32());
    }

    [Fact]
    public void UpdateMappingRequest_uses_camelCase_keys_and_numeric_enums()
    {
        var root = Serialize(new UpdateMappingRequest(
            Frequency: PollingFrequency.OneHour, IsActive: false, IncludeReplies: true,
            ReplySweepFrequency: ReplyScanFrequency.Hourly, ReplyWindowDays: 14));

        AssertHasAll(root, "frequency", "isActive", "includeReplies", "replySweepFrequency", "replyWindowDays");
        Assert.Equal(60, root.GetProperty("frequency").GetInt32());
        Assert.Equal(60, root.GetProperty("replySweepFrequency").GetInt32());
        Assert.False(root.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public void MappingOptionsDto_and_its_options_use_camelCase_keys()
    {
        var root = Serialize(new MappingOptionsDto(
            new[] { new ChannelOption(Guid.NewGuid(), "My Channel") },
            new[] { new SlackChannelOption(Guid.NewGuid(), "general", "Acme", IsPrivate: false) }));

        AssertHasAll(root, "youTubeChannels", "slackChannels");

        var channel = root.GetProperty("youTubeChannels")[0];
        AssertHasAll(channel, "id", "title");

        var slack = root.GetProperty("slackChannels")[0];
        AssertHasAll(slack, "id", "name", "workspaceName", "isPrivate");
        Assert.False(slack.GetProperty("isPrivate").GetBoolean());
    }
}
