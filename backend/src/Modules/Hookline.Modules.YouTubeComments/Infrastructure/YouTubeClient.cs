using System.Text.RegularExpressions;

using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// <see cref="IYouTubeClient"/> backed by the Google.Apis.YouTube.v3 client library. Google's client
/// applies its own exponential backoff to transient 5xx/429 responses; <c>quotaExceeded</c> and
/// <c>commentsDisabled</c> bubble as <see cref="GoogleApiException"/> so the jobs can branch.
/// </summary>
public sealed partial class YouTubeClient(ILogger<YouTubeClient> logger) : IYouTubeClient
{
    private const string ApplicationName = "YouTubeComments";

    // channels.list / search.list / commentThreads.list / videos.list quota costs
    // (YouTube Data API v3 quota table — all reads are 1 unit except search.list at 100).
    private const int ChannelsListCost = 1;
    private const int SearchListCost = 100;
    private const int CommentThreadsListCost = 1;
    private const int VideosListCost = 1;

    // videos.list accepts up to 50 ids per call.
    private const int VideosBatchSize = 50;

    public async Task<(bool Ok, string? Error)> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API key is required.");

        using var service = CreateService(apiKey);

        // Cheapest representative read (1 quota unit): fetch one most-popular video.
        var request = service.Videos.List("id");
        request.Chart = VideosResource.ListRequest.ChartEnum.MostPopular;
        request.MaxResults = 1;

