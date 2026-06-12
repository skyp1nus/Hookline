using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// <see cref="IYouTubeClient"/> backed by the Google.Apis.YouTube.v3 client library. Each service is
/// built per call (see <see cref="CreateService"/>) from a short-lived OAuth access token
/// (<c>youtube.force-ssl</c>) and configured with an exponential back-off handler that retries
/// transient 5xx/429 responses in-call; <c>quotaExceeded</c> (403) is excluded so it bubbles as a
/// <see cref="GoogleApiException"/> for the job to surface honestly, as do <c>commentsDisabled</c> and
/// other non-transient reasons. There is no circuit breaker — the durable retry queue and per-tick
/// scheduling absorb sustained outages.
/// </summary>
public sealed class YouTubeClient : IYouTubeClient
{
    private const string ApplicationName = "YouTubeComments";

    // commentThreads.list / videos.list quota costs (YouTube Data API v3 quota table — both 1 unit).
    private const int CommentThreadsListCost = 1;
    private const int VideosListCost = 1;

    // videos.list accepts up to 50 ids per call.
    private const int VideosBatchSize = 50;

    /// <inheritdoc />
    public async Task<CommentFetchResult> GetRecentCommentsAsync(
        string accessToken, string youtubeChannelId, int maxResults = 50, CancellationToken ct = default)
    {
        using var service = CreateService(accessToken);

        // part=snippet,replies: commentThreads.list is 1 unit regardless of parts, so the inline
        // replies (the few most recent the API ships per thread) come for free.
        var request = service.CommentThreads.List("snippet,replies");
        request.AllThreadsRelatedToChannelId = youtubeChannelId;
        request.Order = CommentThreadsResource.ListRequest.OrderEnum.Time; // newest first
        request.MaxResults = Math.Clamp(maxResults, 1, 100);
        request.TextFormat = CommentThreadsResource.ListRequest.TextFormatEnum.PlainText;

        // Let GoogleApiException bubble (quotaExceeded / commentsDisabled) so the job can branch.
        var response = await request.ExecuteAsync(ct);

        var comments = new List<YouTubeComment>(response.Items?.Count ?? 0);
        foreach (var thread in response.Items ?? Enumerable.Empty<CommentThread>())
        {
            var topLevel = MapComment(thread);
            if (topLevel is null)
                continue;

            comments.Add(topLevel);

            // Flatten any inline replies, tagged with their parent so the caller can thread them.
            foreach (var reply in thread.Replies?.Comments ?? Enumerable.Empty<Comment>())
            {
                var mappedReply = MapReply(reply, topLevel.CommentId, topLevel.VideoId);
                if (mappedReply is not null)
                    comments.Add(mappedReply);
            }
        }

        return new CommentFetchResult(comments, CommentThreadsListCost);
    }

    /// <inheritdoc />
    public async Task<VideoTitlesResult> GetVideoTitlesAsync(
        string accessToken, IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var distinct = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (distinct.Count == 0)
            return new VideoTitlesResult(titles, 0);

        using var service = CreateService(accessToken);

        var unitsUsed = 0;
        for (var offset = 0; offset < distinct.Count; offset += VideosBatchSize)
        {
            var batch = distinct.Skip(offset).Take(VideosBatchSize).ToList();

            var request = service.Videos.List("snippet");
            request.Id = string.Join(',', batch);
            request.MaxResults = VideosBatchSize;

            var response = await request.ExecuteAsync(ct);
            unitsUsed += VideosListCost;

            foreach (var video in response.Items ?? Enumerable.Empty<Video>())
            {
                if (!string.IsNullOrEmpty(video.Id) && video.Snippet?.Title is { } title)
                    titles[video.Id] = title;
            }
        }

        return new VideoTitlesResult(titles, unitsUsed);
    }

    /// <inheritdoc />
    public async Task<ThreadFetchResult> GetCommentThreadsSinceAsync(
        string accessToken, string youtubeChannelId, DateTimeOffset since, int maxPages, CancellationToken ct = default)
    {
        using var service = CreateService(accessToken);

        var threads = new List<YouTubeThread>();
        string? pageToken = null;
        var pages = 0;

        do
        {
            var request = service.CommentThreads.List("snippet,replies");
            request.AllThreadsRelatedToChannelId = youtubeChannelId;
            request.Order = CommentThreadsResource.ListRequest.OrderEnum.Time; // newest first
            request.MaxResults = 100;
            request.TextFormat = CommentThreadsResource.ListRequest.TextFormatEnum.PlainText;
            request.PageToken = pageToken;

            // Let GoogleApiException bubble (quotaExceeded / commentsDisabled) so the job can branch.
            var response = await request.ExecuteAsync(ct);
            pages++;

            var reachedOlder = false;
            foreach (var thread in response.Items ?? Enumerable.Empty<CommentThread>())
            {
                var topLevel = MapComment(thread);
                if (topLevel is null)
                    continue;

                // order=time is newest-first: once we pass the window edge, every remaining thread is
                // older too, so finish this page then stop paging.
                if (topLevel.PublishedAt < since)
                {
                    reachedOlder = true;
                    continue;
                }

                var inlineReplies = new List<YouTubeComment>();
                foreach (var reply in thread.Replies?.Comments ?? Enumerable.Empty<Comment>())
                {
                    var mapped = MapReply(reply, topLevel.CommentId, topLevel.VideoId);
                    if (mapped is not null)
                        inlineReplies.Add(mapped);
                }

                threads.Add(new YouTubeThread(topLevel, inlineReplies, thread.Snippet?.TotalReplyCount ?? 0));
            }

            pageToken = response.NextPageToken;
            if (reachedOlder)
                break;
        }
        while (!string.IsNullOrEmpty(pageToken) && pages < maxPages);

        return new ThreadFetchResult(threads, pages);
    }

