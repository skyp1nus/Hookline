using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.Modules.YouTubeUploads.Jobs;
using Hookline.SharedKernel.Jobs;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Endpoints;

/// <summary>
/// Provider endpoints — backend-direct (the identity middleware bypasses <c>/slack</c> and <c>/google</c>);
/// they are signature/state-verified instead. Interim module-scoped paths per the architecture guide §6;
/// the resulting workspace + account are stored in the SHARED Connections subsystem.
/// </summary>
public static class YouTubeUploadsProviderEndpoints
{
    private const string SlackStateCookie = "slack_oauth_state";
    private const string GoogleStateCookie = "g_oauth_state";
    private const string GoogleClientCookie = "g_oauth_client";

    private const string InstallScopes =
        "chat:write,channels:read,channels:history,groups:read,groups:history,pins:write,files:read";

    public static void MapYouTubeUploadsProviderEndpoints(this IEndpointRouteBuilder app)
    {
        MapSlackEndpoints(app);
        MapGoogleEndpoints(app);
    }

    // ───────────────────────────── Slack ─────────────────────────────
    private static void MapSlackEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/slack/youtube-uploads/events", HandleEventsAsync);
        app.MapPost("/slack/youtube-uploads/interactivity", HandleInteractivityAsync);
        app.MapGet("/slack/youtube-uploads/oauth/start", StartSlackOAuth);
        app.MapGet("/slack/youtube-uploads/oauth/callback", HandleSlackCallbackAsync);
    }

    private static IResult StartSlackOAuth(HttpContext http, IOptions<YouTubeUploadsOptions> opt)
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
        HttpContext http, IOptions<YouTubeUploadsOptions> opt, SlackChannelService workspaces,
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
            await workspaces.HandleOAuthCallbackAsync(code, opt.Value.Slack.RedirectUri, ct);
            return Results.Redirect($"{panel}/connections/slack?connected=1");
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("YouTubeUploads.Slack.OAuth").LogError(ex, "Slack OAuth callback failed");
            return Results.Redirect($"{panel}/connections/slack?error=oauth_failed");
        }
    }

    private static async Task<IResult> HandleEventsAsync(
        HttpRequest req, SlackSignatureVerifier verifier, IOptions<YouTubeUploadsOptions> opt,
        IDedupService dedup, IJobScheduler scheduler, CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        if (!VerifySignature(req, verifier, opt.Value.Slack.SigningSecret, rawBody))
            return Results.Unauthorized();

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        // url_verification is the HTTP Request-URL handshake only; Socket Mode uses a 'hello' frame instead.
        if (type == "url_verification")
            return Results.Text(root.GetProperty("challenge").GetString() ?? "");

        if (type == "event_callback")
            await ProcessEventCallbackAsync(root, dedup, scheduler);

        return Results.Ok(); // 200 ACK within 3s
    }

    /// <summary>
    /// Processes a Slack <c>event_callback</c> envelope whose signature has ALREADY been verified by the
    /// caller — either the HTTP webhook above, or the dev-only Socket Mode client (whose WSS handshake
    /// pre-authenticates the frame, so no per-message signature exists to check). A plain human message is
    /// claimed once via the dedup guard and handed to the durable ingest pipeline. Shared by both inbound
    /// transports so they behave identically.
    /// </summary>
    internal static async Task ProcessEventCallbackAsync(JsonElement root, IDedupService dedup, IJobScheduler scheduler)
    {
        if (!root.TryGetProperty("event", out var ev))
            return;

        var eventId = root.TryGetProperty("event_id", out var eid) ? eid.GetString() : null;
        var evType = ev.TryGetProperty("type", out var et) ? et.GetString() : null;
        var subtype = ev.TryGetProperty("subtype", out var sub) ? sub.GetString() : null;

        var isPlainMessage = evType == "message"
            && (subtype is null || subtype == "file_share")
            && !ev.TryGetProperty("bot_id", out _);

        if (isPlainMessage && eventId is not null && await dedup.TryClaimAsync(eventId))
        {
            var (thumbUrl, thumbMime) = ExtractThumbnail(ev);
            var msg = new SlackMessageRef(
                eventId,
                ev.TryGetProperty("channel", out var c) ? c.GetString() ?? "" : "",
                ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "",
                ev.TryGetProperty("ts", out var ts) ? ts.GetString() ?? "" : "",
                ev.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "",
                thumbUrl, thumbMime);

            scheduler.Enqueue<SlackIngestService>(s => s.ProcessMessageAsync(msg, CancellationToken.None));
        }
    }

    private static async Task<IResult> HandleInteractivityAsync(
        HttpRequest req, SlackSignatureVerifier verifier, IOptions<YouTubeUploadsOptions> opt,
        IJobService jobs, ICancellationFlags cancelFlags, ISlackStatusService status,
        SlackClient slack, SlackIngestService ingest, CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        if (!VerifySignature(req, verifier, opt.Value.Slack.SigningSecret, rawBody))
            return Results.Unauthorized();

        var payloadJson = ExtractFormField(rawBody, "payload");
        if (string.IsNullOrEmpty(payloadJson)) return Results.Ok();

        using var doc = JsonDocument.Parse(payloadJson);
        await DispatchBlockActionsAsync(doc.RootElement, jobs, cancelFlags, status, slack, ingest, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Routes a Slack <c>block_actions</c> interactivity payload whose signature has ALREADY been verified by
    /// the caller — the HTTP webhook above, or the dev-only Socket Mode client — to the shared
    /// cancel/confirm/decline logic. Shared by both inbound transports so they behave identically.
    /// </summary>
    internal static async Task DispatchBlockActionsAsync(
        JsonElement p, IJobService jobs, ICancellationFlags cancelFlags, ISlackStatusService status,
        SlackClient slack, SlackIngestService ingest, CancellationToken ct)
    {
        if (p.TryGetProperty("type", out var pt) && pt.GetString() != "block_actions")
            return;
        if (!p.TryGetProperty("actions", out var actions) || actions.GetArrayLength() == 0)
            return;

        var action = actions[0];
        var actionId = action.GetProperty("action_id").GetString();
        var value = action.TryGetProperty("value", out var v) ? v.GetString() : null;
        var responseUrl = p.TryGetProperty("response_url", out var ru) ? ru.GetString() : null;

        if (!Guid.TryParse(value, out var jobId))
            return;

        switch (actionId)
        {
            case SlackActions.CancelJob:
                await YouTubeUploadsActions.CancelAsync(jobs, cancelFlags, status, jobId,
                    msg => Respond(slack, responseUrl, msg), ct);
                break;
            case SlackActions.ConfirmUpload:
                await YouTubeUploadsActions.ConfirmAsync(jobs, ingest, status, jobId,
                    msg => Respond(slack, responseUrl, msg), ct);
                break;
            case SlackActions.DeclineUpload:
                await YouTubeUploadsActions.DeclineAsync(jobs, jobId, msg => Respond(slack, responseUrl, msg), ct);
                break;
        }
    }

    private static Task Respond(SlackClient slack, string? responseUrl, string text)
        => responseUrl is null
            ? Task.CompletedTask
            : slack.PostToResponseUrlAsync(responseUrl, new { replace_original = true, text });

    // ───────────────────────────── Google ─────────────────────────────
    private static void MapGoogleEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/google/youtube-uploads/oauth/start", async (
            Guid? projectId, GoogleAccountsService oauth, IOptions<YouTubeUploadsOptions> opt, HttpContext http, CancellationToken ct) =>
        {
            var panel = opt.Value.AdminPanelUrl.TrimEnd('/');
            if (projectId is null)
                return Results.Redirect($"{panel}/connections/google?error=missing_client");

            var state = GenerateState();
            string consentUrl;
            try { consentUrl = await oauth.BuildConsentUrlAsync(projectId.Value, state, ct); }
            catch (Exception ex) { return Results.Redirect($"{panel}/connections/google?error={Uri.EscapeDataString(ex.Message)}"); }

            http.Response.Cookies.Append(GoogleStateCookie, state, StateCookie(http));
            http.Response.Cookies.Append(GoogleClientCookie, projectId.Value.ToString(), StateCookie(http));
            return Results.Redirect(consentUrl);
        });

        app.MapGet("/google/youtube-uploads/oauth/callback", async (
            string? code, string? state, HttpContext http, GoogleAccountsService oauth,
            IOptions<YouTubeUploadsOptions> opt, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var panel = opt.Value.AdminPanelUrl.TrimEnd('/');
            var expected = http.Request.Cookies[GoogleStateCookie];
            var clientCookie = http.Request.Cookies[GoogleClientCookie];
            http.Response.Cookies.Delete(GoogleStateCookie, new CookieOptions { Path = "/" });
            http.Response.Cookies.Delete(GoogleClientCookie, new CookieOptions { Path = "/" });

            if (string.IsNullOrEmpty(code) || !ValidState(state, expected))
                return Results.Redirect($"{panel}/connections/google?error=invalid_state");
            if (!Guid.TryParse(clientCookie, out var projectId))
                return Results.Redirect($"{panel}/connections/google?error=invalid_state");

            try
            {
                await oauth.ExchangeAndStoreAsync(projectId, code!, ct);
                return Results.Redirect($"{panel}/connections/google?connected=1");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("YouTubeUploads.Google.OAuth").LogError(ex, "Google OAuth callback failed");
                return Results.Redirect($"{panel}/connections/google?error={Uri.EscapeDataString(ex.Message)}");
            }
        });
    }

    // ───────────────────────────── helpers ─────────────────────────────
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

    private static (string? Url, string? Mime) ExtractThumbnail(JsonElement ev)
    {
        if (!ev.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return (null, null);
        foreach (var f in files.EnumerateArray())
        {
            var mime = f.TryGetProperty("mimetype", out var mt) ? mt.GetString() : null;
            if ((mime == "image/png" || mime == "image/jpeg")
                && f.TryGetProperty("url_private", out var up) && up.GetString() is { Length: > 0 } url)
                return (url, mime);
        }
        return (null, null);
    }
}

