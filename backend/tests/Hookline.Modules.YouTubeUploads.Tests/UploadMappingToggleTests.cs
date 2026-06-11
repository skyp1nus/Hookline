using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.Modules.YouTubeUploads.Jobs;

using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// P0 pause/resume "scheduler sync" for the event-driven uploads pipeline. Uploads have no per-mapping
/// recurring job; the scheduler reaction is the ingest gate. These tests pin: (1) the toggle flips
/// <c>IsActive</c> and audits it, (2) the value the gate reads (<see cref="MappingRoute.IsActive"/>) tracks
/// the toggle, and (3) a paused route enqueues NOTHING on ingest, while resuming re-opens the gate.
/// </summary>
public sealed class UploadMappingToggleTests
{
    private static ChannelMapping NewMapping(string channelId, bool active) => new()
    {
        Id = Guid.NewGuid(),
        SlackWorkspaceId = Guid.NewGuid(),
        SlackChannelId = channelId,
        SlackChannelName = "#" + channelId,
        GoogleAccountId = Guid.NewGuid(),
        IsActive = active,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task UpdateAsync_toggles_IsActive_audits_and_is_visible_to_the_ingest_gate()
    {
        using var db = TestDb.Create();
        var mapping = NewMapping("paused-route", active: true);
        db.ChannelMappings.Add(mapping);
        await db.SaveChangesAsync();

        var audit = new RecordingAuditLog();
        var svc = new ChannelMappingService(db, workspaces: null!, googleAccounts: null!, audit);

        // Pause.
        Assert.True(await svc.UpdateAsync(mapping.Id, isActive: false));
        var paused = await svc.GetByChannelAsync("paused-route");
        Assert.NotNull(paused);
        Assert.False(paused!.IsActive); // the exact value the ingest gate reads

        var pauseRow = Assert.Single(audit.Rows, r => r.Action == "mapping.updated");
        Assert.Equal("youtube-uploads", pauseRow.Module);
        Assert.Equal("channel_mapping", pauseRow.EntityType);
        Assert.Equal("active=False", pauseRow.Detail);

        // Resume re-opens the gate.
        Assert.True(await svc.UpdateAsync(mapping.Id, isActive: true));
        var resumed = await svc.GetByChannelAsync("paused-route");
        Assert.True(resumed!.IsActive);
        Assert.Equal(2, audit.Rows.Count(r => r.Action == "mapping.updated"));
    }

    [Fact]
    public async Task UpdateAsync_returns_false_for_a_missing_mapping()
    {
        using var db = TestDb.Create();
        var svc = new ChannelMappingService(db, workspaces: null!, googleAccounts: null!, new RecordingAuditLog());
        Assert.False(await svc.UpdateAsync(Guid.NewGuid(), isActive: false));
    }

    [Fact]
    public async Task Paused_route_enqueues_nothing_on_ingest()
    {
        using var db = TestDb.Create();
        db.ChannelMappings.Add(NewMapping("C-paused", active: false));
        await db.SaveChangesAsync();

        var scheduler = new SpyJobScheduler();
        var mappings = new ChannelMappingService(db, workspaces: null!, googleAccounts: null!, new RecordingAuditLog());

        // Only the deps reached before the gate are real (dedup checks + mapping lookup); the rest are
        // never touched because a paused route returns at the gate before any parsing/upload work.
        var ingest = new SlackIngestService(
            parser: null!, oauth: null!, drive: null!, jobs: new NotSeenJobService(), mappings: mappings,
            slack: null!, workspaces: null!, status: null!, scheduler: scheduler, audit: new RecordingAuditLog(),
            logger: NullLogger<SlackIngestService>.Instance);

        await ingest.ProcessMessageAsync(
            new SlackMessageRef("evt-1", "C-paused", "U1", "1700000000.0001", "UPLOAD\nVideo: https://drive.google.com/file/d/x/view"),
            CancellationToken.None);

        Assert.Equal(0, scheduler.EnqueueCount); // the pipeline never triggered
    }
}
