using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Modules;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hookline.Modules.Sample;

/// <summary>
/// The reference module. It exists only to prove the <see cref="IModule"/> contract
/// end-to-end (services + endpoint + recurring job + migrated schema) and is removed in
/// Phase 1 when the real modules land. Cost of a new module = exactly this file + its
/// folder + one nav entry, with zero edits to existing modules.
/// </summary>
public sealed class SampleModule : IModule
{
    public string Name => "_sample";

    public IEnumerable<ConnectionRequirement> RequiredConnections => [];

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddDbContext<SampleDbContext>(options => options
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", SampleDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<SamplePingJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{Name}");
        group.MapGet("/ping", () => Results.Ok(new
        {
            module = "_sample",
            status = "ok",
            time = DateTimeOffset.UtcNow,
        }));
    }

    public void RegisterJobs(IJobScheduler scheduler) =>
        scheduler.AddOrUpdateRecurring<SamplePingJob>("_sample.heartbeat", job => job.RunAsync(), "*/15 * * * *");

    public DbContext Migrate(IServiceProvider services) =>
        services.GetRequiredService<SampleDbContext>();
}
