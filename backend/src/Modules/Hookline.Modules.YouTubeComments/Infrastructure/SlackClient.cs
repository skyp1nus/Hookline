using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// <see cref="ISlackClient"/> over a raw <see cref="HttpClient"/> (the bot token is passed per call,
/// so one instance serves any workspace). chat.postMessage carries the Block Kit comment card with a
/// deep link to the comment; conversations.list is form-encoded (a JSON body silently drops
/// <c>types</c>). Retries HTTP 429 honoring Retry-After. App client id/secret (for the OAuth code
/// exchange) come from <see cref="YouTubeCommentsOptions"/>.
/// </summary>
public sealed class SlackClient(
    HttpClient http,
    IOptions<YouTubeCommentsOptions> options,
    ILogger<SlackClient> logger) : ISlackClient
{
    private const string ApiBase = "https://slack.com/api/";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly YouTubeCommentsOptions.SlackSettings _opt = options.Value.Slack;

    // Slack error codes that mean the channel is permanently unusable: retrying will never succeed,
    // so the mapping is deactivated rather than left to fail on every poll.
    private static readonly HashSet<string> ChannelGoneCodes = new(StringComparer.Ordinal)
    {
        "channel_not_found",
        "is_archived",
        "not_in_channel",
    };

    /// <inheritdoc />
    public async Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _opt.ClientId,
            ["client_secret"] = _opt.ClientSecret,
            ["redirect_uri"] = redirectUri,
        };

        using var res = await http.PostAsync(ApiBase + "oauth.v2.access", new FormUrlEncodedContent(form), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(
                $"oauth.v2.access failed: {(root.TryGetProperty("error", out var e) ? e.GetString() : "unknown")}");

        var team = root.GetProperty("team");
        return new SlackOAuthResult(
            AccessToken: root.GetProperty("access_token").GetString()!,
            TeamId: team.GetProperty("id").GetString()!,
            TeamName: team.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "",
            BotUserId: root.TryGetProperty("bot_user_id", out var bu) ? bu.GetString() : null,
            Scope: root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
            AuthedUserId: root.TryGetProperty("authed_user", out var au) && au.TryGetProperty("id", out var auid)
                ? auid.GetString()
                : null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default)
    {
        var result = new List<SlackChannelInfo>();
        string? cursor = null;
        do
        {
            // conversations.list reads its params from the FORM/query string, NOT a JSON body. Sent as
            // JSON, `types` is silently dropped and Slack falls back to public_channel only — so private
            // channels never come back regardless of scopes/membership. Always call it form-encoded.
            var form = new Dictionary<string, string>
            {
                ["types"] = "public_channel,private_channel",
                ["exclude_archived"] = "true",
                ["limit"] = "200",
            };
            if (!string.IsNullOrEmpty(cursor)) form["cursor"] = cursor;

            var root = await CallFormAsync(botToken, "conversations.list", form, ct);
            if (root is null || !root.Value.GetProperty("ok").GetBoolean()) break;

            if (root.Value.TryGetProperty("channels", out var channels))
            {
                foreach (var c in channels.EnumerateArray())
                {
                    result.Add(new SlackChannelInfo(
                        c.GetProperty("id").GetString()!,
                        c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        c.TryGetProperty("is_private", out var p) && p.GetBoolean()));
                }
            }

            // Stop when next_cursor is an EMPTY STRING (the last page still has channels).
            cursor = root.Value.TryGetProperty("response_metadata", out var meta)
                     && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <inheritdoc />
    public async Task<SlackPostResult> PostCommentAsync(
        string botToken, string channelId, CommentNotification comment,
        string? threadTs = null, Guid? mappingId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(botToken))
            return new SlackPostResult(SlackPostStatus.RetryableFailure);

        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channelId,
            ["text"] = Fallback(comment),
            // Suppress Slack's auto link/media unfurl so the YouTube preview card doesn't appear
            // beneath the comment card (the links live inside the blocks + the button).
            ["unfurl_links"] = false,
            ["unfurl_media"] = false,
            ["blocks"] = BuildBlocks(comment, mappingId),
        };
        if (threadTs is not null) payload["thread_ts"] = threadTs;

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + "chat.postMessage")
                {
                    Content = JsonContent.Create(payload, options: JsonOpts),
                };
                req.Headers.Authorization = new("Bearer", botToken);

                using var res = await http.SendAsync(req, ct);
                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    logger.LogWarning("Slack chat.postMessage to {ChannelId} rate-limited; waiting {Wait}s", channelId, wait.TotalSeconds);
                    await Task.Delay(wait, ct);
                    continue;
                }

                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                    return new SlackPostResult(SlackPostStatus.Posted, root.TryGetProperty("ts", out var ts) ? ts.GetString() : null);

                var error = root.TryGetProperty("error", out var er) ? er.GetString() : null;
                var status = error is not null && ChannelGoneCodes.Contains(error)
                    ? SlackPostStatus.ChannelGone
                    : SlackPostStatus.RetryableFailure;
                logger.LogWarning("Slack chat.postMessage to {ChannelId} failed: {Error} ({Status})", channelId, error, status);
                return new SlackPostResult(status);
            }

            // 429-retry budget exhausted — treat as transient so the delivery-retry job tries again.
            return new SlackPostResult(SlackPostStatus.RetryableFailure);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Slack chat.postMessage to {ChannelId} threw", channelId);
            return new SlackPostResult(SlackPostStatus.RetryableFailure);
        }
    }

    private static string Fallback(CommentNotification c) => c.IsReply
        ? $"Reply from {c.AuthorName} on \"{c.VideoTitle}\""
        : $"New comment from {c.AuthorName} on \"{c.VideoTitle}\"";

    /// <summary>
    /// Builds the Block Kit comment card: a header context (avatar + linked author and video), the
    /// quoted comment, and a primary button that deep links straight to the comment under the video.
    /// </summary>
    private static object[] BuildBlocks(CommentNotification c, Guid? mappingId = null)
    {
        // Canonical www host (matches YouTube's own canonical link) so the comment deep link resolves
        // in one hop. The lc (linked comment) param is YouTube's official comment permalink: on desktop
        // web it highlights the comment and scrolls to it. A top-level comment links by its own id;
        // a reply uses the "parentId.replyId" form.
        var videoUrl = $"https://www.youtube.com/watch?v={c.VideoId}";
        var lc = c is { IsReply: true, ParentCommentId: { } parent } ? $"{parent}.{c.CommentId}" : c.CommentId;
        var commentUrl = $"{videoUrl}&lc={lc}";

        var headerElements = new List<object>();
        if (!string.IsNullOrWhiteSpace(c.AuthorImageUrl))
            headerElements.Add(new { type = "image", image_url = c.AuthorImageUrl, alt_text = c.AuthorName });

        var authorLink = string.IsNullOrWhiteSpace(c.AuthorChannelUrl)
            ? $"*{EscapeMrkdwn(c.AuthorName)}*"
            : $"*<{c.AuthorChannelUrl}|{EscapeMrkdwn(c.AuthorName)}>*";
        var replyPrefix = c.IsReply ? "↳ " : string.Empty;
        headerElements.Add(new { type = "mrkdwn", text = $"{replyPrefix}{authorLink}  ·  ▶ *<{videoUrl}|{EscapeMrkdwn(c.VideoTitle)}>*" });

        var buttonText = c.IsReply ? "▶ Go to reply" : "▶ Go to comment";

        var actionElements = new List<object>
        {
            new { type = "button", text = new { type = "plain_text", text = buttonText, emoji = true }, url = commentUrl, action_id = SlackActions.OpenComment, style = "primary" },
        };

        // The "Reject on YouTube" button is only added when a mapping id is supplied (so the callback can
        // route to the comment + owning channel). It carries an inline confirm dialog — this is an
        // irreversible-via-Slack moderation action. The label says "Reject/Hide", NOT "Delete": it sets
        // moderationStatus=rejected (hides the comment), which is reversible in YouTube Studio. The value
        // is "{mappingId}:{commentId}" — the raw comment id (the API target), not the lc deep-link form.
        if (mappingId is { } id)
        {
            actionElements.Add(new
            {
                type = "button",
                text = new { type = "plain_text", text = "🚫 Reject on YouTube", emoji = true },
                action_id = SlackActions.RejectComment,
                value = $"{id}:{c.CommentId}",
                style = "danger",
                confirm = new
                {
                    title = new { type = "plain_text", text = "Reject this comment?" },
                    text = new { type = "mrkdwn", text = "This *hides* the comment on YouTube (moderation status → rejected). It can be restored in YouTube Studio, but not from here." },
                    confirm = new { type = "plain_text", text = "Reject" },
                    deny = new { type = "plain_text", text = "Cancel" },
                    style = "danger",
                },
            });
        }

        return
        [
            new { type = "context", elements = headerElements.ToArray() },
            new { type = "section", text = new { type = "mrkdwn", text = BlockQuote(c.CommentText) } },
            new { type = "actions", elements = actionElements.ToArray() },
        ];
    }

    /// <inheritdoc />
    public async Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(responseUrl))
            return;

        try
        {
            using var res = await http.PostAsync(responseUrl, JsonContent.Create(payload, options: JsonOpts), ct);
            if (!res.IsSuccessStatusCode)
                logger.LogWarning("Slack response_url POST returned {Status}", (int)res.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Slack response_url POST threw");
        }
    }

    /// <summary>Escapes the three characters Slack mrkdwn treats specially (&amp;, &lt;, &gt;).</summary>
    private static string EscapeMrkdwn(string text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Renders <paramref name="text"/> as a Slack block quote: every line prefixed with "&gt;".</summary>
    private static string BlockQuote(string text)
    {
        if (string.IsNullOrEmpty(text))
            return ">";

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(line => $">{EscapeMrkdwn(line)}"));
    }

    // ---- core form-encoded call with 429 retry (read methods whose params Slack reads from the form) ----
    private async Task<JsonElement?> CallFormAsync(
        string botToken, string method, Dictionary<string, string> form, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(botToken))
        {
            logger.LogWarning("No Slack bot token available — skipping {Method}", method);
            return null;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + method)
            {
                Content = new FormUrlEncodedContent(form),
            };
            req.Headers.Authorization = new("Bearer", botToken);

            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                logger.LogWarning("Slack {Method} rate-limited; waiting {Wait}s", method, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }

            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                logger.LogWarning("Slack {Method} returned error: {Error}", method,
                    root.TryGetProperty("error", out var er) ? er.GetString() : "unknown");
            }
            return root.Clone();
        }

        return null;
    }
}
