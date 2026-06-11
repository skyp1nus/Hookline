using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Features;
using Hookline.Modules.YouTubeUploads.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// The uploads slice of the System Danger Zone. Pause-all flips every active route to paused (and audits);
/// reset wipes ONLY operational data (jobs + per-job history) + the module's Redis namespace, while routes,
/// settings and the audit trail survive.
/// </summary>
public sealed class UploadsMaintenanceTests
{
    private static ChannelMapping NewMapping(bool active) => new()
    {
        Id = Guid.NewGuid(),
        SlackWorkspaceId = Guid.NewGuid(),
        SlackChannelId = $"C-{Guid.NewGuid():N}",
        SlackChannelName = "#route",
        GoogleAccountId = Guid.NewGuid(),
        IsActive = active,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static UploadJob NewJob() => new()
    {
        Id = Guid.NewGuid(),
        SlackEventId = $"evt-{Guid.NewGuid():N}",
        SlackChannelId = "C1",
        SlackUserId = "U1",
        SlackMessageTs = "1700000000.0001",
        DriveFileId = "drive-1",
        State = JobState.Done,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task PauseAll_pauses_every_active_route_and_audits()
    {
        using var db = TestDb.Create();
        db.ChannelMappings.AddRange(NewMapping(true), NewMapping(true), NewMapping(false));
        await db.SaveChangesAsync();

        var audit = new RecordingAuditLog();
        var control = new UploadsMaintenanceControl(db, new RecordingCachePurge(), audit);

        var result = await control.PauseAllAsync();

        Assert.Equal(2, result.Affected);
        Assert.All(await db.ChannelMappings.ToListAsync(), m => Assert.False(m.IsActive));
        Assert.Single(audit.Rows, r => r.Action == "maintenance.pause-all" && r.Module == "youtube-uploads");
    }

    [Fact]
    public async Task ResetData_wipes_jobs_and_history_keeps_routes_and_purges_cache()
    {
        using var db = TestDb.Create();
        var route = NewMapping(true);
        db.ChannelMappings.Add(route);
        var job = NewJob();
        db.Jobs.Add(job);
        db.JobHistory.Add(new JobStateHistory { JobId = job.Id, FromState = JobState.Queued, ToState = JobState.Done, At = DateTimeOffset.UtcNow });

        // Connection / secret config the reset MUST NOT touch: the OAuth project (its client secret is the
        // one encrypted column in this schema), its account binding, and the module-local channel cache.
        // This guards against a future RemoveRange accidentally widening the wipe into config/secrets.
        var project = new GoogleProject
        {
            Label = "Project A",
            ClientId = "client-1",
            EncryptedClientSecret = "super-secret",
            Status = GoogleProject.StatusActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.GoogleProjects.Add(project);
        db.GoogleAccountBindings.Add(new GoogleAccountBinding
        {
            AccountId = Guid.NewGuid(),
            ProjectId = project.Id,
            Label = "Bound account",
            Status = "Active",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SlackChannels.Add(new SlackChannel { WorkspaceId = Guid.NewGuid(), SlackChannelId = "C1", Name = "#chan" });
        await db.SaveChangesAsync();

        var cache = new RecordingCachePurge();
        var audit = new RecordingAuditLog();
        var control = new UploadsMaintenanceControl(db, cache, audit);

        var result = await control.ResetDataAsync();

        Assert.Empty(db.Jobs);
        Assert.Empty(db.JobHistory);
        Assert.Single(db.ChannelMappings);                 // routing config survives
        Assert.True((await db.ChannelMappings.FirstAsync()).IsActive); // and is untouched (still active)

        // Connections + secrets + bindings + channel cache all survive the operational-only wipe.
        Assert.Single(db.GoogleProjects);
        Assert.Equal("super-secret", (await db.GoogleProjects.FirstAsync()).EncryptedClientSecret); // secret intact
        Assert.Single(db.GoogleAccountBindings);           // OAuth account binding survives
        Assert.Single(db.SlackChannels);                   // module-local channel cache survives

        Assert.Contains("ytu:", cache.Prefixes);           // module Redis namespace purged
        Assert.Single(audit.Rows, r => r.Action == "maintenance.reset" && r.Module == "youtube-uploads");
        Assert.Equal(2, result.Affected);                  // 1 job + 1 history row
    }
}
