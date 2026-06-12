using System.Text;
using System.Text.Json;

using Hookline.DevTools.SlackHarness;

// ── Hookline local Slack harness ────────────────────────────────────────────────────────────────────
// Builds a VALID, locally-signed Slack request and POSTs it to the running backend — no tunnel, no Slack.
// The signature is computed by SlackRequestSigner (proven byte-identical to the backend's
// SlackSignatureVerifier by SlackHarnessParityTests), so the request passes the real fail-closed verifier.
//
//   fire-upload   → a Slack EVENT that triggers a YouTube Uploads upload
//                   → POST /slack/youtube-uploads/events            (secret: SLACK_SIGNING_SECRET)
//   fire-reject   → a Slack INTERACTIVITY payload for the Comments "Reject on YouTube" button
//                   → POST /slack/youtube-comments/interactivity    (secret: SLACK_COMMENTS_SIGNING_SECRET)
//
// See docs/dev/local-testing.md for usage + env vars.

var (command, opts) = ParseArgs(args);

return command switch
{
    "fire-upload" => await FireUploadAsync(opts),
    "fire-reject" => await FireRejectAsync(opts),
    _ => Usage(),
};

static async Task<int> FireUploadAsync(Dictionary<string, string> opts)
{
    var baseUrl = Resolve(opts, "base-url", "HOOKLINE_BASE_URL", "http://localhost:8080").TrimEnd('/');
    var secret = Resolve(opts, "secret", "SLACK_SIGNING_SECRET", "");
    if (string.IsNullOrEmpty(secret))
        return Fail("No signing secret. Set SLACK_SIGNING_SECRET (the YouTube Uploads app) or pass --secret <value>.");

    var channel = Resolve(opts, "channel", null, "C0000000000");
    var user = Resolve(opts, "user", null, "U0000000000");
    var text = Resolve(opts, "text", null, "Demo upload from the local Slack harness");
    var eventId = Resolve(opts, "event-id", null, $"Ev{Guid.NewGuid():N}");
    var ts = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.000100";

    // Same shape an event_callback HTTP body has: a plain human message (no bot_id) the events handler enqueues.
    var body = JsonSerializer.Serialize(new
    {
        type = "event_callback",
        event_id = eventId,
        @event = new { type = "message", channel, user, ts, text },
    });

    return await PostAsync($"{baseUrl}/slack/youtube-uploads/events", body, "application/json", secret);
}

static async Task<int> FireRejectAsync(Dictionary<string, string> opts)
{
    var baseUrl = Resolve(opts, "base-url", "HOOKLINE_BASE_URL", "http://localhost:8080").TrimEnd('/');
    var secret = Resolve(opts, "secret", "SLACK_COMMENTS_SIGNING_SECRET", "");
    if (string.IsNullOrEmpty(secret))
        return Fail("No signing secret. Set SLACK_COMMENTS_SIGNING_SECRET (the YouTube Comments app) or pass --secret <value>.");

    var mapping = Resolve(opts, "mapping", null, Guid.NewGuid().ToString());
    var comment = Resolve(opts, "comment", null, "Ugx_demo.abc-123");
    var userId = Resolve(opts, "user-id", null, "U0000000000");
    var username = Resolve(opts, "username", null, "devtester");
    // Default sink is a backend path that 404s; PostToResponseUrlAsync swallows the failure, so the
    // dispatch still returns 200. Pass --response-url to point at a real sink if you want to see the reply.
    var responseUrl = Resolve(opts, "response-url", null, $"{baseUrl}/__dev/slack-response-sink");

    // action_id MUST equal SlackActions.RejectComment ("reject_comment") for the handler to act; value is
    // "{mappingId}:{commentId}" (split on the first colon by the handler).
    var payloadJson = JsonSerializer.Serialize(new
    {
        type = "block_actions",
        user = new { id = userId, username },
        response_url = responseUrl,
        actions = new[] { new { action_id = "reject_comment", value = $"{mapping}:{comment}" } },
        message = new { blocks = Array.Empty<object>() },
    });

    // Slack delivers interactivity as a form field; the handler url-decodes the "payload" field.
    var formBody = "payload=" + Uri.EscapeDataString(payloadJson);
    return await PostAsync($"{baseUrl}/slack/youtube-comments/interactivity", formBody,
        "application/x-www-form-urlencoded", secret);
}

static async Task<int> PostAsync(string url, string body, string contentType, string secret)
{
    var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    var signature = SlackRequestSigner.Sign(secret, ts, body);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    using var req = new HttpRequestMessage(HttpMethod.Post, url);
    req.Headers.TryAddWithoutValidation("X-Slack-Request-Timestamp", ts);
    req.Headers.TryAddWithoutValidation("X-Slack-Signature", signature);
    req.Content = new StringContent(body, Encoding.UTF8, contentType);

    Console.WriteLine($"POST {url}");
    Console.WriteLine($"  X-Slack-Request-Timestamp: {ts}");
    Console.WriteLine($"  X-Slack-Signature:         {signature}");
    Console.WriteLine($"  body: {Truncate(body, 400)}");

    try
    {
        using var res = await http.SendAsync(req);
        var resBody = await res.Content.ReadAsStringAsync();
        Console.WriteLine($"→ {(int)res.StatusCode} {res.StatusCode}  {Truncate(resBody, 300)}");
        if (res.IsSuccessStatusCode)
        {
            Console.WriteLine("✓ Accepted — the signature passed the backend's SlackSignatureVerifier and the handler ran.");
            return 0;
        }
        Console.WriteLine("✗ Non-2xx — a 401 means the signing secret does not match the backend's configured secret.");
        return 2;
    }
    catch (Exception ex)
    {
        return Fail($"Request failed: {ex.Message} (is the backend running and reachable at {url}?)");
    }
}

static (string Command, Dictionary<string, string> Opts) ParseArgs(string[] args)
{
    var command = args.Length > 0 ? args[0] : "";
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;
        var key = args[i][2..];
        var val = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        opts[key] = val;
    }
    return (command, opts);
}

static string Resolve(Dictionary<string, string> opts, string optKey, string? envKey, string fallback)
{
    if (opts.TryGetValue(optKey, out var v) && !string.IsNullOrEmpty(v))
        return v;
    if (envKey is not null && Environment.GetEnvironmentVariable(envKey) is { Length: > 0 } e)
        return e;
    return fallback;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

static int Usage()
{
    Console.WriteLine(
        """
        Hookline local Slack harness — POST a valid, locally-signed Slack request to the running backend.

        Usage:
          dotnet run --project backend/tools/Hookline.DevTools.SlackHarness -- <command> [--key value ...]

        Commands:
          fire-upload   Slack EVENT that triggers a YouTube Uploads upload
                        -> POST /slack/youtube-uploads/events        (secret: SLACK_SIGNING_SECRET)
            --channel <id>  --user <id>  --text <str>  --event-id <str>  --base-url <url>  --secret <str>

          fire-reject   Slack INTERACTIVITY for the YouTube Comments "Reject on YouTube" button
                        -> POST /slack/youtube-comments/interactivity (secret: SLACK_COMMENTS_SIGNING_SECRET)
            --mapping <guid>  --comment <id>  --user-id <id>  --username <str>
            --response-url <url>  --base-url <url>  --secret <str>

        Env fallbacks: HOOKLINE_BASE_URL, SLACK_SIGNING_SECRET, SLACK_COMMENTS_SIGNING_SECRET.
        """);
    return 1;
}
