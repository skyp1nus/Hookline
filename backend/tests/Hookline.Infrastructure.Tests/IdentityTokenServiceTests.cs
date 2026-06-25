using Hookline.Infrastructure.Auth;
using Hookline.SharedKernel.Auth;

namespace Hookline.Infrastructure.Tests;

public sealed class IdentityTokenServiceTests
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";
    private readonly IdentityTokenService _svc = new(Key);

    [Fact]
    public void Constructor_throws_when_signing_key_is_null()
    {
        Assert.Throws<InvalidOperationException>(() => new IdentityTokenService(null));
    }

    [Fact]
    public void Constructor_throws_when_signing_key_is_empty()
    {
        Assert.Throws<InvalidOperationException>(() => new IdentityTokenService(""));
    }

    [Fact]
    public void Constructor_throws_when_signing_key_is_whitespace()
    {
        Assert.Throws<InvalidOperationException>(() => new IdentityTokenService("   "));
    }

    [Fact]
    public void Sign_produces_two_part_token()
    {
        var token = _svc.Sign(Guid.NewGuid(), UserRole.Member, TimeSpan.FromMinutes(5));

        Assert.Contains('.', token);
        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.NotEmpty(parts[0]);
        Assert.NotEmpty(parts[1]);
    }

    [Fact]
    public void Verify_roundtrips_user_and_role()
    {
        var userId = Guid.NewGuid();
        var token = _svc.Sign(userId, UserRole.Admin, TimeSpan.FromMinutes(5));

        var identity = _svc.Verify(token);

        Assert.NotNull(identity);
        Assert.Equal(userId, identity.UserId);
        Assert.Equal(UserRole.Admin, identity.Role);
        Assert.True(identity.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Verify_returns_null_for_null_token()
    {
        Assert.Null(_svc.Verify(null));
    }

    [Fact]
    public void Verify_returns_null_for_empty_token()
    {
        Assert.Null(_svc.Verify(""));
    }

    [Fact]
    public void Verify_returns_null_when_no_dot()
    {
        Assert.Null(_svc.Verify("nodothere"));
    }

    [Fact]
    public void Verify_returns_null_when_dot_at_start()
    {
        Assert.Null(_svc.Verify(".signature"));
    }

    [Fact]
    public void Verify_returns_null_when_dot_at_end()
    {
        Assert.Null(_svc.Verify("payload."));
    }

    [Fact]
    public void Verify_rejects_tampered_signature()
    {
        var token = _svc.Sign(Guid.NewGuid(), UserRole.Member, TimeSpan.FromMinutes(5));
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(_svc.Verify(tampered));
    }

    [Fact]
    public void Verify_rejects_token_signed_with_different_key()
    {
        var other = new IdentityTokenService("a-completely-different-signing-key!!");
        var token = other.Sign(Guid.NewGuid(), UserRole.Owner, TimeSpan.FromMinutes(5));

        Assert.Null(_svc.Verify(token));
    }

    [Fact]
    public void Verify_rejects_expired_token()
    {
        var token = _svc.Sign(Guid.NewGuid(), UserRole.Member, TimeSpan.FromSeconds(-1));

        Assert.Null(_svc.Verify(token));
    }
}
