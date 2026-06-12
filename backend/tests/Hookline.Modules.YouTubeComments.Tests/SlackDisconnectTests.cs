using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Features;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Pins the disconnect contract: disconnecting a Slack workspace is a HARD removal, not a soft "Inactive"
/// leftover. <see cref="ISlackConnections.RemoveAsync"/> deletes the workspace row (and with it the encrypted
/// bot token) so <see cref="ISlackConnections.ListAsync"/> no longer returns it and
/// <see cref="ISlackConnections.GetBotTokenAsync"/> resolves to null; the published
/// <c>SlackWorkspaceDisconnected</c> event drives the Comments
/// <see cref="SlackWorkspaceDisconnectedHandler"/>, which drops every mapping for the workspace (cascading
/// the dedup/retry/moderation children) and the cached channels.
/// </summary>
public class SlackDisconnectTests
{
    /// <summary>
    /// In-memory <see cref="ISlackConnections"/> that models the REAL store + event-bus seam for disconnect:
    /// it holds workspaces with their bot tokens and, on <see cref="RemoveAsync"/>, deletes the row (token
    /// gone) and then runs the supplied disconnected-handler synchronously — exactly as the production
    /// in-process event bus fans the event out to module handlers after the row is deleted.
    /// </summary>
    private sealed class FakeSlackStore(Func<SlackWorkspaceDisconnected, Task>? onDisconnected = null) : ISlackConnections
    {
        private readonly Dictionary<Guid, (SlackWorkspaceSummary Summary, string Token)> _rows = new();

        public Guid Seed(string teamName, string token = "xoxb-secret", string app = "youtube-comments", bool active = true)
        {
            var id = Guid.NewGuid();
            _rows[id] = (new SlackWorkspaceSummary(id, $"T-{id:N}", teamName, app, active), token);
            return id;
        }

        public Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SlackWorkspaceSummary>>(_rows.Values.Select(r => r.Summary).ToList());

        public Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult(_rows.TryGetValue(workspaceId, out var r) ? r.Token : null);

        public Task<string?> GetBotTokenForTeamAsync(string teamId, string app, CancellationToken ct = default) =>
            Task.FromResult<string?>(
                _rows.Values.FirstOrDefault(r => r.Summary.TeamId == teamId && r.Summary.App == app).Token);

        public Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, string app, CancellationToken ct = default) =>
            Task.FromResult<SlackWorkspaceSummary?>(
                _rows.Values.Select(r => r.Summary).FirstOrDefault(s => s.TeamId == teamId && s.App == app));

