using Hookline.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Hookline.Infrastructure.Tests;

/// <summary>
/// <see cref="DependencyInjection.GuardSecurityConfig"/> is the boot-time backstop: outside Development
/// it refuses to start if any required secret is empty or a known dev placeholder. These tests pin the
/// module Slack signing secrets into that required set — the D7 prod-silent-break fix. Both modules
/// always map their <c>/slack/.../interactivity</c> callback and the verifier is fail-closed, so a
/// forgotten signing secret would otherwise 401 every "Reject on YouTube" press INVISIBLY in prod. The
/// guard now turns that silent misconfig into a fast, loud boot failure.
/// </summary>
public class GuardSecurityConfigTests
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Hookline.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>A full set of valid (non-empty, non-placeholder) secrets; each test blanks ONE out.</summary>
    private static Dictionary<string, string?> ValidSecrets() => new()
    {
        ["TokenEncryption:Key"] = "strong-aes-key-7Kc2Qe9Rf4Tz8Wx1Yb3Vn6Md0Pa5Sg2",
        ["Identity:SigningKey"] = "strong-identity-3Hn8Lq2Wd5Rt9Yp1Zb4Vc7Mx0Ka6Sj3",
        ["BackendAuth:AdminToken"] = "strong-admin-9Fp2Qe5Rt8Wz1Yb4Vn7Mc0Ka3Sg6Hj9",
        ["YouTubeUploads:Slack:SigningSecret"] = "uploads-signing-secret-abc123",
        ["YouTubeComments:Slack:SigningSecret"] = "comments-signing-secret-def456",
    };

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Development_skips_the_guard_entirely()
    {
        // Even with everything empty, Development never trips the guard.
        var ex = Record.Exception(() => DependencyInjection.GuardSecurityConfig(
            Config(new Dictionary<string, string?>()), new FakeEnv(Environments.Development)));

        Assert.Null(ex);
    }

    [Fact]
    public void All_required_secrets_present_passes_in_production()
    {
        var ex = Record.Exception(() => DependencyInjection.GuardSecurityConfig(
            Config(ValidSecrets()), new FakeEnv(Environments.Production)));

        Assert.Null(ex);
    }

    [Fact]
    public void Empty_comments_signing_secret_fails_fast_in_production()
    {
        var values = ValidSecrets();
        values["YouTubeComments:Slack:SigningSecret"] = ""; // the exact D7 gap: var never passed → default ""

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeComments:Slack:SigningSecret", ex.Message);
    }

    [Fact]
    public void Missing_comments_signing_secret_fails_fast_in_production()
    {
        var values = ValidSecrets();
        values.Remove("YouTubeComments:Slack:SigningSecret"); // key absent entirely (env var omitted)

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeComments:Slack:SigningSecret", ex.Message);
    }

    [Fact]
    public void Placeholder_comments_signing_secret_fails_fast_in_production()
    {
        var values = ValidSecrets();
        values["YouTubeComments:Slack:SigningSecret"] = "CHANGE-ME"; // copied-but-unfilled prod template

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeComments:Slack:SigningSecret", ex.Message);
    }

    [Fact]
    public void Empty_uploads_signing_secret_also_fails_fast_in_production()
    {
        // Symmetry: the same guard now covers the Uploads interactivity secret too.
        var values = ValidSecrets();
        values["YouTubeUploads:Slack:SigningSecret"] = "";

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeUploads:Slack:SigningSecret", ex.Message);
    }

    // ── dev-only Slack Socket Mode: must be OFF in Production (mirrors Auth:DevNoAuth) ──

    [Fact]
    public void Uploads_socket_mode_enabled_fails_fast_in_production()
    {
        var values = ValidSecrets();
        values["YouTubeUploads:Slack:SocketMode:Enabled"] = "true";

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeUploads:Slack:SocketMode:Enabled", ex.Message);
    }

    [Fact]
    public void Comments_socket_mode_enabled_fails_fast_in_production()
    {
        var values = ValidSecrets();
        values["YouTubeComments:Slack:SocketMode:Enabled"] = "true";

        var ex = Assert.Throws<InvalidOperationException>(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Contains("YouTubeComments:Slack:SocketMode:Enabled", ex.Message);
    }

    [Fact]
    public void Socket_mode_off_keeps_production_boot_clean()
    {
        // Both flags explicitly false (the docker-compose default) — the guard must NOT trip.
        var values = ValidSecrets();
        values["YouTubeUploads:Slack:SocketMode:Enabled"] = "false";
        values["YouTubeComments:Slack:SocketMode:Enabled"] = "false";

        var ex = Record.Exception(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Production)));

        Assert.Null(ex);
    }

    [Fact]
    public void Development_allows_socket_mode_enabled()
    {
        // Socket Mode is a dev feature — Development must accept it (the guard returns early there).
        var values = ValidSecrets();
        values["YouTubeUploads:Slack:SocketMode:Enabled"] = "true";
        values["YouTubeComments:Slack:SocketMode:Enabled"] = "true";

        var ex = Record.Exception(() => DependencyInjection.GuardSecurityConfig(
            Config(values), new FakeEnv(Environments.Development)));

        Assert.Null(ex);
    }
}
