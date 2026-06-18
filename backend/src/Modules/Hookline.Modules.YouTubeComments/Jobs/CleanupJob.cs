using Hangfire;

using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// Recurring retention job: trims the module-local <c>processed_comments</c> dedup ledger, which would
/// otherwise grow unbounded. It cuts at a UTC DAY boundary and ARCHIVES every whole day it is about to
/// delete into <c>comment_daily_stats</c> (via <see cref="CommentRollupService"/>) FIRST, so long-horizon
/// history survives the trim. Audit-log retention is owned by the shared host, so it is NOT this job's
/// concern. Set-based delete via <c>ExecuteDeleteAsync</c> (no entities loaded), hitting the
/// <c>processed_at</c> index.
/// </summary>
public sealed class CleanupJob(
    YouTubeCommentsDbContext db,
    CommentRollupService rollup,
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

        // Cut at a UTC DAY boundary, not an intra-day instant. This is critical for the rollup: deletion
        // must remove only WHOLE days so a day is never left half-trimmed. Otherwise a later run would
        // re-roll a partially-deleted day and overwrite its archived total with an undercount (data loss).
        var cutoffDay = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-_options.ProcessedCommentDays);
        var cutoff = new DateTimeOffset(cutoffDay, TimeOnly.MinValue, TimeSpan.Zero);

        // Archive BEFORE deleting: roll up every whole day about to be trimmed (idempotent), so a day still
        // appears in long-horizon history even though its raw rows are gone. The range spans the OLDEST
        // remaining row (across BOTH the processed ledger AND the moderation ledger, so a moderation-only
        // day is archived too) up to the last day being deleted (cutoffDay − 1). Steady-state this is ~one
        // day; on the first run after enabling the rollup it backfills the whole to-be-deleted span.
        var oldestProcessed = await db.ProcessedComments.AsNoTracking()
            .Where(p => p.ProcessedAt < cutoff)
            .MinAsync(p => (DateTimeOffset?)p.ProcessedAt, ct);
        var oldestModeration = await db.CommentModerations.AsNoTracking()
            .Where(m => m.CreatedAt < cutoff)
            .MinAsync(m => (DateTimeOffset?)m.CreatedAt, ct);
        var oldest = Earliest(oldestProcessed, oldestModeration);

        if (oldest is { } first)
        {
            var fromDay = DateOnly.FromDateTime(first.UtcDateTime);
            var toDay = cutoffDay.AddDays(-1); // last fully-deleted day (delete is < cutoffDay 00:00)
            if (fromDay <= toDay)
            {
                var rolledRows = await rollup.RollupRangeAsync(fromDay, toDay, ct);
                await audit.LogAsync(
                    AuditLevel.Information, "Retention",
                    $"Pre-cleanup rollup: archived {rolledRows} mapping-day row(s) before trimming",
                    details: $"{{\"rolledRows\":{rolledRows},\"fromDay\":\"{fromDay:O}\",\"toDay\":\"{toDay:O}\"}}", ct: ct);
            }
        }

        var processedDeleted = await db.ProcessedComments
            .Where(p => p.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        await audit.LogAsync(
            AuditLevel.Information, "Retention",
            $"Retention cleanup: removed {processedDeleted} processed comment(s)",
            details: $"{{\"processedDeleted\":{processedDeleted},\"processedCommentDays\":{_options.ProcessedCommentDays}}}", ct: ct);

        logger.LogInformation("Retention cleanup removed {ProcessedDeleted} processed comment(s)", processedDeleted);
    }

    /// <summary>The earlier of two optional instants (ignoring nulls).</summary>
    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a < b ? a : b;
    }
}
