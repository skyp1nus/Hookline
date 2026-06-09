using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hookline.SharedKernel.Modules;

/// <summary>
/// The contract every module implements. The host discovers modules from an
/// explicit list (no reflection scanning) and drives them through this surface.
/// Adding a module to the system is one line in that list.
/// </summary>
public interface IModule
{
    /// <summary>Stable kebab-case identifier, e.g. "comment-bridge". Routes live under <c>/api/{Name}</c>.</summary>
    string Name { get; }

    /// <summary>Register the module's services into the shared container.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration config);

    /// <summary>Map the module's HTTP endpoints (conventionally under <c>/api/{Name}</c>).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Register recurring jobs via the shared scheduler.</summary>
    void RegisterJobs(IJobScheduler scheduler);

    /// <summary>The external connections (Slack / Google / …) this module needs.</summary>
    IEnumerable<ConnectionRequirement> RequiredConnections { get; }

    /// <summary>
    /// Returns the module's <see cref="DbContext"/> so the host can apply its
    /// migrations under an advisory lock, or <c>null</c> if the module has no schema.
    /// </summary>
    DbContext? Migrate(IServiceProvider services);
}
