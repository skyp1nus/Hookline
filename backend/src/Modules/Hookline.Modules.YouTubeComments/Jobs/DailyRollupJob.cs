using Hangfire;

using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// Recurring nightly job that archives the last few complete UTC days into <c>comment_daily_stats</c> via
/// <see cref="CommentRollupService"/>, so long-horizon trends survive the retention <see cref="CleanupJob"/>.
/// It re-rolls a trailing window (idempotent) to absorb late deliveries and a missed run, and is scheduled
/// AHEAD of the cleanup job — which itself re-rolls the days it is about to delete as a final guard.
/// </summary>
public sealed class DailyRollupJob(
    CommentRollupService rollup,
    IOptions<YouTubeCommentsOptions> options,
    ICommentsAudit audit,
    ILogger<DailyRollupJob> logger)
{
    private readonly YouTubeCommentsOptions.RollupSettings _options = options.Value.Rollup;

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct)
    {
        var rows = await rollup.RollupTrailingDaysAsync(_options.LookbackDays, ct);

        await audit.LogAsync(
            AuditLevel.Information, "Retention",
            $"Daily rollup: archived {rows} mapping-day row(s) over the last {_options.LookbackDays} day(s)",
            details: $"{{\"rolledRows\":{rows},\"lookbackDays\":{_options.LookbackDays}}}", ct: ct);

        logger.LogInformation("Daily rollup archived {Rows} mapping-day row(s)", rows);
    }
}
