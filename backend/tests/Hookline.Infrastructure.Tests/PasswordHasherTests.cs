using Hookline.Infrastructure.Auth;

namespace Hookline.Infrastructure.Tests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_produces_bcrypt_format()
    {
        var hash = _hasher.Hash("password123");

        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void Hash_is_nondeterministic()
    {
        var h1 = _hasher.Hash("same");
        var h2 = _hasher.Hash("same");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _hasher.Hash("secret");

        Assert.True(_hasher.Verify("secret", hash));
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _hasher.Hash("correct");

        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Verify_returns_false_for_invalid_hash()
    {
        Assert.False(_hasher.Verify("anything", "not-a-bcrypt-hash"));
    }

    [Fact]
    public void Verify_throws_for_empty_hash()
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.Verify("anything", ""));
    }
}
