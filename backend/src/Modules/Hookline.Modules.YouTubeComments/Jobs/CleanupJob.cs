using Hangfire;

using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// Recurring retention job: trims the module-local <c>processed_comments</c> dedup ledger, which would
/// otherwise grow unbounded. Audit-log retention is owned by the shared host, so it is NOT this job's
/// concern. Set-based delete via <c>ExecuteDeleteAsync</c> (no entities loaded), hitting the
/// <c>processed_at</c> index.
/// </summary>
public sealed class CleanupJob(
    YouTubeCommentsDbContext db,
    IOptions<YouTubeCommentsOptions> options,
    ICommentsAudit audit,
    ILogger<CleanupJob> logger)
{
    private readonly YouTubeCommentsOptions.RetentionSettings _options = options.Value.Retention;

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct)
    {
        if (_options.ProcessedCommentDays <= 0)
            return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.ProcessedCommentDays);
        var processedDeleted = await db.ProcessedComments
            .Where(p => p.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        await audit.LogAsync(
            "Information", "Retention",
            $"Retention cleanup: removed {processedDeleted} processed comment(s)",
            details: $"{{\"processedDeleted\":{processedDeleted},\"processedCommentDays\":{_options.ProcessedCommentDays}}}", ct: ct);

        logger.LogInformation("Retention cleanup removed {ProcessedDeleted} processed comment(s)", processedDeleted);
    }
}
