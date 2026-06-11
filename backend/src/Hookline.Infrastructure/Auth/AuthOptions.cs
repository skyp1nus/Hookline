namespace Hookline.Infrastructure.Auth;

/// <summary>Backend auth configuration (bound from the <c>Auth</c> / <c>BackendAuth</c> sections).</summary>
public sealed class AuthOptions
{
    /// <summary>Shared secret proving the request comes from the trusted BFF (header <c>X-Admin-Token</c>).</summary>
    public string AdminToken { get; set; } = string.Empty;

    /// <summary>Dedicated HMAC key for the BFF→backend identity assertion (NOT the AES master key).</summary>
    public string IdentitySigningKey { get; set; } = string.Empty;

    /// <summary>Fast-dev escape hatch: skip the BFF token + identity checks and run as a dev admin.</summary>
    public bool DevNoAuth { get; set; }

    /// <summary>Header carrying the short-TTL signed identity assertion.</summary>
    public string IdentityHeader { get; set; } = "X-Hookline-Identity";
}

/// <summary>First-run bootstrap admin seeding (env-only path into a fresh prod DB).</summary>
public sealed class BootstrapOptions
{
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
}
