using Hangfire;
using Hangfire.PostgreSql;

using Hookline.Infrastructure.Audit;
using Hookline.Infrastructure.Auth;
using Hookline.Infrastructure.Caching;
using Hookline.Infrastructure.Connections;
using Hookline.Infrastructure.Health;
using Hookline.Infrastructure.Jobs;
using Hookline.Infrastructure.Messaging;
using Hookline.Infrastructure.Persistence;
using Hookline.Infrastructure.Secrets;
using Hookline.Infrastructure.Settings;

using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Caching;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace Hookline.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers every shared service the modules and host consume.</summary>
    public static IServiceCollection AddHooklineInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        GuardSecurityConfig(config, env);

        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        var redis = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

        // DevNoAuth is honored ONLY in Development. Outside it the flag is forced off so a
        // stray config value can never disable auth (GuardSecurityConfig also refuses to boot
        // if it is set), and IdentityMiddleware therefore never impersonates a dev admin.
        var devNoAuth = config.GetValue<bool>("Auth:DevNoAuth") && env.IsDevelopment();

        services.Configure<AuthOptions>(o =>
        {
            o.AdminToken = config["BackendAuth:AdminToken"] ?? string.Empty;
            o.IdentitySigningKey = config["Identity:SigningKey"] ?? string.Empty;
            o.DevNoAuth = devNoAuth;
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

        // Prefix purge for the module data-reset path (best-effort; a cache outage never fails a reset).
        services.AddSingleton<ICachePurge, RedisCachePurge>();

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
        services.AddScoped<IYouTubeApiKeyConnections, YouTubeApiKeyConnections>();
        services.AddScoped<IConnectionCatalog, ConnectionCatalog>();
        services.AddScoped<ISettingsStore, SettingsStore>();
        services.AddScoped<AlertSettingsService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
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

    /// <summary>
    /// Fails the boot in any non-Development environment if auth is weakened: DevNoAuth must
    /// be off, and the security secrets must not be empty or a known dev placeholder. This is
    /// the loud backstop behind the silent DevNoAuth gating in <see cref="AddHooklineInfrastructure"/>.
    /// </summary>
    private static void GuardSecurityConfig(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            return;
        }

        if (config.GetValue<bool>("Auth:DevNoAuth"))
        {
            throw new InvalidOperationException(
                $"Auth:DevNoAuth=true is only allowed in Development (environment: {env.EnvironmentName}). Refusing to start.");
        }

        (string Key, string? Value)[] secrets =
        [
            ("TokenEncryption:Key", config["TokenEncryption:Key"]),
            ("Identity:SigningKey", config["Identity:SigningKey"]),
            ("BackendAuth:AdminToken", config["BackendAuth:AdminToken"]),
        ];

        foreach (var (key, value) in secrets)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Contains("change-me", StringComparison.OrdinalIgnoreCase)
                || value == "dev-admin-token")
            {
                throw new InvalidOperationException(
                    $"{key} is missing or a known dev placeholder — refusing to start outside Development. " +
                    "Generate a strong value, e.g. `openssl rand -base64 36`.");
            }
        }
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
