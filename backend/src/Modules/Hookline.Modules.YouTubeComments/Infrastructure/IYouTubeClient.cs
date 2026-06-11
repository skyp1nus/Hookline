namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>Resolved metadata for a YouTube channel.</summary>
public sealed record YouTubeChannelInfo(string ChannelId, string Title, string? ThumbnailUrl, string? Handle);

/// <summary>
/// The outcome of a channel lookup: the resolved <see cref="Channel"/> (or <c>null</c> when no
/// channel matched the input) plus the number of quota <see cref="UnitsUsed"/> the lookup consumed.
/// </summary>
public sealed record ChannelLookupResult(YouTubeChannelInfo? Channel, int UnitsUsed);

/// <summary>A single YouTube comment fetched for the polling pipeline (top-level or a reply).</summary>
public sealed record YouTubeComment(
    string CommentId,
    string VideoId,
    string AuthorName,
    string? AuthorChannelUrl,
    string? AuthorImageUrl,
    string Text,
    long LikeCount,
    DateTimeOffset PublishedAt,
    bool IsReply = false,
    string? ParentCommentId = null);

/// <summary>The comments returned by a poll plus the quota <see cref="UnitsUsed"/> the fetch consumed.</summary>
public sealed record CommentFetchResult(IReadOnlyList<YouTubeComment> Comments, int UnitsUsed);

/// <summary>
/// A comment thread for the deep reply sweep: the top-level comment, the (few) inline replies the
/// API returned for free, and the authoritative total reply count (used to decide whether a deeper
/// <c>comments.list</c> fetch is needed to reach replies beyond the inline ones).
/// </summary>
public sealed record YouTubeThread(YouTubeComment TopLevel, IReadOnlyList<YouTubeComment> InlineReplies, long TotalReplyCount);

/// <summary>The threads scanned within the window plus the quota <see cref="UnitsUsed"/> consumed (1 per page).</summary>
public sealed record ThreadFetchResult(IReadOnlyList<YouTubeThread> Threads, int UnitsUsed);

/// <summary>All replies for a single parent comment plus the quota <see cref="UnitsUsed"/> consumed (1 per page).</summary>
public sealed record RepliesResult(IReadOnlyList<YouTubeComment> Replies, int UnitsUsed);

/// <summary>A map of videoId -> title plus the quota <see cref="UnitsUsed"/> the lookups consumed.</summary>
public sealed record VideoTitlesResult(IReadOnlyDictionary<string, string> Titles, int UnitsUsed);

/// <summary>
/// Thin wrapper over the YouTube Data API. Polling uses API KEYS (not OAuth) — that is correct for
/// comment monitoring. A transient/quota failure surfaces as the underlying
/// <c>Google.GoogleApiException</c> so the caller can inspect the reason (e.g. <c>quotaExceeded</c>,
/// <c>commentsDisabled</c>) and react.
/// </summary>
public interface IYouTubeClient
{
    /// <summary>
    /// Verifies that <paramref name="apiKey"/> is a working YouTube Data API key by issuing a
    /// minimal request (videos.list?chart=mostPopular&amp;maxResults=1).
    /// </summary>
    Task<(bool Ok, string? Error)> ValidateKeyAsync(string apiKey, CancellationToken ct = default);

    /// <summary>
    /// Resolves a channel from a raw channel id, an <c>@handle</c>, or a youtube.com URL
    /// (<c>/channel/UC…</c>, <c>/@handle</c>, <c>/user/USERNAME</c>, <c>/c/CUSTOM</c>). Units used:
    /// 1 for a direct channels.list lookup, 101 when a search.list fallback was required.
    /// </summary>
    Task<ChannelLookupResult> GetChannelAsync(string apiKey, string idOrHandleOrUrl, CancellationToken ct = default);

    /// <summary>
    /// Fetches up to <paramref name="maxResults"/> of the most recent comment threads across all
    /// videos of <paramref name="youtubeChannelId"/> (commentThreads.list with
    /// allThreadsRelatedToChannelId, part=snippet,replies, newest-first; 1 quota unit). Inline replies
    /// are flattened in with <see cref="YouTubeComment.IsReply"/> set.
    /// </summary>
    Task<CommentFetchResult> GetRecentCommentsAsync(string apiKey, string youtubeChannelId, int maxResults = 50, CancellationToken ct = default);

    /// <summary>
    /// Resolves titles for <paramref name="videoIds"/> via videos.list (part=snippet) in batches of
    /// 50 (1 quota unit per batch). Missing/private videos are simply absent from the result map.
    /// </summary>
    Task<VideoTitlesResult> GetVideoTitlesAsync(string apiKey, IEnumerable<string> videoIds, CancellationToken ct = default);

    /// <summary>
    /// Pages through the channel's comment threads (commentThreads.list, order=time, part=snippet,replies;
    /// 1 unit per page of up to 100) newest-first, stopping once a thread is older than
    /// <paramref name="since"/> or <paramref name="maxPages"/> is reached. Used by the deep reply sweep.
    /// </summary>
    Task<ThreadFetchResult> GetCommentThreadsSinceAsync(
        string apiKey, string youtubeChannelId, DateTimeOffset since, int maxPages, CancellationToken ct = default);

    /// <summary>
    /// Fetches every reply for <paramref name="parentCommentId"/> via comments.list (1 unit per page of
    /// up to 100). <paramref name="parentVideoId"/> stamps the replies' video id (the API omits it here).
    /// </summary>
    Task<RepliesResult> GetRepliesAsync(
        string apiKey, string parentCommentId, string parentVideoId, int maxPages, CancellationToken ct = default);
}
