using System.Net;

using Google;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Helpers for classifying <see cref="GoogleApiException"/>s so the polling jobs can branch on
/// specific failure reasons (quota, comments disabled).
/// </summary>
public static class GoogleApiExceptionExtensions
{
    /// <summary>
    /// True for HTTP statuses worth retrying: 5xx (server-side) or 429 (rate limited). A 403 with
    /// reason <c>quotaExceeded</c> is deliberately excluded — it is NOT transient, so retrying would
    /// only burn more quota; the job rotates to another key instead. Shared by the YouTube client's
    /// in-call back-off handler (on the raw response) and <see cref="IsTransient"/> (on the exception).
    /// </summary>
    public static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests;

    /// <summary>True when this exception's HTTP status is transient (see <see cref="IsTransientStatus"/>).</summary>
    public static bool IsTransient(this GoogleApiException ex) => IsTransientStatus(ex.HttpStatusCode);

    /// <summary>
    /// True for a hard, key/project-level credential failure that will not fix itself: the key was
    /// revoked/invalid, expired, restricted (IP/referer), or the YouTube Data API is not enabled for
    /// its project. The job auto-disables such a key so it drops out of rotation instead of failing
    /// every tick. Deliberately excludes <c>quotaExceeded</c> (recovers at the Pacific-day rollover)
    /// and channel-level <c>commentsDisabled</c>.
    /// </summary>
    public static bool IsKeyInvalid(this GoogleApiException ex) =>
        ex.HasReason("keyInvalid")
        || ex.HasReason("keyExpired")
        || ex.HasReason("accessNotConfigured")
        || ex.HasReason("ipRefererBlocked");

    /// <summary>
    /// True when any reported <see cref="Google.Apis.Requests.SingleError.Reason"/> equals
    /// <paramref name="reason"/> (case-insensitive), e.g. <c>quotaExceeded</c>, <c>commentsDisabled</c>.
    /// </summary>
    public static bool HasReason(this GoogleApiException ex, string reason)
    {
        var errors = ex.Error?.Errors;
        if (errors is null)
            return false;

        foreach (var e in errors)
        {
            if (string.Equals(e.Reason, reason, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
