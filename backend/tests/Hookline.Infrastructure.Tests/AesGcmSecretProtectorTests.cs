using System.Security.Cryptography;

using Hookline.Infrastructure.Secrets;

namespace Hookline.Infrastructure.Tests;

/// <summary>
/// Pins the security-critical contract of the shared secret protector: real AES-256-GCM round-trip,
/// a fresh nonce per call, the 0x01 version-byte header, authenticated-encryption tamper rejection,
/// key isolation, and fail-fast on a missing master key. These are the guarantees every stored token
/// (Slack bot token, Google refresh token, project client secret, API key) depends on.
/// </summary>
public sealed class AesGcmSecretProtectorTests
{
    private const string MasterKey = "test-master-key-please-change";

    private static AesGcmSecretProtector NewProtector() => new(MasterKey);

    [Fact]
    public void Roundtrip_returns_the_original_plaintext()
    {
        var p = NewProtector();
        const string secret = "super-secret-refresh-token-✓-1234";

        var cipher = p.Protect(secret);

        Assert.NotEqual(secret, cipher);            // genuinely encrypted, not a passthrough
        Assert.Equal(secret, p.Unprotect(cipher));  // and reversible
    }

    [Fact]
    public void Protect_uses_a_fresh_nonce_so_ciphertext_differs_each_call()
    {
        var p = NewProtector();

        // Same plaintext encrypted twice must differ — a fresh 96-bit CSPRNG nonce per call.
        Assert.NotEqual(p.Protect("same-plaintext"), p.Protect("same-plaintext"));
    }

    [Fact]
    public void Ciphertext_starts_with_the_0x01_version_byte()
    {
        var p = NewProtector();

        var bytes = Convert.FromBase64String(p.Protect("anything"));

        Assert.Equal(0x01, bytes[0]);
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected_by_the_auth_tag()
    {
        var p = NewProtector();
        var bytes = Convert.FromBase64String(p.Protect("tamper-me"));
        bytes[^1] ^= 0xFF; // flip a ciphertext byte → GCM tag verification must fail
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(tampered));
        Assert.False(p.TryUnprotect(tampered, out var plain));
        Assert.Equal(string.Empty, plain);
    }

    [Fact]
    public void An_unsupported_version_byte_is_rejected()
    {
        var p = NewProtector();
        var bytes = Convert.FromBase64String(p.Protect("v"));
        bytes[0] = 0x02; // a version this build does not understand

        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void A_different_master_key_cannot_decrypt()
    {
        var cipher = new AesGcmSecretProtector("key-A").Protect("secret");
        var other = new AesGcmSecretProtector("key-B");

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(cipher));
        Assert.False(other.TryUnprotect(cipher, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_master_key_fails_fast(string? key)
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(key));
    }

    [Fact]
    public void TryUnprotect_on_non_base64_garbage_returns_false_without_throwing()
    {
        var p = NewProtector();

        Assert.False(p.TryUnprotect("not-base64-!!!", out var plain));
        Assert.Equal(string.Empty, plain);
    }
}
