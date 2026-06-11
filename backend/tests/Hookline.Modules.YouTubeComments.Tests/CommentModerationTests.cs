using System.Net;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;

using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The "Reject on YouTube" action: resolves a force-ssl credential for the comment's owning channel via
/// the shared <see cref="IGoogleChannelCredentials"/> contract (never a reference to the Uploads module),
/// calls <c>setModerationStatus = rejected</c>, and records a durable idempotency row + an honest audit
/// entry. Every terminal path — success, already-done, already-gone (404), forbidden (403), quota, no
/// scope, provider-absent — maps to a distinct outcome rather than a silent failure.
/// </summary>
public class CommentModerationTests
{
    private const string Channel = "UC_test_channel_000000";

    private sealed record Harness(
        CommentModerationService Service,
        Guid MappingId,
        FakeYouTubeModerationClient Moderation,
        RecordingCommentsAudit Audit,
        YouTubeCommentsDbContext Db);

    private static Harness Build(bool seedCredential = true, bool hasProvider = true, string channelId = Channel)
    {
        var db = TestDb.Create();
        var yt = new YouTubeChannel { YouTubeChannelId = channelId, Title = "Chan" };
        var sc = new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = "C1", Name = "general" };
        var mapping = new ChannelMapping
        {
            YouTubeChannelId = yt.Id,
            SlackChannelId = sc.Id,
            YouTubeChannel = yt,
            SlackChannel = sc,
        };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(sc);
        db.ChannelMappings.Add(mapping);
        db.SaveChanges();

        var creds = new FakeGoogleChannelCredentials();
        if (seedCredential) creds.Seed(channelId);
        var providers = hasProvider ? new IGoogleChannelCredentials[] { creds } : Array.Empty<IGoogleChannelCredentials>();

        var moderation = new FakeYouTubeModerationClient();
        var audit = new RecordingCommentsAudit();
        var google = new FakeGoogleConnections();

        var service = new CommentModerationService(
            db, providers, google, moderation, audit, NullLogger<CommentModerationService>.Instance);

        return new Harness(service, mapping.Id, moderation, audit, db);
    }

    private static SlackActor Actor() => new("U123", "ada");

    [Fact]
    public async Task Reject_success_calls_youtube_records_row_and_audits()
    {
        var h = Build();

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.Rejected, result.Outcome);
        Assert.True(result.CardShouldShowRejected);
        Assert.Equal(1, h.Moderation.Calls);
        Assert.Equal("CMT1", h.Moderation.LastCommentId);
        Assert.Equal("ya29.test-access", h.Moderation.LastAccessToken);

        var row = Assert.Single(h.Db.CommentModerations);
        Assert.Equal("CMT1", row.CommentId);
        Assert.Equal(CommentModeration.StatusRejected, row.Status);
        Assert.Equal("U123", row.SlackUserId);
        Assert.Equal("ada", row.SlackUserName);

        var entry = Assert.Single(h.Audit.Entries, e => e.Category == "Moderation" && e.Message == "Comment rejected on YouTube");
        Assert.Equal(AuditLevel.Information, entry.Level);
        Assert.Equal("Comment", entry.EntityType);
        Assert.Equal("CMT1", entry.EntityId);
        Assert.Contains("U123", entry.Details);
    }

    [Fact]
    public async Task Already_moderated_short_circuits_without_calling_youtube()
    {
        var h = Build();
        h.Db.CommentModerations.Add(new CommentModeration
        {
            MappingId = h.MappingId,
            CommentId = "CMT1",
            SlackUserName = "bob",
            Status = CommentModeration.StatusRejected,
        });
        await h.Db.SaveChangesAsync();

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.AlreadyDone, result.Outcome);
        Assert.Equal(0, h.Moderation.Calls);
        Assert.Contains("bob", result.Message);
    }

    [Fact]
    public async Task Comment_already_gone_404_is_treated_as_removed_and_recorded()
    {
        var h = Build();
        h.Moderation.Throw = () => FakeYouTubeModerationClient.Api(HttpStatusCode.NotFound, "commentNotFound");

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.AlreadyGoneOnYouTube, result.Outcome);
        Assert.True(result.CardShouldShowRejected);
        var row = Assert.Single(h.Db.CommentModerations);
        Assert.Equal(CommentModeration.StatusAlreadyGone, row.Status);
    }

    [Fact]
    public async Task Forbidden_403_is_honest_error_with_no_row()
    {
        var h = Build();
        h.Moderation.Throw = () => FakeYouTubeModerationClient.Api(HttpStatusCode.Forbidden);

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.Forbidden, result.Outcome);
        Assert.False(result.CardShouldShowRejected);
        Assert.Empty(h.Db.CommentModerations);
    }

    [Fact]
    public async Task Quota_exceeded_is_honest_error_with_no_row()
    {
        var h = Build();
        h.Moderation.Throw = () => FakeYouTubeModerationClient.Api(HttpStatusCode.Forbidden, "quotaExceeded");

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.QuotaExceeded, result.Outcome);
        Assert.Empty(h.Db.CommentModerations);
    }

    [Fact]
    public async Task No_connected_force_ssl_account_returns_not_connected()
    {
        var h = Build(seedCredential: false);

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.NotConnected, result.Outcome);
        Assert.Equal(0, h.Moderation.Calls);
        Assert.Contains("Connections", result.Message);
    }

    [Fact]
    public async Task Credentials_provider_absent_degrades_honestly()
    {
        var h = Build(hasProvider: false);

        var result = await h.Service.RejectAsync(h.MappingId, "CMT1", Actor());

        Assert.Equal(ModerationOutcome.NotConnected, result.Outcome);
        Assert.Equal(0, h.Moderation.Calls);
    }

    [Fact]
    public async Task Unknown_mapping_fails_honestly()
    {
        var h = Build();

        var result = await h.Service.RejectAsync(Guid.NewGuid(), "CMT1", Actor());

        Assert.Equal(ModerationOutcome.Failed, result.Outcome);
        Assert.Equal(0, h.Moderation.Calls);
    }

    [Theory]
    [InlineData("https://www.googleapis.com/auth/youtube.force-ssl https://www.googleapis.com/auth/youtube.upload", true)]
    [InlineData("https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly", false)]
    public async Task CanModerate_reads_scope_snapshot_for_the_owning_channel(string scopes, bool expected)
    {
        var db = TestDb.Create();
        var google = new FakeGoogleConnections();
        google.Seed(Channel, scopes);
        var creds = new FakeGoogleChannelCredentials();
        var service = new CommentModerationService(
            db, new IGoogleChannelCredentials[] { creds }, google,
            new FakeYouTubeModerationClient(), new RecordingCommentsAudit(), NullLogger<CommentModerationService>.Instance);

        Assert.Equal(expected, await service.CanModerateAsync(Channel));
    }

    [Fact]
    public async Task CanModerate_false_for_a_different_channel()
    {
        var db = TestDb.Create();
        var google = new FakeGoogleConnections();
        google.Seed("UC_other", GoogleScopes.YouTubeForceSsl);
        var service = new CommentModerationService(
            db, new IGoogleChannelCredentials[] { new FakeGoogleChannelCredentials() }, google,
            new FakeYouTubeModerationClient(), new RecordingCommentsAudit(), NullLogger<CommentModerationService>.Instance);

        Assert.False(await service.CanModerateAsync(Channel));
    }
}
