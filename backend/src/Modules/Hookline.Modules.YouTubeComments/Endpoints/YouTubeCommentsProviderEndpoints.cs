using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    // chat:write = post cards; channels:read + groups:read = list + target public channels AND private
    // channels (groups) — same visibility as YouTube Uploads; team:read = workspace name. These must ALSO
    // be configured in the Comments Slack app's Bot Token Scopes in the Slack console, then re-installed.
    private const string InstallScopes = "chat:write,channels:read,groups:read,team:read";

    public static void MapYouTubeCommentsProviderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/slack/youtube-comments/oauth/start", StartSlackOAuth);
        app.MapGet("/slack/youtube-comments/oauth/callback", HandleSlackCallbackAsync);
        app.MapPost("/slack/youtube-comments/interactivity", HandleInteractivityAsync);
    }

    /// <summary>
    /// Slack interactivity callback — handles the "Reject on YouTube" button. Signature-verified over
    /// the raw body (replay-guarded), then the <c>block_actions</c> payload is routed to the moderation
    /// service. The card is updated in place on success (response_url, replace_original) or an honest
    /// ephemeral error is returned. ACKs 200 within Slack's window.
    /// </summary>
    private static async Task<IResult> HandleInteractivityAsync(
        HttpRequest req, SlackSignatureVerifier verifier, IOptions<YouTubeCommentsOptions> opt,
        CommentModerationService moderation, ISlackClient slack, CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        if (!VerifySignature(req, verifier, opt.Value.Slack.SigningSecret, rawBody))
            return Results.Unauthorized();

        var payloadJson = ExtractFormField(rawBody, "payload");
        if (string.IsNullOrEmpty(payloadJson))
            return Results.Ok();

        using var doc = JsonDocument.Parse(payloadJson);
        await DispatchBlockActionsAsync(doc.RootElement, moderation, slack, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Routes a Slack <c>block_actions</c> interactivity payload whose signature has ALREADY been verified by
    /// the caller — the HTTP webhook above, or the dev-only Socket Mode client (pre-authenticated by the WSS
    /// handshake, so there is no per-message signature to check) — to the moderation service. Only the
    /// "Reject on YouTube" button takes a server action; the open-comment / re-consent buttons are URL links.
    /// The card is updated in place on success or an honest ephemeral error is returned. Shared by both
    /// inbound transports so they behave identically.
    /// </summary>
    internal static async Task DispatchBlockActionsAsync(
        JsonElement p, CommentModerationService moderation, ISlackClient slack, CancellationToken ct)
    {
        if (!p.TryGetProperty("type", out var pt) || pt.GetString() != "block_actions")
            return;
        if (!p.TryGetProperty("actions", out var actions) || actions.GetArrayLength() == 0)
            return;

        var action = actions[0];
        var actionId = action.TryGetProperty("action_id", out var aid) ? aid.GetString() : null;
        if (actionId != SlackActions.RejectComment)
            return; // the open_comment / reconsent_google link buttons need no server action

        var value = action.TryGetProperty("value", out var v) ? v.GetString() : null;
        var responseUrl = p.TryGetProperty("response_url", out var ru) ? ru.GetString() : null;
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(responseUrl))
            return;

        // value = "{mappingId}:{commentId}". Split on the FIRST colon — a comment id may contain none,
        // but it never contains a colon, and a Guid never does.
        var sep = value.IndexOf(':');
        if (sep <= 0 || !Guid.TryParse(value[..sep], out var mappingId))
            return;
        var commentId = value[(sep + 1)..];

        var actor = ExtractActor(p);
        var result = await moderation.RejectAsync(mappingId, commentId, actor, ct);

        if (result.CardShouldShowRejected)
        {
            JsonElement? blocks = p.TryGetProperty("message", out var msg)
                && msg.TryGetProperty("blocks", out var b) ? b : null;
            var statusLine = $"🚫 Removed on YouTube · by {actor.Display}";
            var updated = CommentCardUpdater.MarkActioned(blocks, statusLine);
            await slack.PostToResponseUrlAsync(
                responseUrl, new { replace_original = true, text = "Comment removed on YouTube", blocks = updated }, ct);
        }
        else
        {
            // Honest error (no scope / not owner / quota / failure) — ephemeral, card left intact to retry.
            await slack.PostToResponseUrlAsync(
                responseUrl, new { response_type = "ephemeral", replace_original = false, text = result.Message }, ct);
        }
    }

    private static SlackActor ExtractActor(JsonElement payload)
    {
        if (!payload.TryGetProperty("user", out var u))
            return new SlackActor(null, null);

        var id = u.TryGetProperty("id", out var uid) ? uid.GetString() : null;
        var name = u.TryGetProperty("username", out var un) ? un.GetString()
            : u.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        return new SlackActor(id, name);
    }

    private static bool VerifySignature(HttpRequest req, SlackSignatureVerifier verifier, string? secret, string rawBody)
        => verifier.Verify(
            secret,
            req.Headers["X-Slack-Request-Timestamp"].ToString(),
            rawBody,
            req.Headers["X-Slack-Signature"].ToString());

    private static async Task<string> ReadRawBodyAsync(HttpRequest req)
    {
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        return body;
    }

    private static string? ExtractFormField(string body, string name)
    {
        foreach (var pair in body.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            if (pair[..eq] == name) return WebUtility.UrlDecode(pair[(eq + 1)..]);
        }
        return null;
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
