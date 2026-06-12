using System.Security.Cryptography;
using System.Text;

namespace Hookline.DevTools.SlackHarness;

/// <summary>
/// Computes Slack's <c>X-Slack-Signature</c> exactly like the modules' <c>SlackSignatureVerifier</c>:
/// <code>
///   base      = "v0:{timestamp}:{body}"
///   signature = "v0=" + lowercase-hex(HMAC_SHA256(signingSecret, base))
/// </code>
/// Kept byte-for-byte identical to the verifier. <c>SlackHarnessParityTests</c> in BOTH module test
/// projects feed this output through the REAL <c>SlackSignatureVerifier</c> to prove parity, so a drift
/// here fails the build rather than silently producing 401s.
/// </summary>
public static class SlackRequestSigner
{
    public static string Sign(string signingSecret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        return "v0=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"v0:{timestamp}:{body}"))).ToLowerInvariant();
    }
}
