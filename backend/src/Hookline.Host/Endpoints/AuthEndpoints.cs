using Hookline.Infrastructure.Auth;
using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hookline.Host.Endpoints;

public static class AuthEndpoints
{
    public static void MapHooklineAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Lets the BFF decide whether to show the one-time Create-Owner flow.
        group.MapGet("/bootstrap-state", async (UserService users) =>
        {
            var ownerExists = await users.OwnerExistsAsync();
            var all = await users.ListAsync();
            return Results.Ok(new { ownerExists, userCount = all.Count });
        });

        // The BFF validates here, then mints the session cookie + identity assertion.
        group.MapPost("/login", async (LoginRequest request, UserService users) =>
        {
            var user = await users.ValidateCredentialsAsync(request.Email, request.Password);
            return user is null
                ? Results.Problem(statusCode: 401, title: "invalid_credentials", detail: "Invalid email or password.")
                : Results.Ok(ToDto(user));
        });

        group.MapPost("/logout", () => Results.NoContent());

        group.MapGet("/me", (ICurrentUser current) => current.IsAuthenticated
            ? Results.Ok(new { id = current.UserId, email = current.Email, role = current.Role?.ToString(), isSystem = current.IsSystem })
            : Results.Problem(statusCode: 401, title: "unauthorized"));

        group.MapGet("/users", async (ICurrentUser current, UserService users) =>
        {
            if (!current.HasAtLeast(UserRole.Admin))
            {
                return Forbidden();
            }

            var all = await users.ListAsync();
            return Results.Ok(all.Select(ToDto));
        });

        group.MapPost("/users", async (CreateUserRequest request, ICurrentUser current, UserService users) =>
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.Problem(statusCode: 400, title: "validation", detail: "Unknown role.");
            }

            return ToResult(await users.CreateUserAsync(current, request.Email, request.Password, role));
        });

        // One-time Create-Owner. Creating a second Owner is rejected server-side
        // (race-safe partial unique index → 409).
        group.MapPost("/owner", async (CreateOwnerRequest request, ICurrentUser current, UserService users) =>
            ToResult(await users.CreateOwnerAsync(current, request.Email, request.Password)));
    }

    private static IResult ToResult(Result<User> result) =>
        result.IsSuccess
            ? Results.Ok(ToDto(result.Value!))
            : Results.Problem(statusCode: result.Error!.Status, title: result.Error.Code, detail: result.Error.Message);

    private static IResult Forbidden() =>
        Results.Problem(statusCode: 403, title: "forbidden", detail: "You do not have permission to do that.");

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.Role.ToString(), user.Status.ToString(), user.LastLoginAt);

    private sealed record LoginRequest(string Email, string Password);
    private sealed record CreateUserRequest(string Email, string Password, string Role);
    private sealed record CreateOwnerRequest(string Email, string Password);
    private sealed record UserDto(Guid Id, string Email, string Role, string Status, DateTimeOffset? LastLoginAt);
}
