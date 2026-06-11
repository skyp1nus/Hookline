using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.Modules.YouTubeComments.Jobs;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Exactly-once delivery: a comment fetched by the poll is posted to Slack and recorded in
/// <c>processed_comments</c> once per mapping. A second poll over the same comments re-fetches them but
/// posts nothing new — the dedup ledger excludes anything already delivered. (Replies are gated by the
/// mapping's opt-in.)
/// </summary>
public class DedupTests
{
    [Fact]
    public async Task Second_poll_over_same_comments_posts_nothing_new()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db, includeReplies: false);

        var youtube = new FakeYouTube(
            new YouTubeComment("C1", "V1", "Ada", null, null, "hi", 0, DateTimeOffset.UtcNow),
            new YouTubeComment("C2", "V1", "Bo", null, null, "yo", 0, DateTimeOffset.UtcNow));
        var slack = new FakeSlack();
        var job = Job(db, youtube, slack);

        await job.RunAsync(mappingId, CancellationToken.None);
        Assert.Equal(2, slack.Posts);
        Assert.Equal(2, await db.ProcessedComments.CountAsync());

        // Same fetch again — every comment is already in the ledger.
        await job.RunAsync(mappingId, CancellationToken.None);
        Assert.Equal(2, slack.Posts); // no new posts
        Assert.Equal(2, await db.ProcessedComments.CountAsync()); // no new ledger rows
    }

    [Fact]
    public async Task Replies_are_dropped_when_the_mapping_opts_out()
    {
        using var db = TestDb.Create();
        var mappingId = await SeedMappingAsync(db, includeReplies: false);

        var youtube = new FakeYouTube(
            new YouTubeComment("C1", "V1", "Ada", null, null, "top", 0, DateTimeOffset.UtcNow),
            new YouTubeComment("R1", "V1", "Bo", null, null, "reply", 0, DateTimeOffset.UtcNow, IsReply: true, ParentCommentId: "C1"));
        var slack = new FakeSlack();

        await Job(db, youtube, slack).RunAsync(mappingId, CancellationToken.None);

        Assert.Equal(1, slack.Posts); // only the top-level comment
        Assert.Equal("C1", (await db.ProcessedComments.SingleAsync()).CommentId);
    }

    private static PollCommentsJob Job(YouTubeCommentsDbContext db, FakeYouTube youtube, FakeSlack slack) =>
        new(db, youtube, slack, new FakeKeyProvider(), new NullScheduler(), new FakeSlackConn(),
            new NullAudit(), NullLogger<PollCommentsJob>.Instance);

    private static async Task<Guid> SeedMappingAsync(YouTubeCommentsDbContext db, bool includeReplies)
    {
        var yt = new YouTubeChannel { YouTubeChannelId = "UCxxxxxxxxxxxxxxxxxxxxxx", Title = "Chan" };
        var ch = new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = "C123", Name = "general" };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(ch);
        var mapping = new ChannelMapping
        {
            YouTubeChannelId = yt.Id, SlackChannelId = ch.Id,
            Frequency = PollingFrequency.FifteenMinutes, IsActive = true,
            IncludeReplies = includeReplies,
            CommentsSinceUtc = DateTimeOffset.UtcNow.AddHours(-1), // watermark in the past
        };
        db.ChannelMappings.Add(mapping);
        await db.SaveChangesAsync();
        return mapping.Id;
    }

    // ── fakes ──
    private sealed class FakeYouTube(params YouTubeComment[] comments) : IYouTubeClient
    {
        public Task<(bool Ok, string? Error)> ValidateKeyAsync(string apiKey, CancellationToken ct = default) => Task.FromResult((true, (string?)null));
        public Task<ChannelLookupResult> GetChannelAsync(string apiKey, string input, CancellationToken ct = default) => Task.FromResult(new ChannelLookupResult(null, 1));
        public Task<CommentFetchResult> GetRecentCommentsAsync(string apiKey, string channelId, int maxResults = 50, CancellationToken ct = default) =>
            Task.FromResult(new CommentFetchResult(comments, 1));
        public Task<VideoTitlesResult> GetVideoTitlesAsync(string apiKey, IEnumerable<string> videoIds, CancellationToken ct = default) =>
            Task.FromResult(new VideoTitlesResult(videoIds.Distinct().ToDictionary(v => v, v => $"Title {v}"), 1));
        public Task<ThreadFetchResult> GetCommentThreadsSinceAsync(string apiKey, string channelId, DateTimeOffset since, int maxPages, CancellationToken ct = default) =>
            Task.FromResult(new ThreadFetchResult([], 0));
        public Task<RepliesResult> GetRepliesAsync(string apiKey, string parentCommentId, string parentVideoId, int maxPages, CancellationToken ct = default) =>
            Task.FromResult(new RepliesResult([], 0));
    }

    private sealed class FakeSlack : ISlackClient
    {
        public int Posts;
        public Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SlackChannelInfo>>([]);
        public Task<SlackPostResult> PostCommentAsync(string botToken, string channelId, CommentNotification comment, string? threadTs = null, Guid? mappingId = null, CancellationToken ct = default)
        {
            Posts++;
            return Task.FromResult(new SlackPostResult(SlackPostStatus.Posted, $"ts-{Posts}"));
        }
        public Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeKeyProvider : IYouTubeApiKeyProvider
    {
        public Task<ApiKeyLease?> AcquireAsync(int unitsNeeded = 1, CancellationToken ct = default) =>
            Task.FromResult<ApiKeyLease?>(new ApiKeyLease(Guid.NewGuid(), "k", "API", 9999));
        public Task RecordUsageAsync(Guid apiKeyId, int units, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkExhaustedAsync(Guid apiKeyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkInvalidAsync(Guid apiKeyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullScheduler : IPollingScheduler
    {
        public void Schedule(Guid mappingId, PollingFrequency frequency) { }
        public void Remove(Guid mappingId) { }
        public void ScheduleReplySweep(Guid mappingId, ReplyScanFrequency frequency) { }
        public void RemoveReplySweep(Guid mappingId) { }
        public Task SyncAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSlackConn : ISlackConnections
    {
        public Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) => Task.FromResult<string?>("xoxb-test");
        public Task<string?> GetBotTokenForTeamAsync(string teamId, CancellationToken ct = default) => Task.FromResult<string?>("xoxb-test");
        public Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SlackWorkspaceSummary>>([]);
        public Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, CancellationToken ct = default) => Task.FromResult<SlackWorkspaceSummary?>(null);
        public Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class NullAudit : ICommentsAudit
    {
        public Task LogAsync(string level, string category, string message, string? entityType = null, string? entityId = null, string? actor = null, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
