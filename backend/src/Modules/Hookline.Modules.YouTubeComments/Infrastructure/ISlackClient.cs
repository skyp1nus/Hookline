namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>Result of a successful Slack OAuth v2 token exchange.</summary>
public sealed record SlackOAuthResult(
    string AccessToken,
    string TeamId,
    string TeamName,
    string? BotUserId,
    string? Scope,
    string? AuthedUserId);

/// <summary>A Slack conversation (channel) the bot can see.</summary>
public sealed record SlackChannelInfo(string Id, string Name, bool IsPrivate);

/// <summary>A YouTube comment to render as a Slack Block Kit notification.</summary>
public sealed record CommentNotification(
    string AuthorName,
    string? AuthorChannelUrl,
    string? AuthorImageUrl,
    string VideoTitle,
    string VideoId,
    string CommentText,
    long LikeCount,
    DateTimeOffset PublishedAt,
    string CommentId,
    bool IsReply = false,
    string? ParentCommentId = null);

/// <summary>Outcome of posting a comment to Slack.</summary>
public enum SlackPostStatus
{
    /// <summary>Slack accepted the message.</summary>
    Posted,

    /// <summary>A transient failure (rate limit, transport, unknown error) — safe to retry later.</summary>
    RetryableFailure,

    /// <summary>The channel is gone for good (archived / not found / bot removed) — retrying is futile.</summary>
    ChannelGone,
}

/// <summary>
/// Result of <see cref="ISlackClient.PostCommentAsync"/>. On <see cref="SlackPostStatus.Posted"/>,
/// <see cref="MessageTs"/> carries the message timestamp used to thread replies under it.
/// </summary>
public sealed record SlackPostResult(SlackPostStatus Status, string? MessageTs = null)
{
    public bool Ok => Status == SlackPostStatus.Posted;
}

/// <summary>
/// Thin wrapper over the Slack Web API: OAuth v2 token exchange, listing conversations, and posting
/// comment notifications. Implemented in Infrastructure over a raw <c>HttpClient</c>.
/// </summary>
public interface ISlackClient
{
    /// <summary>Exchanges an OAuth authorization <paramref name="code"/> for a bot token + workspace metadata.</summary>
    Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    /// <summary>Lists every public and private (non-archived) channel visible to the bot.</summary>
    Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default);

    /// <summary>
    /// Posts a Block Kit comment notification to <paramref name="channelId"/>, optionally threaded
    /// under <paramref name="threadTs"/>. When <paramref name="mappingId"/> is supplied the card carries
    /// a moderation action: the active "Reject on YouTube" button when <paramref name="canModerate"/> is
    /// true (the owning Google account holds the force-ssl scope), otherwise a proactive "Re-consent to
    /// enable removal" link to Connections → Google — so an unscoped account never shows a Reject button
    /// that would only fail on click. The returned <see cref="SlackPostResult"/> distinguishes a
    /// successful post (with its message ts) from a retryable failure and a permanently-gone channel.
    /// </summary>
    Task<SlackPostResult> PostCommentAsync(
        string botToken, string channelId, CommentNotification comment,
        string? threadTs = null, Guid? mappingId = null, bool canModerate = false, CancellationToken ct = default);

    /// <summary>
    /// POSTs a payload to a Slack <c>response_url</c> from an interaction (no bot token needed). Used to
    /// update the card after a moderation action (<c>replace_original</c>) or to reply ephemerally with
    /// an honest error. Best-effort: a non-success response is logged, not thrown.
    /// </summary>
    Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default);
}
