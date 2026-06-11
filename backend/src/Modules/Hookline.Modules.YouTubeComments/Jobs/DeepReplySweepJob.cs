using System.Text.Json;

using Google;

using Hangfire;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Jobs;

/// <summary>
/// The deep reply sweep for one mapping: pages through the channel's comment threads within the
/// configured window and forwards any reply not yet delivered — including replies on older comments
/// the normal poll can't see and replies beyond the few the API returns inline (a comments.list fetch
/// is issued only when a thread's total reply count exceeds what we already have). Replies thread under
/// their parent's Slack message; dedup keeps it idempotent so it overlaps the normal poll harmlessly.
/// </summary>
public sealed class DeepReplySweepJob(
    YouTubeCommentsDbContext db,
    IYouTubeClient youtube,
    ISlackClient slack,
    IYouTubeApiKeyProvider keys,
    IPollingScheduler scheduler,
    ISlackConnections slackConnections,
    CommentModerationService moderation,
    ICommentsAudit audit,
    ILogger<DeepReplySweepJob> logger)
{
    // Page caps bound the worst-case quota a single sweep can spend.
    private const int MaxScanPages = 30;   // up to ~3000 threads scanned per sweep
    private const int MaxReplyPages = 10;  // up to ~1000 replies fetched per popular comment
    private const int ScanBudget = MaxScanPages;

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(Guid mappingId, CancellationToken ct)
    {
        var mapping = await db.ChannelMappings
            .Include(m => m.YouTubeChannel)
            .Include(m => m.SlackChannel)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct);

        if (mapping is null || !mapping.IsActive || !mapping.IncludeReplies ||
            mapping.ReplySweepFrequency == ReplyScanFrequency.Off)
        {
            logger.LogDebug("Reply sweep {MappingId} skipped: disabled/inactive", mappingId);
            return;
        }

        var ytChannel = mapping.YouTubeChannel;
        var slackChannel = mapping.SlackChannel;
        if (ytChannel is null || slackChannel is null)
        {
            logger.LogWarning("Reply sweep {MappingId} has incomplete navigation data; skipping", mappingId);
            return;
        }

        var botToken = await slackConnections.GetBotTokenAsync(slackChannel.WorkspaceId, ct);
        if (string.IsNullOrEmpty(botToken))
        {
            logger.LogWarning("Reply sweep {MappingId} Slack workspace disconnected; skipping", mappingId);
            return;
        }

        var lease = await keys.AcquireAsync(ScanBudget, ct);
        if (lease is null)
        {
            await audit.LogAsync(AuditLevel.Warning, "ReplySweep", "No API key with available quota", "ChannelMapping", mappingId.ToString(), ct: ct);
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now.AddDays(-mapping.ReplyWindowDays);
            // Never look back past the mapping's watermark.
            var since = windowStart > mapping.CommentsSinceUtc ? windowStart : mapping.CommentsSinceUtc;

            var fetch = await youtube.GetCommentThreadsSinceAsync(lease.ApiKey, ytChannel.YouTubeChannelId, since, MaxScanPages, ct);
            var unitsUsed = fetch.UnitsUsed;

            if (fetch.Threads.Count == 0)
            {
                await keys.RecordUsageAsync(lease.Id, unitsUsed, ct);
                return;
            }

            var parentIds = fetch.Threads.Select(t => t.TopLevel.CommentId).ToList();

            // Replies already delivered or queued, for these parents — dedup + per-parent counts.
            var processed = await db.ProcessedComments
                .AsNoTracking()
                .Where(p => p.MappingId == mappingId && p.ParentCommentId != null && parentIds.Contains(p.ParentCommentId))
                .Select(p => new { p.CommentId, p.ParentCommentId })
                .ToListAsync(ct);

            var skip = processed.Select(x => x.CommentId).ToHashSet(StringComparer.Ordinal);
            skip.UnionWith(await db.PendingDeliveries
                .AsNoTracking()
                .Where(p => p.MappingId == mappingId && p.ParentCommentId != null && parentIds.Contains(p.ParentCommentId))
                .Select(p => p.CommentId)
                .ToListAsync(ct));

            var postedCountByParent = processed
                .GroupBy(x => x.ParentCommentId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            // Collect undelivered replies, fetching deeper only when inline can't already cover them.
            var toPost = new List<YouTubeComment>();
            foreach (var thread in fetch.Threads)
            {
                var parent = thread.TopLevel.CommentId;
                IReadOnlyList<YouTubeComment> replies = thread.InlineReplies;

                var known = postedCountByParent.GetValueOrDefault(parent);
                if (thread.TotalReplyCount > thread.InlineReplies.Count && thread.TotalReplyCount > known)
                {
                    var deep = await youtube.GetRepliesAsync(lease.ApiKey, parent, thread.TopLevel.VideoId, MaxReplyPages, ct);
                    unitsUsed += deep.UnitsUsed;
                    replies = deep.Replies;
                }

                foreach (var reply in replies)
                {
                    if (reply.PublishedAt > mapping.CommentsSinceUtc && skip.Add(reply.CommentId))
                        toPost.Add(reply);
                }
            }

            var postedCount = 0;
            var queuedCount = 0;
            var channelGone = false;

            if (toPost.Count > 0)
            {
                var titles = await youtube.GetVideoTitlesAsync(
                    lease.ApiKey, toPost.Select(r => r.VideoId).Where(v => !string.IsNullOrEmpty(v)), ct);
                unitsUsed += titles.UnitsUsed;

                // Parent → Slack ts, so replies thread under the original comment's message.
                var neededParents = toPost.Select(r => r.ParentCommentId!).Distinct().ToList();
                var tsMap = (await db.ProcessedComments
                        .AsNoTracking()
                        .Where(p => p.MappingId == mappingId && neededParents.Contains(p.CommentId) && p.SlackMessageTs != null)
                        .Select(p => new { p.CommentId, p.SlackMessageTs })
                        .ToListAsync(ct))
                    .ToDictionary(x => x.CommentId, x => x.SlackMessageTs!, StringComparer.Ordinal);

                // Force-ssl capability for the owning channel — gates the Reject button vs. the proactive
                // re-consent link. Resolved once per sweep, not per reply.
                var canModerate = await moderation.CanModerateAsync(ytChannel.YouTubeChannelId, ct);

                foreach (var reply in toPost.OrderBy(r => r.PublishedAt))
                {
                    var title = titles.Titles.TryGetValue(reply.VideoId, out var t) ? t : reply.VideoId;
                    var notification = PollCommentsJob.ToNotification(reply, title);
                    string? threadTs = reply.ParentCommentId is { } pid && tsMap.TryGetValue(pid, out var ts) ? ts : null;

                    var result = await slack.PostCommentAsync(botToken, slackChannel.SlackChannelId, notification, threadTs, mappingId, canModerate, ct);

                    if (result.Status == SlackPostStatus.Posted)
                    {
                        db.ProcessedComments.Add(new ProcessedComment
                        {
                            MappingId = mappingId,
                            CommentId = reply.CommentId,
                            VideoId = reply.VideoId,
                            ProcessedAt = now,
                            SlackMessageTs = result.MessageTs,
                            ParentCommentId = reply.ParentCommentId,
                        });
                        postedCount++;
                    }
                    else if (result.Status == SlackPostStatus.ChannelGone)
                    {
                        channelGone = true;
                        break;
                    }
                    else
                    {
                        db.PendingDeliveries.Add(new PendingDelivery
                        {
                            MappingId = mappingId,
                            CommentId = reply.CommentId,
                            ParentCommentId = reply.ParentCommentId,
                            VideoId = reply.VideoId,
                            PayloadJson = JsonSerializer.Serialize(notification),
                            AttemptCount = 0,
                            NextAttemptAt = now.Add(InitialRetryDelay),
                            CreatedAt = now,
                        });
                        queuedCount++;
                    }
                }
            }

            await keys.RecordUsageAsync(lease.Id, unitsUsed, ct);
            if (channelGone)
            {
                mapping.IsActive = false;
                mapping.LastError = PollCommentsJob.ChannelGoneError;
            }
            await db.SaveChangesAsync(ct);

            if (channelGone)
            {
                scheduler.Remove(mappingId);
                scheduler.RemoveReplySweep(mappingId);
                await audit.LogAsync(AuditLevel.Warning, "ReplySweep", PollCommentsJob.ChannelGoneError, "ChannelMapping", mappingId.ToString(), ct: ct);
            }

            await audit.LogAsync(
                AuditLevel.Information, "ReplySweep",
                $"Reply sweep {ytChannel.Title}: {postedCount} posted, {queuedCount} queued",
                "ChannelMapping", mappingId.ToString(),
                details: $"{{\"threads\":{fetch.Threads.Count},\"posted\":{postedCount},\"queued\":{queuedCount},\"unitsUsed\":{unitsUsed}}}", ct: ct);

            logger.LogInformation(
                "Reply sweep {MappingId} ({Channel}): {Posted} posted, {Queued} queued, {Threads} threads, {Units}u",
                mappingId, ytChannel.Title, postedCount, queuedCount, fetch.Threads.Count, unitsUsed);
        }
        catch (GoogleApiException ex) when (ex.HasReason("quotaExceeded"))
        {
            await keys.MarkExhaustedAsync(lease.Id, ct);
            await audit.LogAsync(AuditLevel.Warning, "Quota",
                $"Quota exhausted on key '{lease.Name}' during reply sweep", "ChannelMapping", mappingId.ToString(), ct: ct);
            logger.LogWarning(ex, "Quota exceeded during reply sweep {MappingId}", mappingId);
        }
        catch (GoogleApiException ex) when (ex.IsKeyInvalid())
        {
            // Dead key — disable it so it leaves the rotation pool; the next tick rotates to another key.
            await keys.MarkInvalidAsync(lease.Id, ct);
            var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason ?? "invalid";
            await audit.LogAsync(AuditLevel.Warning, "ApiKey",
                $"API key '{lease.Name}' disabled — YouTube rejected it ({reason})",
                "ChannelMapping", mappingId.ToString(), details: ex.Message, ct: ct);
            logger.LogWarning(ex, "API key {Key} disabled during reply sweep {MappingId}: {Reason}", lease.Name, mappingId, reason);
        }
        catch (GoogleApiException ex) when (ex.HasReason("commentsDisabled") || (int)ex.HttpStatusCode == 403)
        {
            await keys.RecordUsageAsync(lease.Id, 1, ct);
            await audit.LogAsync(AuditLevel.Warning, "ReplySweep",
                "Comments unavailable for this channel (disabled or forbidden)",
                "ChannelMapping", mappingId.ToString(), details: ex.Message, ct: ct);
            logger.LogWarning(ex, "Comments unavailable during reply sweep {MappingId}", mappingId);
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditLevel.Error, "ReplySweep", ex.Message, "ChannelMapping", mappingId.ToString(), ct: ct);
            logger.LogError(ex, "Reply sweep failed for mapping {MappingId}", mappingId);
        }
    }
}
