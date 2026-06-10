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
    /// True for failures worth retrying: HTTP 5xx (server-side) or HTTP 429 (rate limited). Note that
    /// a 403 with reason <c>quotaExceeded</c> is intentionally NOT transient — retrying would only
    /// burn more quota; the job rotates to another key instead.
    /// </summary>
    public static bool IsTransient(this GoogleApiException ex)
    {
        var status = (int)ex.HttpStatusCode;
        return status >= 500 || ex.HttpStatusCode == HttpStatusCode.TooManyRequests;
    }

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
