using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Features;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Caching;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The comments slice of the System Danger Zone. Pause-all flips every active mapping to paused AND tears
/// down its per-mapping recurring jobs; reset wipes the operational state (dedup ledger / retry queue /
/// quota), advances every mapping's watermark and purges the module's Redis namespace, while the mappings
/// themselves survive.
/// </summary>
public sealed class CommentsMaintenanceTests
{
    private sealed class SpyPollingScheduler : IPollingScheduler
    {
        public List<Guid> Removed { get; } = new();
        public List<Guid> RemovedSweep { get; } = new();

        public void Schedule(Guid mappingId, PollingFrequency frequency) { }
        public void Remove(Guid mappingId) => Removed.Add(mappingId);
        public void ScheduleReplySweep(Guid mappingId, ReplyScanFrequency frequency) { }
        public void RemoveReplySweep(Guid mappingId) => RemovedSweep.Add(mappingId);
        public Task SyncAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingCachePurge : ICachePurge
    {
        public List<string> Prefixes { get; } = new();
        public Task<long> PurgeByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            Prefixes.Add(prefix);
            return Task.FromResult(0L);
        }
    }

    private static ChannelMapping NewMapping(bool active) => new()
    {
        Id = Guid.NewGuid(),
        YouTubeChannelId = Guid.NewGuid(),
        SlackChannelId = Guid.NewGuid(),
        Frequency = PollingFrequency.FifteenMinutes,
        IsActive = active,
        CommentsSinceUtc = DateTimeOffset.UtcNow.AddDays(-30),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
    };

    [Fact]
    public async Task PauseAll_pauses_active_mappings_and_tears_down_their_jobs()
    {
        using var db = TestDb.Create();
        var a = NewMapping(true);
        var b = NewMapping(true);
        var alreadyPaused = NewMapping(false);
        db.ChannelMappings.AddRange(a, b, alreadyPaused);
        await db.SaveChangesAsync();

        var scheduler = new SpyPollingScheduler();
        var audit = new RecordingCommentsAudit();
        var control = new CommentsMaintenanceControl(db, scheduler, new RecordingCachePurge(), audit);

        var result = await control.PauseAllAsync();

        Assert.Equal(2, result.Affected);
        Assert.All(await db.ChannelMappings.ToListAsync(), m => Assert.False(m.IsActive));
        // Only the two formerly-active mappings get their recurring jobs torn down.
        Assert.Equal(2, scheduler.Removed.Count);
        Assert.Contains(a.Id, scheduler.Removed);
        Assert.Contains(b.Id, scheduler.Removed);
        Assert.DoesNotContain(alreadyPaused.Id, scheduler.Removed);
        Assert.Equal(2, scheduler.RemovedSweep.Count);
        Assert.Contains(audit.Entries, e => e.Category == "maintenance.pause-all");
    }

    [Fact]
    public async Task ResetData_wipes_operational_state_advances_watermark_keeps_mappings_and_purges_cache()
    {
        using var db = TestDb.Create();
        var mapping = NewMapping(true);
        var oldWatermark = mapping.CommentsSinceUtc;
        mapping.LastError = "boom";
        mapping.LastPolledAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.ChannelMappings.Add(mapping);
        db.ProcessedComments.Add(new ProcessedComment { MappingId = mapping.Id, CommentId = "c1", VideoId = "v1", ProcessedAt = DateTimeOffset.UtcNow });
        db.PendingDeliveries.Add(new PendingDelivery { MappingId = mapping.Id, CommentId = "c1", VideoId = "v1", PayloadJson = "{}", NextAttemptAt = DateTimeOffset.UtcNow });
        db.QuotaUsages.Add(new QuotaUsage { ApiKeyId = Guid.NewGuid(), UsageDate = new DateOnly(2026, 6, 11), UnitsUsed = 5 });
        await db.SaveChangesAsync();

        var cache = new RecordingCachePurge();
        var audit = new RecordingCommentsAudit();
        var control = new CommentsMaintenanceControl(db, new SpyPollingScheduler(), cache, audit);

        var result = await control.ResetDataAsync();

        Assert.Empty(db.ProcessedComments);
        Assert.Empty(db.PendingDeliveries);
        Assert.Empty(db.QuotaUsages);

        var kept = Assert.Single(db.ChannelMappings); // mapping (routing) survives
        Assert.True(kept.CommentsSinceUtc > oldWatermark); // watermark advanced so old comments can't replay
        Assert.Null(kept.LastError);
        Assert.Null(kept.LastPolledAt);

        Assert.Contains("ytc:", cache.Prefixes);
        Assert.Contains(audit.Entries, e => e.Category == "maintenance.reset");
        Assert.Equal(3, result.Affected); // 1 processed + 1 pending + 1 quota
    }
}
