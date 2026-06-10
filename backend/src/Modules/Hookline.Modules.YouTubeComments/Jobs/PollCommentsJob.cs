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
/// The recurring polling job for a single channel mapping: fetch recent YouTube comments (and, when
/// the mapping opts in, their replies), skip ones already delivered or queued, post the new ones to
/// Slack (oldest-first, replies threaded under their parent), and record dedup + quota + audit. A
/// transient Slack failure is parked in <c>pending_deliveries</c> for the retry job; a permanently
/// gone Slack channel deactivates the mapping. The Slack bot token is resolved from the shared
/// Connections store by the mapping's Slack-channel workspace id.
/// </summary>
public sealed class PollCommentsJob(
    YouTubeCommentsDbContext db,
    IYouTubeClient youtube,
    ISlackClient slack,
    IYouTubeApiKeyProvider keys,
    IPollingScheduler scheduler,
    ISlackConnections slackConnections,
    ICommentsAudit audit,
    ILogger<PollCommentsJob> logger)
{
    // Each poll fetches comments (1u) and may resolve video titles (>=1u). Acquire a small budget up
    // front so AcquireAsync rejects keys that are essentially out of quota.
    private const int QuotaBudget = 2;
    private const int MaxComments = 50;

    // First retry of a parked delivery; the retry job backs off exponentially from here.
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);

    internal const string ChannelGoneError = "Slack channel archived or bot removed; mapping deactivated";

    /// <summary>
    /// Runs one poll for <paramref name="mappingId"/>.
    /// <para><see cref="DisableConcurrentExecutionAttribute"/> takes a per-job distributed lock so two
    /// runs of the same mapping can never overlap (a slow poll won't be lapped by the next tick, which
    /// would otherwise double-post). Failures are swallowed (logged + audited) rather than rethrown:
    /// a poll is fully retried on its next scheduled tick, so <see cref="AutomaticRetryAttribute"/> is
    /// pinned to 0 attempts to avoid Hangfire piling on extra runs.</para>
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(Guid mappingId, CancellationToken ct)
    {
        // 1. Load the mapping with everything we need to poll + post.
        var mapping = await db.ChannelMappings
            .Include(m => m.YouTubeChannel)
            .Include(m => m.SlackChannel)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct);

        if (mapping is null || !mapping.IsActive)
        {
            logger.LogDebug("Mapping {MappingId} missing or inactive; skipping poll", mappingId);
            return;
        }

        var ytChannel = mapping.YouTubeChannel;
        var slackChannel = mapping.SlackChannel;
        if (ytChannel is null || slackChannel is null)
        {
            logger.LogWarning("Mapping {MappingId} has incomplete navigation data; skipping", mappingId);
            return;
        }

        // Resolve the workspace bot token from the shared Connections store.
        var botToken = await slackConnections.GetBotTokenAsync(slackChannel.WorkspaceId, ct);
        if (string.IsNullOrEmpty(botToken))
        {
            logger.LogWarning("Mapping {MappingId} Slack workspace disconnected; skipping poll", mappingId);
            await SetLastErrorAsync(mapping, "Slack workspace disconnected", ct);
            return;
        }

        var channelLabel = ytChannel.Title;

        // 2. Acquire an API key with enough remaining quota.
        var lease = await keys.AcquireAsync(QuotaBudget, ct);
        if (lease is null)
        {
            const string msg = "No API key with available quota";
            await audit.LogAsync("Warning", "Polling", msg, "ChannelMapping", mappingId.ToString(), ct: ct);
            mapping.LastError = msg;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Poll for {MappingId} skipped: {Message}", mappingId, msg);
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            // 3. Fetch the most recent comment threads (incl. inline replies) for the channel.
            var fetch = await youtube.GetRecentCommentsAsync(lease.ApiKey, ytChannel.YouTubeChannelId, MaxComments, ct);
            var unitsUsed = fetch.UnitsUsed;

            // 4. Narrow to candidates: drop replies unless opted in, and anything at/under the watermark.
            var candidates = fetch.Comments
                .Where(c => mapping.IncludeReplies || !c.IsReply)
                .Where(c => c.PublishedAt > mapping.CommentsSinceUtc)
                .ToList();

            var candidateIds = candidates.Select(c => c.CommentId).ToList();

            // 5. Exclude comments already delivered OR already parked for retry.
            var skip = new HashSet<string>(StringComparer.Ordinal);
            if (candidateIds.Count > 0)
            {
                skip.UnionWith(await db.ProcessedComments
                    .AsNoTracking()
                    .Where(p => p.MappingId == mappingId && candidateIds.Contains(p.CommentId))
                    .Select(p => p.CommentId)
                    .ToListAsync(ct));
                skip.UnionWith(await db.PendingDeliveries
                    .AsNoTracking()
                    .Where(p => p.MappingId == mappingId && candidateIds.Contains(p.CommentId))
                    .Select(p => p.CommentId)
                    .ToListAsync(ct));
            }

            var newComments = candidates
                .Where(c => !skip.Contains(c.CommentId))
                .OrderBy(c => c.PublishedAt) // deliver oldest-first so Slack ordering is chronological
                .ToList();

            var postedCount = 0;
            var queuedCount = 0;
            var channelGone = false;

            // 6. Resolve titles + post each new comment.
            if (newComments.Count > 0)
            {
                var videoIds = newComments.Select(c => c.VideoId).Where(v => !string.IsNullOrEmpty(v));
                var titlesResult = await youtube.GetVideoTitlesAsync(lease.ApiKey, videoIds, ct);
                unitsUsed += titlesResult.UnitsUsed;

                // Slack ts of top-level comments posted in THIS run, so a same-run reply threads under
                // its parent without a database round-trip.
                var tsThisRun = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var comment in newComments)
                {
                    var title = titlesResult.Titles.TryGetValue(comment.VideoId, out var t) ? t : comment.VideoId;
                    var notification = ToNotification(comment, title);
                    var threadTs = await ResolveThreadTsAsync(mappingId, comment, tsThisRun, ct);

                    var result = await slack.PostCommentAsync(botToken, slackChannel.SlackChannelId, notification, threadTs, ct);

                    if (result.Status == SlackPostStatus.Posted)
                    {
                        db.ProcessedComments.Add(new ProcessedComment
                        {
                            MappingId = mappingId,
                            CommentId = comment.CommentId,
                            VideoId = comment.VideoId,
                            ProcessedAt = now,
                            SlackMessageTs = result.MessageTs,
                            ParentCommentId = comment.ParentCommentId,
                        });
                        if (!comment.IsReply && result.MessageTs is not null)
                            tsThisRun[comment.CommentId] = result.MessageTs;
                        postedCount++;
                    }
                    else if (result.Status == SlackPostStatus.ChannelGone)
                    {
                        channelGone = true;
                        break; // stop; the channel is unusable
                    }
                    else // RetryableFailure: park it so it survives the fetch window.
                    {
                        db.PendingDeliveries.Add(new PendingDelivery
                        {
                            MappingId = mappingId,
                            CommentId = comment.CommentId,
                            ParentCommentId = comment.ParentCommentId,
                            VideoId = comment.VideoId,
                            PayloadJson = JsonSerializer.Serialize(notification),
                            AttemptCount = 0,
                            NextAttemptAt = now.Add(InitialRetryDelay),
                            CreatedAt = now,
                        });
                        queuedCount++;
                    }
                }
            }

            // 7. Record quota + finalize mapping state.
            await keys.RecordUsageAsync(lease.Id, unitsUsed, ct);
            mapping.LastPolledAt = now;
            if (channelGone)
            {
                mapping.IsActive = false;
                mapping.LastError = ChannelGoneError;
            }
            else
            {
                mapping.LastError = null;
            }
            await db.SaveChangesAsync(ct);

            if (channelGone)
            {
                scheduler.Remove(mappingId);
                scheduler.RemoveReplySweep(mappingId);
                await audit.LogAsync("Warning", "Polling", ChannelGoneError, "ChannelMapping", mappingId.ToString(), ct: ct);
                logger.LogWarning("Mapping {MappingId} deactivated: Slack channel gone", mappingId);
            }

            await audit.LogAsync(
                "Information", "Polling",
                $"Polled {channelLabel}: {postedCount} posted, {queuedCount} queued",
                "ChannelMapping", mappingId.ToString(),
                details: $"{{\"fetched\":{fetch.Comments.Count},\"new\":{newComments.Count},\"posted\":{postedCount}," +
                         $"\"queued\":{queuedCount},\"unitsUsed\":{unitsUsed}}}", ct: ct);

            logger.LogInformation(
                "Polled mapping {MappingId} ({Channel}): {Posted} posted, {Queued} queued of {New} new ({Fetched} fetched), {Units}u",
                mappingId, channelLabel, postedCount, queuedCount, newComments.Count, fetch.Comments.Count, unitsUsed);
        }
        catch (GoogleApiException ex) when (ex.HasReason("quotaExceeded"))
        {
            // The key is out of quota server-side. Pin it exhausted so the next tick rotates. Don't crash.
            await keys.MarkExhaustedAsync(lease.Id, ct);
            var msg = $"Quota exhausted on key '{lease.Name}'; will rotate on next run";
            await audit.LogAsync("Warning", "Quota", msg, "ChannelMapping", mappingId.ToString(), ct: ct);
            await SetLastErrorAsync(mapping, msg, ct);
            logger.LogWarning(ex, "Quota exceeded for mapping {MappingId} on key {Key}", mappingId, lease.Name);
        }
        catch (GoogleApiException ex) when (ex.IsKeyInvalid())
        {
            // The key itself is dead (revoked/invalid/expired/restricted, or API not enabled). Disable it
            // so it leaves the rotation pool instead of failing every tick; the next tick rotates to
            // another key. An admin re-enables it after fixing the credential.
            await keys.MarkInvalidAsync(lease.Id, ct);
            var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason ?? "invalid";
            var msg = $"API key '{lease.Name}' disabled — YouTube rejected it ({reason})";
            await audit.LogAsync("Warning", "ApiKey", msg, "ChannelMapping", mappingId.ToString(), details: ex.Message, ct: ct);
            await SetLastErrorAsync(mapping, msg, ct);
            logger.LogWarning(ex, "API key {Key} disabled for mapping {MappingId}: {Reason}", lease.Name, mappingId, reason);
        }
        catch (GoogleApiException ex) when (ex.HasReason("commentsDisabled") || (int)ex.HttpStatusCode == 403)
        {
            await keys.RecordUsageAsync(lease.Id, 1, ct);
            var msg = "Comments unavailable for this channel (disabled or forbidden)";
            await audit.LogAsync("Warning", "Polling", msg, "ChannelMapping", mappingId.ToString(), details: ex.Message, ct: ct);
            await SetLastErrorAsync(mapping, msg, ct);
            logger.LogWarning(ex, "Comments unavailable for mapping {MappingId}", mappingId);
        }
        catch (Exception ex)
        {
            // Anything else: log + audit + stamp LastError, then swallow. The next scheduled tick is a
            // natural retry; rethrowing would trigger Hangfire's retry queue and risk noise.
            await audit.LogAsync("Error", "Polling", ex.Message, "ChannelMapping", mappingId.ToString(), ct: ct);
            await SetLastErrorAsync(mapping, ex.Message, ct);
            logger.LogError(ex, "Poll failed for mapping {MappingId}", mappingId);
        }
    }

    private async Task<string?> ResolveThreadTsAsync(
        Guid mappingId, YouTubeComment comment, IReadOnlyDictionary<string, string> tsThisRun, CancellationToken ct)
    {
        if (!comment.IsReply || comment.ParentCommentId is null)
            return null;

        if (tsThisRun.TryGetValue(comment.ParentCommentId, out var ts))
            return ts;

        return await db.ProcessedComments
            .AsNoTracking()
            .Where(p => p.MappingId == mappingId && p.CommentId == comment.ParentCommentId)
            .Select(p => p.SlackMessageTs)
            .FirstOrDefaultAsync(ct);
    }

    internal static CommentNotification ToNotification(YouTubeComment c, string title) => new(
        AuthorName: c.AuthorName,
        AuthorChannelUrl: c.AuthorChannelUrl,
        AuthorImageUrl: c.AuthorImageUrl,
        VideoTitle: title,
        VideoId: c.VideoId,
        CommentText: c.Text,
        LikeCount: c.LikeCount,
        PublishedAt: c.PublishedAt,
        CommentId: c.CommentId,
        IsReply: c.IsReply,
        ParentCommentId: c.ParentCommentId);

    private async Task SetLastErrorAsync(ChannelMapping mapping, string error, CancellationToken ct)
    {
        try
        {
            mapping.LastError = error;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist LastError for mapping {MappingId}", mapping.Id);
        }
    }
}
