using Hangfire;
using Hangfire.PostgreSql;

using Hookline.Infrastructure.Audit;
using Hookline.Infrastructure.Auth;
using Hookline.Infrastructure.Connections;
using Hookline.Infrastructure.Health;
using Hookline.Infrastructure.Jobs;
using Hookline.Infrastructure.Messaging;
using Hookline.Infrastructure.Persistence;
using Hookline.Infrastructure.Secrets;
using Hookline.Infrastructure.Settings;

using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Messaging;
using Hookline.SharedKernel.Modules;
using Hookline.SharedKernel.Secrets;
using Hookline.SharedKernel.Settings;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace Hookline.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers every shared service the modules and host consume.</summary>
    public static IServiceCollection AddHooklineInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        var redis = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

        services.Configure<AuthOptions>(o =>
        {
            o.AdminToken = config["BackendAuth:AdminToken"] ?? string.Empty;
            o.IdentitySigningKey = config["Identity:SigningKey"] ?? string.Empty;
            o.DevNoAuth = config.GetValue<bool>("Auth:DevNoAuth");
        });
        services.Configure<BootstrapOptions>(o =>
        {
            o.AdminEmail = config["Bootstrap:AdminEmail"] ?? string.Empty;
            o.AdminPassword = config["Bootstrap:AdminPassword"] ?? string.Empty;
        });

        // Security primitives (fail fast on missing keys).
        services.AddSingleton<ISecretProtector>(_ => new AesGcmSecretProtector(config["TokenEncryption:Key"]));
        services.AddSingleton(_ => new IdentityTokenService(config["Identity:SigningKey"]));
        services.AddSingleton<PasswordHasher>();

        // Redis (lazy, never aborts the app on a transient outage).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserAccessor>();

        void ConfigureContext(DbContextOptionsBuilder builder, string schema) => builder
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

        services.AddDbContext<SharedDbContext>(o => ConfigureContext(o, SharedDbContext.SchemaName));
        services.AddDbContext<AuthDbContext>(o => ConfigureContext(o, AuthDbContext.SchemaName));
        services.AddDbContext<ConnectionsDbContext>(o => ConfigureContext(o, ConnectionsDbContext.SchemaName));

        services.AddScoped<ISlackConnections, SlackConnections>();
        services.AddScoped<IGoogleConnections, GoogleConnections>();
        services.AddScoped<IConnectionCatalog, ConnectionCatalog>();
        services.AddScoped<ISettingsStore, SettingsStore>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<UserService>();
        services.AddScoped<OAuthFlowService>();

        services.AddSingleton<IEventBus, InProcessEventBus>();
        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(postgres)));
        services.AddHangfireServer();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres")
            .AddCheck<RedisHealthCheck>("redis");

        return services;
    }

    /// <summary>The single auth gate (BFF token + signed identity). Place early in the pipeline.</summary>
    public static IApplicationBuilder UseHooklineIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<IdentityMiddleware>();

    /// <summary>
    /// Migrate the shared contexts + every module's context under one advisory lock.
    /// Throws (host fails to start) if any migration fails.
    /// </summary>
    public static async Task MigrateHooklineAsync(this IServiceProvider rootServices, IReadOnlyList<IModule> modules)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfiguration>();
        var postgres = config.GetConnectionString("Postgres")!;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Hookline.Migrations");

        var contexts = new List<DbContext>
        {
            sp.GetRequiredService<SharedDbContext>(),
            sp.GetRequiredService<AuthDbContext>(),
            sp.GetRequiredService<ConnectionsDbContext>(),
        };
        foreach (var module in modules)
        {
            if (module.Migrate(sp) is { } moduleContext)
            {
                contexts.Add(moduleContext);
            }
        }

        await DbMigrator.MigrateAsync(postgres, contexts, logger);
    }

    /// <summary>Seed the first-run bootstrap admin (idempotent) after migrations.</summary>
    public static async Task SeedBootstrapAdminAsync(this IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<UserService>().BootstrapAsync();
    }
}
