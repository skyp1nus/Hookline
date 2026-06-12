using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Pins the fix for "can't create a Comments mapping despite a connected Slack workspace": Slack is
/// connected through the SHARED Connections area, which never touches this module's channel cache, so the
/// mapping picker stayed empty. <see cref="SlackChannelService.RefreshAllChannelsAsync"/> back-fills the
/// cache from every active workspace's shared bot token, and one workspace's failure can't abort the rest.
/// </summary>
public class SlackChannelRefreshTests
{
    private static SlackChannelService Build(YouTubeCommentsDbContext db, FakeSlackConnections conns, ISlackClient slack) =>
        new(db, slack, conns, new RecordingCommentsAudit(), NullLogger<SlackChannelService>.Instance);

    [Fact]
    public async Task RefreshAll_fills_the_picker_cache_for_active_workspaces_only()
    {
        using var db = TestDb.Create();
        var conns = new FakeSlackConnections();
        var active1 = conns.Seed("Acme", active: true);
        var active2 = conns.Seed("Globex", active: true);
        var inactive = conns.Seed("Defunct", active: false);

        var slack = new FakeSlack(
            new SlackChannelInfo("C1", "general", IsPrivate: false),
            new SlackChannelInfo("C2", "random", IsPrivate: true));

        var all = await Build(db, conns, slack).RefreshAllChannelsAsync();

        // Both active workspaces synced (2 channels each); the inactive one was skipped entirely.
        Assert.Equal(4, all.Length);
        Assert.Equal(2, await db.SlackChannels.CountAsync(c => c.WorkspaceId == active1));
        Assert.Equal(2, await db.SlackChannels.CountAsync(c => c.WorkspaceId == active2));
        Assert.Equal(0, await db.SlackChannels.CountAsync(c => c.WorkspaceId == inactive));
    }

    [Fact]
    public async Task RefreshAll_swallows_a_workspace_sync_failure_and_returns_what_synced()
    {
        using var db = TestDb.Create();
        var conns = new FakeSlackConnections();
        conns.Seed("Acme", active: true);

        // A workspace whose Slack listing throws (revoked token, outage) must not bubble out of the refresh.
        var result = await Build(db, conns, new ThrowingSlack()).RefreshAllChannelsAsync();

        Assert.Empty(result);
        Assert.Equal(0, await db.SlackChannels.CountAsync());
    }

    private sealed class FakeSlack(params SlackChannelInfo[] channels) : ISlackClient
    {
        public Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SlackChannelInfo>>(channels);
        public Task<SlackPostResult> PostCommentAsync(string botToken, string channelId, CommentNotification comment,
            string? threadTs = null, Guid? mappingId = null, bool canModerate = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingSlack : ISlackClient
    {
        public Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default) =>
            throw new InvalidOperationException("slack down");
        public Task<SlackPostResult> PostCommentAsync(string botToken, string channelId, CommentNotification comment,
            string? threadTs = null, Guid? mappingId = null, bool canModerate = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