/// <summary>Shared cancel/confirm/decline logic used by both the Slack interactivity handler and the
/// <c>/api/youtube-uploads/jobs/{id}/{action}</c> BFF endpoints, so the two stay behaviourally identical.</summary>
internal static class YouTubeUploadsActions
{
    public static async Task<(bool ok, string? error)> CancelAsync(
        IJobService jobs, ICancellationFlags cancelFlags, ISlackStatusService status,
        Guid jobId, Func<string, Task> respond, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return (false, "not_found");
        if (!job.IsCancellable)
        {
            await respond("Already uploading/uploaded — remove it manually in YouTube Studio.");
            return (false, "not_cancellable");
        }

        await cancelFlags.RequestAsync(jobId);
        if (job.State == JobState.Queued)
        {
            await jobs.TransitionAsync(job, JobState.Cancelled, "cancelled by user (queued)", ct);
            await status.RefreshQueueAsync(ct);
        }
        await respond("Cancellation requested.");
        return (true, null);
    }

    public static async Task<(bool ok, string? error)> ConfirmAsync(
        IJobService jobs, SlackIngestService ingest, ISlackStatusService status,
        Guid jobId, Func<string, Task> respond, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null || job.IsTerminal)
        {
            await respond("This request is no longer pending.");
            return (false, "not_pending");
        }
        if (job.Confirmed == true)
        {
            await respond("Already confirmed — uploading.");
            return (true, null);
        }

        job.Confirmed = true;
        await jobs.SaveAsync(job, ct);
        ingest.Enqueue(job.Id);
        await status.RefreshQueueAsync(ct);
        await respond(":white_check_mark: Confirmed — uploading.");
        return (true, null);
    }

    public static async Task<(bool ok, string? error)> DeclineAsync(
        IJobService jobs, Guid jobId, Func<string, Task> respond, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return (false, "not_found");
        if (job.State == JobState.Queued && job.Confirmed != true)
        {
            job.Confirmed = false;
            await jobs.TransitionAsync(job, JobState.Cancelled, "declined by user", ct);
        }
        await respond("Cancelled — not uploaded.");
        return (true, null);
    }
}