        try
        {
            await request.ExecuteAsync(ct);
            return (true, null);
        }
        catch (GoogleApiException ex)
        {
            // Surface the API-provided message (e.g. "API key not valid", "quota exceeded").
            var message = ex.Error?.Message ?? ex.Message;
            logger.LogWarning(ex, "YouTube API key validation failed: {Message}", message);
            return (false, message);
        }
    }

    /// <inheritdoc />
    public async Task<ChannelLookupResult> GetChannelAsync(string apiKey, string idOrHandleOrUrl, CancellationToken ct = default)
    {
        var input = idOrHandleOrUrl?.Trim() ?? string.Empty;
        if (input.Length == 0)
            return new ChannelLookupResult(null, 0);

        var query = ParseInput(input);
        using var service = CreateService(apiKey);

        // Track quota precisely as each request is issued so a mid-lookup failure still reports the
        // units already spent (the search.list fallback charges before the follow-up channels.list).
        var unitsUsed = 0;
        try
        {
            if (query.Kind == LookupKind.Search)
            {
                // No direct lookup for /c/CUSTOM URLs: search for the channel (100 units), then read
                // the matched id via channels.list (1 unit) for a precise, complete snippet.
                var search = service.Search.List("snippet");
                search.Q = query.Value;
                search.Type = "channel";
                search.MaxResults = 1;

                var searchResponse = await search.ExecuteAsync(ct);
                unitsUsed += SearchListCost;

                var matchedId = searchResponse.Items?.FirstOrDefault()?.Id?.ChannelId;
                if (string.IsNullOrEmpty(matchedId))
                    return new ChannelLookupResult(null, unitsUsed);

                var byId = service.Channels.List("snippet");
                byId.Id = matchedId;
                var byIdResponse = await byId.ExecuteAsync(ct);
                unitsUsed += ChannelsListCost;

                return new ChannelLookupResult(MapChannel(byIdResponse.Items?.FirstOrDefault()), unitsUsed);
            }

            var request = service.Channels.List("snippet");
            switch (query.Kind)
            {
                case LookupKind.Id:
                    request.Id = query.Value;
                    break;
                case LookupKind.Handle:
                    request.ForHandle = query.Value;
                    break;
                case LookupKind.Username:
                    request.ForUsername = query.Value;
                    break;
            }

            var response = await request.ExecuteAsync(ct);
            unitsUsed += ChannelsListCost;

            return new ChannelLookupResult(MapChannel(response.Items?.FirstOrDefault()), unitsUsed);
        }
        catch (GoogleApiException ex)
        {
            // Treat API failures (bad key, quota, invalid filter) as "not resolved": the service maps
            // a null channel to a 400. Report whatever quota was already consumed before the failure.
            var message = ex.Error?.Message ?? ex.Message;
            logger.LogWarning(ex, "YouTube channel lookup failed for '{Input}': {Message}", input, message);
            return new ChannelLookupResult(null, unitsUsed);
        }
    }

    /// <inheritdoc />
    public async Task<CommentFetchResult> GetRecentCommentsAsync(
        string apiKey, string youtubeChannelId, int maxResults = 50, CancellationToken ct = default)
    {
        using var service = CreateService(apiKey);

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
        string apiKey, IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var distinct = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (distinct.Count == 0)
            return new VideoTitlesResult(titles, 0);

        using var service = CreateService(apiKey);

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
        string apiKey, string youtubeChannelId, DateTimeOffset since, int maxPages, CancellationToken ct = default)
    {
        using var service = CreateService(apiKey);

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
        string apiKey, string parentCommentId, string parentVideoId, int maxPages, CancellationToken ct = default)
    {
        using var service = CreateService(apiKey);

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

    private static YouTubeService CreateService(string apiKey) => new(new BaseClientService.Initializer
    {
        ApiKey = apiKey,
        ApplicationName = ApplicationName,
    });

    /// <summary>Maps a channel resource snippet to <see cref="YouTubeChannelInfo"/>, or <c>null</c>.</summary>
    private static YouTubeChannelInfo? MapChannel(Channel? channel)
    {
        var snippet = channel?.Snippet;
        if (channel is null || snippet is null || string.IsNullOrEmpty(channel.Id))
            return null;

        return new YouTubeChannelInfo(
            channel.Id,
            snippet.Title ?? string.Empty,
            BestThumbnail(snippet.Thumbnails),
            snippet.CustomUrl);
    }

    /// <summary>Picks the highest-resolution thumbnail URL available (high ?? medium ?? default).</summary>
    private static string? BestThumbnail(ThumbnailDetails? thumbnails)
    {
        if (thumbnails is null)
            return null;

        return thumbnails.High?.Url
            ?? thumbnails.Medium?.Url
            ?? thumbnails.Default__?.Url;
    }

    /// <summary>Classifies the raw input into a lookup strategy + the token to query with.</summary>
    private static LookupQuery ParseInput(string input)
    {
        // A bare channel id (UC + 22 chars): direct id lookup.
        if (ChannelIdRegex().IsMatch(input))
            return new LookupQuery(LookupKind.Id, input);

        // A bare @handle (no scheme): handle lookup. Google accepts the leading '@'.
        if (input.StartsWith('@'))
            return new LookupQuery(LookupKind.Handle, input);

        // youtube.com URLs. Match the path segment that identifies the channel.
        if (TryParsePath(input, out var segments))
        {
            // /channel/UC...
            if (segments.Length >= 2 && segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase))
                return new LookupQuery(LookupKind.Id, segments[1]);

            // /user/USERNAME
            if (segments.Length >= 2 && segments[0].Equals("user", StringComparison.OrdinalIgnoreCase))
                return new LookupQuery(LookupKind.Username, segments[1]);

            // /c/CUSTOM (legacy custom URL) -> no direct lookup, fall back to search.
            if (segments.Length >= 2 && segments[0].Equals("c", StringComparison.OrdinalIgnoreCase))
                return new LookupQuery(LookupKind.Search, segments[1]);

            // /@handle
            if (segments.Length >= 1 && segments[0].StartsWith('@'))
                return new LookupQuery(LookupKind.Handle, segments[0]);
        }

        // Fallback: treat the whole input as a handle (covers a bare custom name typed without '@').
        return new LookupQuery(LookupKind.Handle, input.StartsWith('@') ? input : "@" + input);
    }

    /// <summary>Splits the path of a (possibly scheme-less) youtube.com URL into its segments.</summary>
    private static bool TryParsePath(string input, out string[] segments)
    {
        segments = Array.Empty<string>();

        var candidate = input;
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "https://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        var isYouTube = host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        if (!isYouTube)
            return false;

        segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0;
    }

    private enum LookupKind { Id, Handle, Username, Search }

    private readonly record struct LookupQuery(LookupKind Kind, string Value);

    [GeneratedRegex(@"^UC[0-9A-Za-z_-]{22}$")]
    private static partial Regex ChannelIdRegex();
}
