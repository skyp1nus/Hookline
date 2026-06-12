using System.Text.Json;

using Hookline.DevTools.SlackHarness;
using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// Proves the dev Slack harness (<see cref="SlackRequestSigner"/>) signs requests that the REAL,
/// fail-closed <see cref="SlackSignatureVerifier"/> accepts — byte-for-byte parity. If the harness's HMAC
/// base string ever drifts from the verifier's, this fails the build instead of silently producing 401s
/// at the live <c>/slack/youtube-uploads/events</c> endpoint. Covers the EVENTS (raw JSON body) surface.
/// </summary>
public class SlackHarnessParityTests
{
    private const string Secret = "uploads-harness-secret-9f8e7d6c5b4a";

    [Fact]
    public void Harness_signed_event_body_passes_the_real_verifier()
    {
        var verifier = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var body = JsonSerializer.Serialize(new
        {
            type = "event_callback",
            event_id = "EvHARNESS1",
            @event = new { type = "message", channel = "C123", user = "U123", ts = "1700000000.0001", text = "hello" },
        });

        var signature = SlackRequestSigner.Sign(Secret, ts, body);

        Assert.True(verifier.Verify(Secret, ts, body, signature));
    }

    [Fact]
    public void A_signature_under_a_different_secret_is_rejected()
    {
        var verifier = new SlackSignatureVerifier();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string body = "{}";

        var signature = SlackRequestSigner.Sign("the-wrong-secret", ts, body);

        Assert.False(verifier.Verify(Secret, ts, body, signature));
    }
}
