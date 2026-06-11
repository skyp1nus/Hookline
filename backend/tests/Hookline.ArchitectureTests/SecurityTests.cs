using System.Security.Cryptography;

using Hookline.Infrastructure.Auth;
using Hookline.Infrastructure.Secrets;
using Hookline.SharedKernel.Auth;

namespace Hookline.ArchitectureTests;

/// <summary>Guards the crypto invariants: AES-GCM authentication + signed-identity verification.</summary>
public sealed class SecurityTests
{
    private readonly AesGcmSecretProtector _protector = new("unit-test-master-key");

    [Fact]
    public void Protector_round_trips()
    {
        const string secret = "xoxb-super-secret-token-🔒";
        Assert.Equal(secret, _protector.Unprotect(_protector.Protect(secret)));
    }

    [Fact]
    public void Protector_uses_a_fresh_nonce_per_encryption()
    {
        Assert.NotEqual(_protector.Protect("same"), _protector.Protect("same"));
    }

    [Fact]
    public void Protector_emits_the_versioned_layout()
    {
        var bytes = Convert.FromBase64String(_protector.Protect("x"));
        Assert.Equal(0x01, bytes[0]);                 // version byte
        Assert.True(bytes.Length >= 1 + 12 + 16 + 1); // version + nonce + tag + ≥1 ciphertext byte
    }

    [Fact]
    public void Protector_rejects_a_tampered_tag()
    {
        var bytes = Convert.FromBase64String(_protector.Protect("tamper-me"));
        bytes[^1] ^= 0xFF; // flip a ciphertext byte
        var tampered = Convert.ToBase64String(bytes);

        // AES-GCM throws AuthenticationTagMismatchException (a CryptographicException subclass).
        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(tampered));
        Assert.False(_protector.TryUnprotect(tampered, out _));
    }

    [Fact]
    public void Protector_fails_fast_without_a_key() =>
        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(""));

    [Fact]
    public void Identity_token_round_trips()
    {
        var service = new IdentityTokenService("dedicated-identity-key");
        var userId = Guid.NewGuid();

        var token = service.Sign(userId, UserRole.Owner, TimeSpan.FromMinutes(2));
        var verified = service.Verify(token);

        Assert.NotNull(verified);
        Assert.Equal(userId, verified!.UserId);
        Assert.Equal(UserRole.Owner, verified.Role);
    }

    [Fact]
    public void Identity_token_rejects_expiry()
    {
        var service = new IdentityTokenService("dedicated-identity-key");
        var token = service.Sign(Guid.NewGuid(), UserRole.Admin, TimeSpan.FromSeconds(-1));
        Assert.Null(service.Verify(token));
    }

    [Fact]
    public void Identity_token_rejects_a_foreign_signature()
    {
        var minted = new IdentityTokenService("key-a").Sign(Guid.NewGuid(), UserRole.Admin, TimeSpan.FromMinutes(2));
        Assert.Null(new IdentityTokenService("key-b").Verify(minted));
    }

    [Fact]
    public void Identity_token_rejects_garbage() =>
        Assert.Null(new IdentityTokenService("key").Verify("not-a-token"));
}
