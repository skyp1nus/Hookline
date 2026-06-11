using Hangfire.Dashboard;

using Hookline.SharedKernel.Auth;

using Microsoft.Extensions.DependencyInjection;

namespace Hookline.Infrastructure.Jobs;

/// <summary>
/// Gates the Hangfire dashboard behind real admin authorization. The identity middleware
/// has already resolved <see cref="ICurrentUser"/> for the request (the dashboard is not
/// in the auth bypass allowlist), so only an Admin+ may view it — never anonymous/local-only.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var user = http.RequestServices.GetService<ICurrentUser>();
        return user?.HasAtLeast(UserRole.Admin) == true;
    }
}
