using System.Security.Cryptography;
using System.Text;

using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Endpoints;

/// <summary>
/// Provider endpoints — backend-direct (the identity middleware bypasses <c>/slack</c>); the OAuth
/// install is CSRF-protected by a one-shot state cookie instead. Module-scoped paths per the
/// architecture guide §6; the resulting workspace is stored in the SHARED Connections subsystem.
/// </summary>
public static class YouTubeCommentsProviderEndpoints
{
    private const string SlackStateCookie = "ytc_slack_oauth_state";
    private const string InstallScopes = "chat:write,channels:read,groups:read,team:read";

    public static void MapYouTubeCommentsProviderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/slack/youtube-comments/oauth/start", StartSlackOAuth);
        app.MapGet("/slack/youtube-comments/oauth/callback", HandleSlackCallbackAsync);
    }

    private static IResult StartSlackOAuth(HttpContext http, IOptions<YouTubeCommentsOptions> opt)
    {
        var o = opt.Value.Slack;
        var state = GenerateState();
        http.Response.Cookies.Append(SlackStateCookie, state, StateCookie(http));

        var url = "https://slack.com/oauth/v2/authorize"
            + $"?client_id={Uri.EscapeDataString(o.ClientId)}"
            + $"&scope={Uri.EscapeDataString(InstallScopes)}"
            + $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(url);
    }

    private static async Task<IResult> HandleSlackCallbackAsync(
        HttpContext http, IOptions<YouTubeCommentsOptions> opt, SlackChannelService slack,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var panel = opt.Value.AdminPanelUrl.TrimEnd('/');
        var code = http.Request.Query["code"].ToString();
        var state = http.Request.Query["state"].ToString();
        var slackError = http.Request.Query["error"].ToString();

        var expected = http.Request.Cookies[SlackStateCookie];
        http.Response.Cookies.Delete(SlackStateCookie, new CookieOptions { Path = "/" });

        if (!string.IsNullOrEmpty(slackError))
            return Results.Redirect($"{panel}/connections/slack?error={Uri.EscapeDataString(slackError)}");
        if (string.IsNullOrEmpty(code))
            return Results.Redirect($"{panel}/connections/slack?error=missing_code");
        if (!ValidState(state, expected))
            return Results.Redirect($"{panel}/connections/slack?error=invalid_state");

        try
        {
            await slack.HandleOAuthCallbackAsync(code, opt.Value.Slack.RedirectUri, ct);
            return Results.Redirect($"{panel}/connections/slack?connected=1");
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("YouTubeComments.Slack.OAuth").LogError(ex, "Slack OAuth callback failed");
            return Results.Redirect($"{panel}/connections/slack?error=oauth_failed");
        }
    }

    private static CookieOptions StateCookie(HttpContext http) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/",
    };

    private static string GenerateState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static bool ValidState(string? state, string? expected) =>
        !string.IsNullOrEmpty(state) && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected));
}