    /// <inheritdoc />
    public async Task<RepliesResult> GetRepliesAsync(
        string accessToken, string parentCommentId, string parentVideoId, int maxPages, CancellationToken ct = default)
    {
        using var service = CreateService(accessToken);

        var replies = new List<YouTubeComment>();
        string? pageToken = null;
        var pages = 0;

        do
        {
            var request = service.Comments.List("snippet");
            request.ParentId = parentCommentId;
            request.MaxResults = 100;
            request.TextFormat = CommentsResource.ListRequest.TextFormatEnum.PlainText;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync(ct);
            pages++;

            foreach (var reply in response.Items ?? Enumerable.Empty<Comment>())
            {
                var mapped = MapReply(reply, parentCommentId, parentVideoId);
                if (mapped is not null)
                    replies.Add(mapped);
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && pages < maxPages);

        return new RepliesResult(replies, pages);
    }

    /// <summary>Maps a comment thread's top-level comment to <see cref="YouTubeComment"/>, or <c>null</c> when incomplete.</summary>
    private static YouTubeComment? MapComment(CommentThread? thread)
    {
        var snippet = thread?.Snippet?.TopLevelComment?.Snippet;
        var commentId = thread?.Snippet?.TopLevelComment?.Id;
        if (snippet is null || string.IsNullOrEmpty(commentId))
            return null;

        // videoId lives on the thread snippet; the comment snippet also carries it as a fallback.
        var videoId = thread!.Snippet!.VideoId ?? snippet.VideoId ?? string.Empty;

        return new YouTubeComment(
            CommentId: commentId,
            VideoId: videoId,
            AuthorName: snippet.AuthorDisplayName ?? string.Empty,
            AuthorChannelUrl: snippet.AuthorChannelUrl,
            AuthorImageUrl: snippet.AuthorProfileImageUrl,
            Text: snippet.TextDisplay ?? string.Empty,
            LikeCount: snippet.LikeCount ?? 0,
            PublishedAt: snippet.PublishedAtDateTimeOffset ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Maps a reply <see cref="Comment"/> to a <see cref="YouTubeComment"/> flagged as a reply, or <c>null</c> when incomplete.</summary>
    private static YouTubeComment? MapReply(Comment? reply, string parentCommentId, string parentVideoId)
    {
        var snippet = reply?.Snippet;
        if (reply is null || snippet is null || string.IsNullOrEmpty(reply.Id))
            return null;

        return new YouTubeComment(
            CommentId: reply.Id,
            VideoId: string.IsNullOrEmpty(snippet.VideoId) ? parentVideoId : snippet.VideoId,
            AuthorName: snippet.AuthorDisplayName ?? string.Empty,
            AuthorChannelUrl: snippet.AuthorChannelUrl,
            AuthorImageUrl: snippet.AuthorProfileImageUrl,
            Text: snippet.TextDisplay ?? string.Empty,
            LikeCount: snippet.LikeCount ?? 0,
            PublishedAt: snippet.PublishedAtDateTimeOffset ?? DateTimeOffset.UtcNow,
            IsReply: true,
            ParentCommentId: parentCommentId);
    }

    private static YouTubeService CreateService(string accessToken)
    {
        // Build the per-call service from the OAuth access token (force-ssl), exactly as the moderation
        // write client does — monitoring now runs on the same owner credential, not an API key.
        var credential = GoogleCredential.FromAccessToken(accessToken);
        var service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });

        // Lightweight in-call resilience: retry transient 5xx/429 with exponential back-off (up to 3
        // extra attempts, ~1s/2s/4s). quotaExceeded (403) is not transient, so it is left to bubble and
        // the job surfaces an honest "quota exhausted" state instead of burning more quota.
        var backOff = new BackOffHandler(new BackOffHandler.Initializer(new ExponentialBackOff(TimeSpan.FromSeconds(1), 3))
        {
            HandleUnsuccessfulResponseFunc = response =>
                GoogleApiExceptionExtensions.IsTransientStatus(response.StatusCode),
        });
        service.HttpClient.MessageHandler.AddUnsuccessfulResponseHandler(backOff);

        return service;
    }
}
