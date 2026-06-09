using System.Security.Cryptography;
using System.Text;

using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Hookline.Infrastructure.Auth;

/// <summary>
/// The single auth gate. Bypass allowlist = exactly <c>/slack/*</c>, <c>/google/*</c>,
/// <c>/linkedin/*</c> and <c>/health</c> (signature/state-verified or public).
/// Everything else must present a valid <c>X-Admin-Token</c> (proves the BFF is calling —
/// it does NOT establish identity), after which the signed identity assertion is verified
/// to resolve <see cref="ICurrentUser"/>. Endpoints then enforce role policies.
/// The <c>DevNoAuth</c> escape hatch is bound off outside Development (see
/// <c>DependencyInjection.GuardSecurityConfig</c>), so this gate is never weakened in prod.
/// </summary>
public sealed class IdentityMiddleware(
    RequestDelegate next,
    IOptions<AuthOptions> options,
    IdentityTokenService identityTokens)
{
    private static readonly string[] BypassPrefixes =
        ["/slack", "/google", "/linkedin", "/health"];

    private readonly AuthOptions _options = options.Value;
    private readonly byte[] _adminTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.AdminToken));

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (IsBypassed(path))
        {
            CurrentUserAccessor.Set(context, HooklineUser.Anonymous);
            await next(context);
            return;
        }

        // Fast-dev: no BFF token / identity required; run as a dev admin.
        if (_options.DevNoAuth)
        {
            CurrentUserAccessor.Set(
                context,
                new HooklineUser(true, Guid.Empty, "dev@hookline.local", UserRole.Admin, false));
            await next(context);
            return;
        }

        // 1) Prove the caller is the trusted BFF (constant-time compare). NOT identity.
        if (!IsValidAdminToken(context.Request.Headers["X-Admin-Token"]))
        {
            await WriteProblem(context, StatusCodes.Status401Unauthorized, "bff_required",
                "This endpoint is only reachable through the Hookline BFF.");
            return;
        }

        // 2) Resolve identity from the short-TTL signed assertion (if present).
        var assertion = context.Request.Headers[_options.IdentityHeader].ToString();
        var identity = identityTokens.Verify(assertion);
        CurrentUserAccessor.Set(
            context,
            identity is null
                ? HooklineUser.Anonymous
                : HooklineUser.Authenticated(identity.UserId, email: null, identity.Role));

        await next(context);
    }

    private static bool IsBypassed(PathString path) =>
        BypassPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private bool IsValidAdminToken(string? provided)
    {
        if (string.IsNullOrEmpty(provided) || _options.AdminToken.Length == 0)
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(providedHash, _adminTokenHash);
    }

    private static Task WriteProblem(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = code,
            status,
            detail,
        });
    }
}
