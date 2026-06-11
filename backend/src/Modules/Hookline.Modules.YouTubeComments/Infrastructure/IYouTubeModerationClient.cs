using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.YouTube.v3;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// The write side of the YouTube Data API used by comment moderation — distinct from the read-only,
/// API-key-based <see cref="IYouTubeClient"/> (polling). Authorized per call with a short-lived OAuth
/// access token resolved through the shared <c>IGoogleChannelCredentials</c> contract. A failure
/// surfaces as the underlying <c>Google.GoogleApiException</c> so the caller can branch on the reason
/// (already-gone / forbidden / quota).
/// </summary>
public interface IYouTubeModerationClient
{
    /// <summary>
    /// Sets <c>moderationStatus = rejected</c> on <paramref name="commentId"/> (hides it on YouTube),
    /// authorized by <paramref name="accessToken"/>. This is a moderation status change, not a hard
    /// delete — reversible in YouTube Studio. Throws <c>GoogleApiException</c> on an API error.
    /// </summary>
    Task RejectAsync(string accessToken, string commentId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IYouTubeModerationClient"/> over the Google.Apis.YouTube.v3 client library. Builds a
/// per-call <see cref="YouTubeService"/> authorized from the supplied access token and issues
/// <c>comments.setModerationStatus</c>.
/// </summary>
public sealed class YouTubeModerationClient : IYouTubeModerationClient
{
    private const string ApplicationName = "Hookline.YouTubeComments";

    public async Task RejectAsync(string accessToken, string commentId, CancellationToken ct = default)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        using var service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });

        var request = service.Comments.SetModerationStatus(
            new Repeatable<string>(new[] { commentId }),
            CommentsResource.SetModerationStatusRequest.ModerationStatusEnum.Rejected);

        await request.ExecuteAsync(ct);
    }
}
