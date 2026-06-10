using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Jobs;

/// <summary>
/// Runs once on host start (after migrations): seeds the env "Default (env)" Google project when
/// configured, then self-heals jobs interrupted by a restart. Queued/Downloading happened BEFORE the
/// YouTube upload → safe to resume from scratch (re-enqueue). Uploading/Processing are the point of no
/// return → never re-upload; fail with a verify note (unless the video id was already saved → mark Done).
/// </summary>
public sealed class YouTubeUploadsStartupService(
    IServiceScopeFactory scopeFactory,
    IJobScheduler scheduler,
    ILogger<YouTubeUploadsStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        await SeedEnvProjectAsync(sp, ct);
        await RecoverInterruptedJobsAsync(sp, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedEnvProjectAsync(IServiceProvider sp, CancellationToken ct)
    {
        var google = sp.GetRequiredService<IOptions<YouTubeUploadsOptions>>().Value.Google;
        if (string.IsNullOrWhiteSpace(google.SeedClientId) || string.IsNullOrWhiteSpace(google.SeedClientSecret))
        {
            return;
        }

        var projects = sp.GetRequiredService<GoogleProjectsService>();
        if (await projects.CountAsync(ct) > 0 || await projects.ClientIdExistsAsync(google.SeedClientId, ct))
        {
            return;
        }

        var created = await projects.CreateAsync(google.SeedLabel, google.SeedClientId, google.SeedClientSecret, ct);
        logger.LogInformation("Seeded default YouTubeUploads project {Id} from YouTubeUploads:Google seed config", created.Id);
    }

    private async Task RecoverInterruptedJobsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<YouTubeUploadsDbContext>();

        var interrupted = await db.Jobs.Where(j =>
            j.State == JobState.Queued || j.State == JobState.Downloading ||
            j.State == JobState.Uploading || j.State == JobState.Processing).ToListAsync(ct);
        if (interrupted.Count == 0) return;

        int resumed = 0, failed = 0, finalized = 0;
        var now = DateTimeOffset.UtcNow;
        var toEnqueue = new List<Guid>();

        foreach (var job in interrupted)
        {
            if (job.YouTubeVideoId is not null)
            {
                job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Done, At = now, Note = "recovered: upload had completed" });
                job.State = JobState.Done; job.UpdatedAt = now; finalized++;
            }
            else if (job.State is JobState.Uploading or JobState.Processing)
            {
                job.ErrorMessage = "Interrupted after the YouTube upload started — verify in YouTube Studio; the bot won’t re-upload.";
                job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Failed, At = now, Note = "recovered: interrupted past point of no return" });
                job.State = JobState.Failed; job.UpdatedAt = now; failed++;
            }
            else // Queued or Downloading — resume from scratch
            {
                if (job.State == JobState.Downloading)
                {
                    job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Queued, At = now, Note = "recovered: re-queued after restart" });
                    job.State = JobState.Queued; job.UpdatedAt = now;
                }
                toEnqueue.Add(job.Id);
                resumed++;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var id in toEnqueue)
        {
            scheduler.Enqueue<UploadJobHandler>(h => h.RunAsync(id, CancellationToken.None));
        }

        logger.LogInformation("Job recovery: {Resumed} re-queued, {Failed} failed (point of no return), {Finalized} finalized", resumed, failed, finalized);
    }
}
