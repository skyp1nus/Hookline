using System.Text.Json;

using Hangfire;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// Recurring job that drains <c>pending_deliveries</c> — Slack posts the poll couldn't deliver
/// transiently. Each due row is re-posted (replies threaded under their parent); on success it becomes
/// a <see cref="ProcessedComment"/> and the row is removed; on a transient failure it backs off
/// exponentially; after the configured max attempts it is dead-lettered (audited + dropped). A
/// permanently-gone Slack channel deactivates the mapping and clears its queue.
/// </summary>
public sealed class DeliveryRetryJob(
    YouTubeCommentsDbContext db,
    ISlackClient slack,
    IPollingScheduler scheduler,
    ISlackConnections slackConnections,
    IOptions<YouTubeCommentsOptions> options,
    ICommentsAudit audit,
    ILogger<DeliveryRetryJob> logger)
{
    private const int BatchSize = 200;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    private readonly YouTubeCommentsOptions.DeliverySettings _options = options.Value.Delivery;

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var due = await db.PendingDeliveries
            .Where(p => p.NextAttemptAt <= now)
            .OrderBy(p => p.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        var targets = new Dictionary<Guid, PostTarget?>();
        var deactivatedMappings = new HashSet<Guid>();

        var delivered = 0;
        var deadLettered = 0;

        foreach (var row in due)
        {
            if (deactivatedMappings.Contains(row.MappingId))
            {
                db.PendingDeliveries.Remove(row);
                continue;
            }

            var target = await ResolveTargetAsync(row.MappingId, targets, ct);
            if (target is null)
            {
                // Mapping deleted/inactive or workspace disconnected — nothing to deliver to. Drop the row.
                db.PendingDeliveries.Remove(row);
                continue;
            }

            var notification = DeserializePayload(row);
            if (notification is null)
            {
                await audit.LogAsync(AuditLevel.Error, "Delivery",
                    "Dropped a pending delivery with an unreadable payload", "ChannelMapping", row.MappingId.ToString(), ct: ct);
                db.PendingDeliveries.Remove(row);
                deadLettered++;
                continue;
            }

            var threadTs = await ResolveThreadTsAsync(row.MappingId, notification, ct);
            var result = await slack.PostCommentAsync(target.BotToken, target.ChannelId, notification, threadTs, row.MappingId, ct);

            if (result.Status == SlackPostStatus.Posted)
            {
                db.ProcessedComments.Add(new ProcessedComment
                {
                    MappingId = row.MappingId,
                    CommentId = row.CommentId,
                    VideoId = row.VideoId,
                    ProcessedAt = now,
                    SlackMessageTs = result.MessageTs,
                    ParentCommentId = row.ParentCommentId,
                });
                db.PendingDeliveries.Remove(row);
                delivered++;
            }
            else if (result.Status == SlackPostStatus.ChannelGone)
            {
                await DeactivateMappingAsync(row.MappingId, ct);
                deactivatedMappings.Add(row.MappingId);
                db.PendingDeliveries.Remove(row);
            }
            else // RetryableFailure
            {
                row.AttemptCount++;
                row.LastError = $"Slack post retry {row.AttemptCount} failed";
                if (row.AttemptCount >= _options.MaxAttempts)
                {
                    await audit.LogAsync(AuditLevel.Error, "Delivery",
                        $"Gave up delivering comment after {row.AttemptCount} attempts",
                        "ChannelMapping", row.MappingId.ToString(),
                        details: $"{{\"commentId\":\"{row.CommentId}\"}}", ct: ct);
                    db.PendingDeliveries.Remove(row);
                    deadLettered++;
                }
                else
                {
                    row.NextAttemptAt = now.Add(Backoff(row.AttemptCount));
                }
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Delivery retry: {Delivered} delivered, {DeadLettered} dead-lettered of {Due} due",
            delivered, deadLettered, due.Count);
    }

    /// <summary>Exponential backoff capped at <see cref="MaxBackoff"/>: base * 2^(attempt-1).</summary>
    private static TimeSpan Backoff(int attempt)
    {
        var factor = Math.Pow(2, Math.Max(0, attempt - 1));
        var ticks = BaseBackoff.Ticks * factor;
        return ticks >= MaxBackoff.Ticks ? MaxBackoff : TimeSpan.FromTicks((long)ticks);
    }

    private CommentNotification? DeserializePayload(PendingDelivery row)
    {
        try
        {
            return JsonSerializer.Deserialize<CommentNotification>(row.PayloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Unreadable pending delivery payload for mapping {MappingId}", row.MappingId);
            return null;
        }
    }

    private async Task<PostTarget?> ResolveTargetAsync(
        Guid mappingId, Dictionary<Guid, PostTarget?> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(mappingId, out var cached))
            return cached;

        var info = await db.ChannelMappings
            .AsNoTracking()
            .Where(m => m.Id == mappingId && m.IsActive)
            .Select(m => new { m.SlackChannel!.WorkspaceId, m.SlackChannel.SlackChannelId })
            .FirstOrDefaultAsync(ct);

        PostTarget? target = null;
        if (info is not null)
        {
            var botToken = await slackConnections.GetBotTokenAsync(info.WorkspaceId, ct);
            if (!string.IsNullOrEmpty(botToken))
                target = new PostTarget(botToken, info.SlackChannelId);
        }

        cache[mappingId] = target;
        return target;
    }

    private async Task<string?> ResolveThreadTsAsync(Guid mappingId, CommentNotification notification, CancellationToken ct)
    {
        if (!notification.IsReply || notification.ParentCommentId is null)
            return null;

        return await db.ProcessedComments
            .AsNoTracking()
            .Where(p => p.MappingId == mappingId && p.CommentId == notification.ParentCommentId)
            .Select(p => p.SlackMessageTs)
            .FirstOrDefaultAsync(ct);
    }

    private async Task DeactivateMappingAsync(Guid mappingId, CancellationToken ct)
    {
        var mapping = await db.ChannelMappings.FirstOrDefaultAsync(m => m.Id == mappingId, ct);
        if (mapping is not null && mapping.IsActive)
        {
            mapping.IsActive = false;
            mapping.LastError = PollCommentsJob.ChannelGoneError;
        }
        scheduler.Remove(mappingId);
        scheduler.RemoveReplySweep(mappingId);
        await audit.LogAsync(AuditLevel.Warning, "Delivery", PollCommentsJob.ChannelGoneError, "ChannelMapping", mappingId.ToString(), ct: ct);
        logger.LogWarning("Mapping {MappingId} deactivated during delivery retry: Slack channel gone", mappingId);
    }

    private sealed record PostTarget(string BotToken, string ChannelId);
}
