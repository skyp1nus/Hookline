using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// Runs once on host start (after migrations): reconciles the per-mapping recurring poll + reply-sweep
/// jobs against the mappings table (re-adding active ones and pruning orphans so jobs survive a restart
/// and a fresh database self-heals), then registers — or removes — the static delivery-retry and
/// retention-cleanup recurring jobs from config. Every step is non-fatal so a transient failure here
/// never stops the host from booting.
/// </summary>
public sealed class YouTubeCommentsStartupService(
    IServiceScopeFactory scopeFactory,
    IJobScheduler scheduler,
    IOptions<YouTubeCommentsOptions> options,
    ILogger<YouTubeCommentsStartupService> logger) : IHostedService
{
    private const string DeliveryJobId = "ytc:delivery-retry";
    private const string RetentionJobId = "ytc:retention-cleanup";

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var polling = scope.ServiceProvider.GetRequiredService<IPollingScheduler>();
            await polling.SyncAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reconcile recurring polling jobs on startup");
        }

        try
        {
            var o = options.Value;

            if (o.Delivery.Enabled)
            {
                scheduler.AddOrUpdateRecurring<DeliveryRetryJob>(DeliveryJobId, j => j.RunAsync(CancellationToken.None), o.Delivery.Cron);
                logger.LogInformation("Delivery retry scheduled ({Cron})", o.Delivery.Cron);
            }
            else
            {
                scheduler.RemoveRecurring(DeliveryJobId);
            }

            if (o.Retention.Enabled)
            {
                scheduler.AddOrUpdateRecurring<CleanupJob>(RetentionJobId, j => j.RunAsync(CancellationToken.None), o.Retention.Cron);
                logger.LogInformation("Retention cleanup scheduled ({Cron})", o.Retention.Cron);
            }
            else
            {
                scheduler.RemoveRecurring(RetentionJobId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule the static delivery/retention jobs on startup");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
