using Hookline.DevTools.SlackHarness;
using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Proves the dev Slack harness (<see cref="SlackRequestSigner"/>) signs requests that the REAL,
/// fail-closed <see cref="SlackSignatureVerifier"/> accepts — byte-for-byte parity. If the harness's HMAC
/// base string ever drifts from the verifier's, this fails the build instead of silently producing 401s
/// at the live <c>/slack/youtube-comments/interactivity</c> endpoint. Covers the INTERACTIVITY
/// (form-encoded <c>payload=…</c> body) surface used by the "Reject on YouTube" button.
/// </summary>
public class SlackHarnessParityTests
{
    private const string Secret = "comments-harness-secret-1a2b3c4d5e6f";

    [Fact]
    public void Harness_signed_interactivity_body_passes_the_real_verifier()
    {
        var verifier = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // The exact wire shape fire-reject sends: payload=<url-encoded block_actions JSON>.
        const string payloadJson =
            """{"type":"block_actions","actions":[{"action_id":"reject_comment","value":"00000000-0000-0000-0000-000000000000:CMT1"}]}""";
        var body = "payload=" + Uri.EscapeDataString(payloadJson);

        var signature = SlackRequestSigner.Sign(Secret, ts, body);

        Assert.True(verifier.Verify(Secret, ts, body, signature));
    }

    [Fact]
    public void Tampering_the_body_after_signing_is_rejected()
    {
        var verifier = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string body = "payload=%7B%7D";

        var signature = SlackRequestSigner.Sign(Secret, ts, body);

        Assert.False(verifier.Verify(Secret, ts, body + "&injected=1", signature));
    }
}
