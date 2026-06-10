using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// On startup the scheduler reconciles Hangfire's recurring jobs with the mappings table: every active
/// mapping gets its poll job (plus a reply sweep when replies are enabled), inactive ones get none, and
/// orphan jobs whose mapping was deleted/deactivated while the host was down are pruned. This is what
/// makes jobs survive a restart and a fresh database self-heal.
/// </summary>
public class SchedulerReconcileTests
{
    private static string Poll(Guid id) => HangfirePollingScheduler.JobId(id);
    private static string Sweep(Guid id) => HangfirePollingScheduler.ReplySweepJobId(id);

    [Fact]
    public async Task SyncAll_adds_active_prunes_orphans_and_honours_reply_settings()
    {
        using var db = TestDb.Create();

        var withReplies = new ChannelMapping
        {
            YouTubeChannelId = Guid.NewGuid(), SlackChannelId = Guid.NewGuid(),
            Frequency = PollingFrequency.FiveMinutes, IsActive = true,
            IncludeReplies = true, ReplySweepFrequency = ReplyScanFrequency.Daily,
        };
        var noReplies = new ChannelMapping
        {
            YouTubeChannelId = Guid.NewGuid(), SlackChannelId = Guid.NewGuid(),
            Frequency = PollingFrequency.OneHour, IsActive = true,
            IncludeReplies = false, ReplySweepFrequency = ReplyScanFrequency.Off,
        };
        var inactive = new ChannelMapping
        {
            YouTubeChannelId = Guid.NewGuid(), SlackChannelId = Guid.NewGuid(),
            Frequency = PollingFrequency.OneMinute, IsActive = false,
        };
        db.ChannelMappings.AddRange(withReplies, noReplies, inactive);
        await db.SaveChangesAsync();

        var fake = new FakeJobScheduler();
        // Stale recurring jobs from a prior run whose mappings no longer exist / are inactive.
        var orphan = Guid.NewGuid();
        fake.Recurring[Poll(orphan)] = "*/15 * * * *";
        fake.Recurring[Sweep(orphan)] = "0 4 * * *";
        fake.Recurring[Poll(inactive.Id)] = "* * * * *";

        var scheduler = new HangfirePollingScheduler(fake, db, NullLogger<HangfirePollingScheduler>.Instance);
        await scheduler.SyncAllAsync();

        // Active mappings scheduled with their cron.
        Assert.Equal("*/5 * * * *", fake.Recurring[Poll(withReplies.Id)]);
        Assert.Equal("0 4 * * *", fake.Recurring[Sweep(withReplies.Id)]);
        Assert.Equal("0 * * * *", fake.Recurring[Poll(noReplies.Id)]);
        // Replies-off mapping gets no sweep.
        Assert.False(fake.Recurring.ContainsKey(Sweep(noReplies.Id)));
        // Orphans + the inactive mapping's stale job are pruned.
        Assert.False(fake.Recurring.ContainsKey(Poll(orphan)));
        Assert.False(fake.Recurring.ContainsKey(Sweep(orphan)));
        Assert.False(fake.Recurring.ContainsKey(Poll(inactive.Id)));
    }

    [Fact]
    public async Task SyncAll_is_idempotent()
    {
        using var db = TestDb.Create();
        var m = new ChannelMapping
        {
            YouTubeChannelId = Guid.NewGuid(), SlackChannelId = Guid.NewGuid(),
            Frequency = PollingFrequency.FifteenMinutes, IsActive = true,
        };
        db.ChannelMappings.Add(m);
        await db.SaveChangesAsync();

        var fake = new FakeJobScheduler();
        var scheduler = new HangfirePollingScheduler(fake, db, NullLogger<HangfirePollingScheduler>.Instance);

        await scheduler.SyncAllAsync();
        await scheduler.SyncAllAsync();

        Assert.True(fake.Recurring.ContainsKey(Poll(m.Id)));
        Assert.Single(fake.Recurring); // no duplicate jobs after a second reconcile
    }
}
