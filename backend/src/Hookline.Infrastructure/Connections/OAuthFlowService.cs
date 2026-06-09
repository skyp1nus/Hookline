using System.Security.Cryptography;

using Hookline.SharedKernel.Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace Hookline.Infrastructure.Connections;

/// <summary>
/// OAuth start/callback skeleton owned by the Connections subsystem. Phase 0 implements
/// the CSRF-safe state lifecycle (generate → store in Redis with a short TTL → verify);
/// the provider-specific code exchange + token storage land when the modules are absorbed.
/// </summary>
public sealed class OAuthFlowService(
    IConnectionMultiplexer redis,
    IConfiguration config,
    ILogger<OAuthFlowService> logger)
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    private static readonly string[] KnownProviders = ["slack", "google"];

    /// <summary>Generate state, persist it, and return the provider's authorize URL.</summary>
    public async Task<Result<string>> StartAsync(string provider, string? returnUrl, CancellationToken ct = default)
    {
        provider = provider.ToLowerInvariant();
        if (!KnownProviders.Contains(provider))
        {
            return Error.Validation($"Unknown OAuth provider '{provider}'.");
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await redis.GetDatabase().StringSetAsync(
            RedisKeys.OAuthState(provider, state),
            returnUrl ?? "/",
            StateTtl);

        var clientId = config[$"{Capitalize(provider)}:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            // Skeleton: the provider isn't configured yet (real wiring is Phase 1).
            logger.LogInformation("OAuth start requested for {Provider} but no client is configured.", provider);
            return Error.Conflict($"The {provider} connection is not configured yet.");
        }

        var redirectUri = config[$"{Capitalize(provider)}:RedirectUri"] ?? string.Empty;
        var authorizeUrl = provider switch
        {
            "slack" => $"https://slack.com/oauth/v2/authorize?client_id={clientId}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "google" => $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&access_type=offline&prompt=consent",
            _ => string.Empty,
        };

        return authorizeUrl;
    }

    /// <summary>Verify the returned state (one-shot) before any code exchange.</summary>
    public async Task<Result<string>> VerifyStateAsync(string provider, string state, CancellationToken ct = default)
    {
        provider = provider.ToLowerInvariant();
        var key = RedisKeys.OAuthState(provider, state);
        var returnUrl = await redis.GetDatabase().StringGetDeleteAsync(key);
        if (returnUrl.IsNullOrEmpty)
        {
            return Error.Validation("Invalid or expired OAuth state.");
        }

        // Phase 1 will exchange the code for tokens and persist the connection here.
        return returnUrl.ToString();
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
