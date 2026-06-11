using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// Pins the centralized audit guarantee: EVERY job-state transition writes one shared audit entry from
/// <see cref="JobService.TransitionAsync"/>. This is the single chokepoint the previously-unaudited paths
/// funnel through — cancel-while-queued and decline (ProviderEndpoints) and startup-recovery terminal
/// transitions all call TransitionAsync — so proving it here proves those paths now emit, without standing
/// up the whole pipeline. The per-job JobStateHistory timeline must remain intact alongside the audit row.
/// </summary>
public sealed class JobAuditTests
{
    private sealed record AuditEntry(string Action, string? Module, string? EntityType, string? EntityId, string? Detail);

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(string action, string? module = null, string? entityType = null,
            string? entityId = null, string? detail = null, string? actor = null, CancellationToken ct = default)
        {
            Entries.Add(new AuditEntry(action, module, entityType, entityId, detail));
            return Task.CompletedTask;
        }
    }

    private sealed class PassthroughProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
        public bool TryUnprotect(string ciphertext, out string plaintext) { plaintext = ciphertext; return true; }
    }

    private static YouTubeUploadsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<YouTubeUploadsDbContext>()
                .UseInMemoryDatabase($"jobaudit-{Guid.NewGuid()}")
                .Options,
            new PassthroughProtector());

    private static UploadJob NewQueuedJob() => new()
    {
        Id = Guid.NewGuid(),
        SlackEventId = "evt-1",
        SlackChannelId = "C1",
        SlackUserId = "U1",
        SlackMessageTs = "1700000000.0001",
        DriveFileId = "drive-1",
        State = JobState.Queued,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Cancel_while_queued_transition_emits_an_audit_entry_and_keeps_history()
    {
        using var db = NewDb();
        var audit = new RecordingAuditLog();
        // googleAccounts is unused by TransitionAsync — only the db + audit collaborators matter here.
        var jobs = new JobService(db, googleAccounts: null!, audit);

        var job = NewQueuedJob();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        // The exact call cancel-while-queued (ProviderEndpoints.cs) and decline make.
        await jobs.TransitionAsync(job, JobState.Cancelled, "cancelled by user (queued)");

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("upload.cancelled", entry.Action);
        Assert.Equal("youtube-uploads", entry.Module);
        Assert.Equal("upload_job", entry.EntityType);
        Assert.Equal(job.Id.ToString(), entry.EntityId);
        Assert.Equal("cancelled by user (queued)", entry.Detail);

        // Centralizing audit must NOT drop the per-job timeline.
        Assert.Contains(db.JobHistory, h => h.JobId == job.Id && h.ToState == JobState.Cancelled);
    }

    [Theory]
    [InlineData(JobState.Done, "upload.done")]
    [InlineData(JobState.Failed, "upload.failed")]
    [InlineData(JobState.Blocked, "upload.blocked")]
    [InlineData(JobState.Queued, "upload.queued")]
    public async Task Transition_action_name_tracks_the_target_state(JobState to, string expectedAction)
    {
        using var db = NewDb();
        var audit = new RecordingAuditLog();
        var jobs = new JobService(db, googleAccounts: null!, audit);

        var job = NewQueuedJob();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        await jobs.TransitionAsync(job, to, "note");

        Assert.Equal(expectedAction, Assert.Single(audit.Entries).Action);
        Assert.Equal(to, job.State);
    }
}