        public Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default) =>
            Task.FromResult(Seed(write.TeamName, write.BotToken, write.App));

        public Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(workspaceId, out var r))
                return Task.FromResult(false);
            _rows[workspaceId] = (r.Summary with { IsActive = false }, r.Token);
            return Task.FromResult(true);
        }

        public async Task<bool> RemoveAsync(Guid workspaceId, CancellationToken ct = default)
        {
            if (!_rows.Remove(workspaceId)) // row + its bot token are gone
                return false;
            if (onDisconnected is not null)
                await onDisconnected(new SlackWorkspaceDisconnected(workspaceId));
            return true;
        }
    }

    /// <summary>Records scheduler removals so we can assert recurring jobs are torn down on disconnect.</summary>
    private sealed class RecordingScheduler : IPollingScheduler
    {
        public List<Guid> Removed { get; } = new();
        public List<Guid> ReplySweepsRemoved { get; } = new();

        public void Schedule(Guid mappingId, PollingFrequency frequency) { }
        public void Remove(Guid mappingId) => Removed.Add(mappingId);
        public void ScheduleReplySweep(Guid mappingId, ReplyScanFrequency frequency) { }
        public void RemoveReplySweep(Guid mappingId) => ReplySweepsRemoved.Add(mappingId);
        public Task SyncAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Seeds a YouTube channel, a cached Slack channel for <paramref name="workspaceId"/>, and an
    /// (in)active mapping plus one of each cascade-child row, leaving them tracked in <paramref name="db"/>.</summary>
    private static async Task<Guid> SeedMappingWithChildrenAsync(
        YouTubeCommentsDbContext db, Guid workspaceId, bool active)
    {
        // Unique within the 40-char column limit; the literal id matters only for FK shape here.
        var yt = new YouTubeChannel { YouTubeChannelId = $"UC{Guid.NewGuid():N}".Substring(0, 24), Title = "Chan" };
        var ch = new SlackChannel { WorkspaceId = workspaceId, SlackChannelId = $"C-{Guid.NewGuid():N}", Name = "general" };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(ch);

        var mapping = new ChannelMapping
        {
            YouTubeChannelId = yt.Id,
            SlackChannelId = ch.Id,
            Frequency = PollingFrequency.FifteenMinutes,
            IsActive = active,
        };
        db.ChannelMappings.Add(mapping);

        db.ProcessedComments.Add(new ProcessedComment { MappingId = mapping.Id, CommentId = "C1", VideoId = "V1" });
        db.PendingDeliveries.Add(new PendingDelivery { MappingId = mapping.Id, CommentId = "C2", VideoId = "V1", PayloadJson = "{}" });
        db.CommentModerations.Add(new CommentModeration { MappingId = mapping.Id, CommentId = "C3" });

        await db.SaveChangesAsync();
        return mapping.Id;
    }

    [Fact]
    public async Task Disconnect_hard_removes_the_workspace_so_it_no_longer_lists_and_its_token_is_gone()
    {
        using var db = TestDb.Create();
        var scheduler = new RecordingScheduler();
        var disconnects = new List<Guid>();

        var store = new FakeSlackStore(async ev =>
        {
            disconnects.Add(ev.WorkspaceId);
            await new SlackWorkspaceDisconnectedHandler(
                    db, scheduler, new RecordingCommentsAudit(),
                    NullLogger<SlackWorkspaceDisconnectedHandler>.Instance)
                .HandleAsync(ev);
        });
        var workspaceId = store.Seed("Acme", token: "xoxb-acme-secret");

        var service = new SlackChannelService(
            db, new NoopSlackClient(), store, new RecordingCommentsAudit(),
            NullLogger<SlackChannelService>.Instance);

        // Sanity: connected workspace lists and hands out a bot token.
        Assert.Single(await store.ListAsync());
        Assert.Equal("xoxb-acme-secret", await store.GetBotTokenAsync(workspaceId));

        var removed = await service.DeleteWorkspaceAsync(workspaceId);

        Assert.True(removed);
        Assert.Empty(await store.ListAsync());                       // no lingering "Inactive" row
        Assert.Null(await store.GetBotTokenAsync(workspaceId));      // encrypted token gone with the row
        Assert.Equal(new[] { workspaceId }, disconnects);           // event still fired exactly once
    }

    [Fact]
    public async Task Disconnecting_a_missing_workspace_returns_false_and_fires_no_event()
    {
        using var db = TestDb.Create();
        var disconnects = new List<Guid>();
        var store = new FakeSlackStore(ev => { disconnects.Add(ev.WorkspaceId); return Task.CompletedTask; });

        var service = new SlackChannelService(
            db, new NoopSlackClient(), store, new RecordingCommentsAudit(),
            NullLogger<SlackChannelService>.Instance);

        Assert.False(await service.DeleteWorkspaceAsync(Guid.NewGuid()));
        Assert.Empty(disconnects);
    }

    [Fact]
    public async Task Handler_drops_all_mappings_and_cached_channels_and_cascades_children()
    {
        using var db = TestDb.Create();
        var workspaceId = Guid.NewGuid();
        var otherWorkspaceId = Guid.NewGuid();

        // Two mappings on the disconnected workspace (one active, one already inactive) + cascade children,
        // plus a mapping on a DIFFERENT workspace that must survive untouched.
        var activeId = await SeedMappingWithChildrenAsync(db, workspaceId, active: true);
        var inactiveId = await SeedMappingWithChildrenAsync(db, workspaceId, active: false);
        var survivorId = await SeedMappingWithChildrenAsync(db, otherWorkspaceId, active: true);

        var scheduler = new RecordingScheduler();
        var handler = new SlackWorkspaceDisconnectedHandler(
            db, scheduler, new RecordingCommentsAudit(),
            NullLogger<SlackWorkspaceDisconnectedHandler>.Instance);

        await handler.HandleAsync(new SlackWorkspaceDisconnected(workspaceId));

        // Both of the workspace's mappings are gone (active filter dropped — inactive ones go too).
        Assert.Null(await db.ChannelMappings.FindAsync(activeId));
        Assert.Null(await db.ChannelMappings.FindAsync(inactiveId));
        // The unrelated workspace's mapping + children are untouched.
        Assert.NotNull(await db.ChannelMappings.FindAsync(survivorId));

        // Cached channels for the workspace are deleted; the other workspace's channel remains.
        Assert.Equal(0, await db.SlackChannels.CountAsync(c => c.WorkspaceId == workspaceId));
        Assert.Equal(1, await db.SlackChannels.CountAsync(c => c.WorkspaceId == otherWorkspaceId));

        // Cascade children of the removed mappings are gone; the survivor's one row of each remains.
        Assert.Equal(1, await db.ProcessedComments.CountAsync());
        Assert.Equal(1, await db.PendingDeliveries.CountAsync());
        Assert.Equal(1, await db.CommentModerations.CountAsync());
        Assert.Equal(survivorId, (await db.ProcessedComments.SingleAsync()).MappingId);

        // Recurring poll + reply-sweep torn down for both removed mappings (idempotent removal).
        Assert.Contains(activeId, scheduler.Removed);
        Assert.Contains(inactiveId, scheduler.Removed);
        Assert.Contains(activeId, scheduler.ReplySweepsRemoved);
        Assert.Contains(inactiveId, scheduler.ReplySweepsRemoved);
        Assert.DoesNotContain(survivorId, scheduler.Removed);
    }

    /// <summary>Slack client that is never called on the disconnect path (delete touches no Slack API).</summary>
    private sealed class NoopSlackClient : ISlackClient
    {
        public Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SlackChannelInfo>>([]);
        public Task<SlackPostResult> PostCommentAsync(string botToken, string channelId, CommentNotification comment,
            string? threadTs = null, Guid? mappingId = null, bool canModerate = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
