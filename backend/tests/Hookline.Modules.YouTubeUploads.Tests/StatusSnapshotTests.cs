using System.Text.Json;

using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>Two uploads running in one channel must both stay visible in the Slack status.</summary>
public sealed class StatusSnapshotTests
{
    [Fact]
    public async Task Snapshot_lists_every_active_job_in_the_channel_oldest_first()
    {
        using var db = TestDb.Create();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        db.Jobs.AddRange(
            Job("C1", "second.mp4", JobState.Downloading, t0.AddMinutes(12)),
            Job("C1", "first.mp4", JobState.Downloading, t0),
            Job("C1", "uploading.mp4", JobState.Uploading, t0.AddMinutes(20)),
            Job("C2", "other-channel.mp4", JobState.Downloading, t0));
        await db.SaveChangesAsync();

        var snap = await new JobService(db, googleAccounts: null!, new RecordingAuditLog()).GetStatusSnapshotAsync("C1");

        Assert.Equal(["first.mp4", "second.mp4", "uploading.mp4"], snap.Active.Select(j => j.OriginalFileName));
    }

    [Fact]
    public void Status_message_renders_each_active_job()
    {
        var view = new StatusView(98, 100,
            [
                new ActiveJobView("first.mp4", "Downloading from Drive", 95, 1100L << 20, 1154L << 20, false),
                new ActiveJobView("second.mp4", "Downloading from Drive", 30, 350L << 20, 1154L << 20, false),
            ],
            [], [], 0, "Dan - Smart Tutorials");

        var (text, blocks) = SlackBlocks.Status(view);
        var json = JsonSerializer.Serialize(blocks);

        Assert.Contains("first.mp4", json);
        Assert.Contains("95%", json);
        Assert.Contains("second.mp4", json);
        Assert.Contains("30%", json);
        Assert.Contains("+1 more", text);
    }

    [Fact]
    public void Long_queue_is_truncated_under_the_slack_block_limit()
    {
        var queued = Enumerable.Range(1, 60).Select(i => new QueuedJobView(Guid.NewGuid(), $"video-{i}.mp4")).ToList();
        var view = new StatusView(98, 100, [], queued, [], 0, null);

        var (_, blocks) = SlackBlocks.Status(view);
        var json = JsonSerializer.Serialize(blocks);

        Assert.True(blocks.Length <= 50, $"{blocks.Length} blocks");
        Assert.Contains("and 40 more queued", json);
    }

    private static UploadJob Job(string channel, string name, JobState state, DateTimeOffset downloadStartedAt) => new()
    {
        Id = Guid.NewGuid(),
        SlackEventId = $"evt-{name}",
        SlackChannelId = channel,
        SlackUserId = "U1",
        SlackMessageTs = "1700000000.0001",
        DriveFileId = $"drive-{name}",
        OriginalFileName = name,
        State = state,
        DownloadStartedAt = downloadStartedAt,
        CreatedAt = downloadStartedAt,
        UpdatedAt = downloadStartedAt,
    };
}
