using Microsoft.Extensions.Logging;

namespace Hookline.Modules.Sample;

/// <summary>A no-op recurring job that proves the module → IJobScheduler → Hangfire path.</summary>
public sealed class SamplePingJob(ILogger<SamplePingJob> logger)
{
    public Task RunAsync()
    {
        logger.LogInformation("Sample module heartbeat at {Time:O}.", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
