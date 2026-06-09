using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Http;

namespace Hookline.Infrastructure.Auth;

/// <summary>Concrete <see cref="ICurrentUser"/> value.</summary>
public sealed record HooklineUser(
    bool IsAuthenticated,
    Guid? UserId,
    string? Email,
    UserRole? Role,
    bool IsSystem) : ICurrentUser
{
    public bool HasAtLeast(UserRole role) => Role is { } r && r >= role;

    public static readonly HooklineUser Anonymous = new(false, null, null, null, false);

    /// <summary>The internal principal used by background jobs (full power, no HTTP context).</summary>
    public static readonly HooklineUser System = new(true, null, "system", UserRole.Owner, true);

    public static HooklineUser Authenticated(Guid id, string? email, UserRole role) =>
        new(true, id, email, role, false);
}

/// <summary>
/// Resolves the caller per request from <see cref="HttpContext.Items"/> (set by the
/// identity middleware), or the system principal when there is no HTTP context
/// (i.e. inside a background job).
/// </summary>
public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string ItemsKey = "hookline.current-user";

    private ICurrentUser Resolve()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return HooklineUser.System;
        }

        return ctx.Items.TryGetValue(ItemsKey, out var value) && value is ICurrentUser user
            ? user
            : HooklineUser.Anonymous;
    }

    public static void Set(HttpContext context, ICurrentUser user) => context.Items[ItemsKey] = user;

    public bool IsAuthenticated => Resolve().IsAuthenticated;
    public Guid? UserId => Resolve().UserId;
    public string? Email => Resolve().Email;
    public UserRole? Role => Resolve().Role;
    public bool IsSystem => Resolve().IsSystem;
    public bool HasAtLeast(UserRole role) => Resolve().HasAtLeast(role);
}
