using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The interactivity-callback building blocks: the Slack signature verifier (replay-guarded HMAC), and
/// the card rewrite that strips the "Reject" button and appends a "removed by" status line after a
/// successful moderation.
/// </summary>
public class CommentInteractivityTests
{
    // ── signature verification ──
    private const string Secret = "8f742c2a1b3d4e5f";

    private static string Sign(string secret, string ts, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "v0=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"v0:{ts}:{body}"))).ToLowerInvariant();
    }

    [Fact]
    public void Valid_signature_passes()
    {
        var v = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string body = "payload=%7B%22type%22%3A%22block_actions%22%7D";

        Assert.True(v.Verify(Secret, ts, body, Sign(Secret, ts, body)));
    }

    [Fact]
    public void Tampered_signature_fails()
    {
        var v = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        Assert.False(v.Verify(Secret, ts, "payload=x", "v0=deadbeef"));
    }

    [Fact]
    public void Stale_timestamp_is_rejected_as_replay()
    {
        var v = new SlackSignatureVerifier();
        const string body = "payload=x";
        var stale = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();

        Assert.False(v.Verify(Secret, stale, body, Sign(Secret, stale, body)));
    }

    [Fact]
    public void Empty_signing_secret_fails_closed()
    {
        var v = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        Assert.False(v.Verify("", ts, "payload=x", Sign("", ts, "payload=x")));
    }

    // ── card rewrite ──
    private static JsonElement Blocks(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Reject_button_is_stripped_open_button_kept_and_status_appended()
    {
        var original = Blocks("""
        [
          {"type":"context","elements":[{"type":"mrkdwn","text":"hdr"}]},
          {"type":"section","text":{"type":"mrkdwn","text":">quote"}},
          {"type":"actions","elements":[
            {"type":"button","action_id":"open_comment","url":"https://youtu.be/x"},
            {"type":"button","action_id":"reject_comment","value":"g:CMT1"}
          ]}
        ]
        """);

        var updated = JsonDocument.Parse(
            CommentCardUpdater.MarkActioned(original, "🚫 Removed on YouTube · by @ada").ToJsonString()).RootElement;

        Assert.Equal(4, updated.GetArrayLength()); // context, section, actions(1), status context

        var actions = updated[2];
        Assert.Equal("actions", actions.GetProperty("type").GetString());
        var elements = actions.GetProperty("elements");
        Assert.Equal(1, elements.GetArrayLength());
        Assert.Equal("open_comment", elements[0].GetProperty("action_id").GetString());

        var status = updated[3];
        Assert.Equal("context", status.GetProperty("type").GetString());
        Assert.Contains("Removed on YouTube", status.GetProperty("elements")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Actions_block_is_dropped_when_only_the_reject_button_remained()
    {
        var original = Blocks("""
        [
          {"type":"section","text":{"type":"mrkdwn","text":">quote"}},
          {"type":"actions","elements":[
            {"type":"button","action_id":"reject_comment","value":"g:CMT1"}
          ]}
        ]
        """);

        var updated = JsonDocument.Parse(
            CommentCardUpdater.MarkActioned(original, "🚫 Removed").ToJsonString()).RootElement;

        // section + status context only — the now-empty actions row is gone.
        Assert.Equal(2, updated.GetArrayLength());
        Assert.Equal("section", updated[0].GetProperty("type").GetString());
        Assert.Equal("context", updated[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Missing_blocks_yields_a_single_status_context()
    {
        var updated = JsonDocument.Parse(
            CommentCardUpdater.MarkActioned(null, "🚫 Removed").ToJsonString()).RootElement;

        Assert.Equal(1, updated.GetArrayLength());
        Assert.Equal("context", updated[0].GetProperty("type").GetString());
    }

    // ── button value round-trips with the callback's split ──
    [Fact]
    public void Button_value_splits_into_mapping_and_comment_id()
    {
        var mappingId = Guid.NewGuid();
        const string commentId = "Ugx_abc.def-123"; // YouTube reply ids contain '.' and '-', never ':'
        var value = $"{mappingId}:{commentId}";

        var sep = value.IndexOf(':');
        Assert.True(sep > 0);
        Assert.True(Guid.TryParse(value[..sep], out var parsed));
        Assert.Equal(mappingId, parsed);
        Assert.Equal(commentId, value[(sep + 1)..]);
    }
}
