using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Logging.Abstractions;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Proves the dynamic scheduler stays in sync with mapping mutations through the real call chain
/// MappingService → HangfirePollingScheduler → IJobScheduler: creating a mapping adds its recurring poll,
/// deleting removes it, and toggling active/inactive pauses/resumes it. Reply-sweep registration tracks
/// the IncludeReplies + sweep-frequency settings. This is what keeps Hangfire's recurring jobs honest as
/// the write-path buttons mutate mappings (the startup reconcile is covered by SchedulerReconcileTests).
/// </summary>
public class MappingSchedulerSyncTests
{
    private static string Poll(Guid id) => HangfirePollingScheduler.JobId(id);
    private static string Sweep(Guid id) => HangfirePollingScheduler.ReplySweepJobId(id);

    private static (MappingService service, FakeJobScheduler jobs) Build(YouTubeCommentsDbContext db)
    {
        var jobs = new FakeJobScheduler();
        var scheduler = new HangfirePollingScheduler(jobs, db, NullLogger<HangfirePollingScheduler>.Instance);
        var slack = new FakeSlackConnections();
        var service = new MappingService(db, scheduler, new RecordingCommentsAudit(), slack);
        return (service, jobs);
    }

    private static async Task<(Guid ytChannelId, Guid slackChannelId)> SeedEndpointsAsync(YouTubeCommentsDbContext db)
    {
        var yt = new YouTubeChannel { YouTubeChannelId = "UC_test", Title = "Test Channel" };
        var slack = new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = "C123", Name = "general" };
        db.YouTubeChannels.Add(yt);
        db.SlackChannels.Add(slack);
        await db.SaveChangesAsync();
        return (yt.Id, slack.Id);
    }

    [Fact]
    public async Task Create_schedules_the_recurring_poll_with_its_cron()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(yt, slack, PollingFrequency.FiveMinutes));

        Assert.True(dto.IsActive);
        Assert.Equal("*/5 * * * *", jobs.Recurring[Poll(dto.Id)]);
        // Replies off by default → no sweep job.
        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));
    }

    [Fact]
    public async Task Create_with_replies_also_schedules_the_reply_sweep()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FifteenMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Daily));

        Assert.Equal("*/15 * * * *", jobs.Recurring[Poll(dto.Id)]);
        Assert.Equal("0 4 * * *", jobs.Recurring[Sweep(dto.Id)]);
    }

    [Fact]
    public async Task Toggle_inactive_then_active_pauses_and_resumes_the_poll()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(yt, slack, PollingFrequency.OneHour));
        Assert.True(jobs.Recurring.ContainsKey(Poll(dto.Id)));

        // Pause.
        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: false, IncludeReplies: null, ReplySweepFrequency: null, ReplyWindowDays: null));
        Assert.False(jobs.Recurring.ContainsKey(Poll(dto.Id)));
        Assert.Contains(Poll(dto.Id), jobs.Removed);

        // Resume.
        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: true, IncludeReplies: null, ReplySweepFrequency: null, ReplyWindowDays: null));
        Assert.Equal("0 * * * *", jobs.Recurring[Poll(dto.Id)]);
    }

    [Fact]
    public async Task Update_frequency_reschedules_with_the_new_cron()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(yt, slack, PollingFrequency.OneHour));
        Assert.Equal("0 * * * *", jobs.Recurring[Poll(dto.Id)]);

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: PollingFrequency.OneMinute, IsActive: null, IncludeReplies: null,
            ReplySweepFrequency: null, ReplyWindowDays: null));

        Assert.Equal("* * * * *", jobs.Recurring[Poll(dto.Id)]);
    }

    [Fact]
    public async Task Delete_removes_both_the_poll_and_the_reply_sweep()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FiveMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Hourly));
        Assert.True(jobs.Recurring.ContainsKey(Poll(dto.Id)));
        Assert.True(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        var deleted = await service.DeleteAsync(dto.Id);

        Assert.True(deleted);
        Assert.False(jobs.Recurring.ContainsKey(Poll(dto.Id)));
        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));
        Assert.Contains(Poll(dto.Id), jobs.Removed);
        Assert.Contains(Sweep(dto.Id), jobs.Removed);
    }

    [Fact]
    public async Task Toggle_inactive_then_active_with_replies_pauses_and_resumes_both_jobs()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FiveMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Daily));
        Assert.True(jobs.Recurring.ContainsKey(Poll(dto.Id)));
        Assert.True(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: false, IncludeReplies: null, ReplySweepFrequency: null, ReplyWindowDays: null));
        Assert.False(jobs.Recurring.ContainsKey(Poll(dto.Id)));
        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: true, IncludeReplies: null, ReplySweepFrequency: null, ReplyWindowDays: null));
        Assert.Equal("*/5 * * * *", jobs.Recurring[Poll(dto.Id)]);
        Assert.Equal("0 4 * * *", jobs.Recurring[Sweep(dto.Id)]);
    }

    [Fact]
    public async Task Update_includeReplies_false_to_true_schedules_the_sweep()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(yt, slack, PollingFrequency.FifteenMinutes));
        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: null, IncludeReplies: true,
            ReplySweepFrequency: ReplyScanFrequency.Daily, ReplyWindowDays: null));

        Assert.Equal("0 4 * * *", jobs.Recurring[Sweep(dto.Id)]);
    }

    [Fact]
    public async Task Update_includeReplies_true_to_false_removes_the_sweep()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FifteenMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Hourly));
        Assert.True(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: null, IncludeReplies: false, ReplySweepFrequency: null, ReplyWindowDays: null));

        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));
        Assert.Contains(Sweep(dto.Id), jobs.Removed);
        // The poll itself is untouched.
        Assert.True(jobs.Recurring.ContainsKey(Poll(dto.Id)));
    }

    [Fact]
    public async Task Update_reply_sweep_frequency_reschedules_with_the_new_cron()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FifteenMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Daily));
        Assert.Equal("0 4 * * *", jobs.Recurring[Sweep(dto.Id)]);

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: null, IncludeReplies: null,
            ReplySweepFrequency: ReplyScanFrequency.Hourly, ReplyWindowDays: null));

        Assert.Equal("0 * * * *", jobs.Recurring[Sweep(dto.Id)]);
    }

    [Fact]
    public async Task Update_reply_sweep_to_off_removes_the_sweep_but_keeps_the_poll()
    {
        using var db = TestDb.Create();
        var (yt, slack) = await SeedEndpointsAsync(db);
        var (service, jobs) = Build(db);

        var dto = await service.CreateAsync(new CreateMappingRequest(
            yt, slack, PollingFrequency.FiveMinutes,
            IncludeReplies: true, ReplySweepFrequency: ReplyScanFrequency.Hourly));
        Assert.True(jobs.Recurring.ContainsKey(Sweep(dto.Id)));

        await service.UpdateAsync(dto.Id, new UpdateMappingRequest(
            Frequency: null, IsActive: null, IncludeReplies: null,
            ReplySweepFrequency: ReplyScanFrequency.Off, ReplyWindowDays: null));

        Assert.False(jobs.Recurring.ContainsKey(Sweep(dto.Id)));
        Assert.True(jobs.Recurring.ContainsKey(Poll(dto.Id)));
    }
}
