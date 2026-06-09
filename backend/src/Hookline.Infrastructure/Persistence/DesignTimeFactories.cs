using Hookline.Infrastructure.Auth;
using Hookline.Infrastructure.Connections;
using Hookline.Infrastructure.Secrets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookline.Infrastructure.Persistence;

// Design-time factories for `dotnet ef migrations add`. The connection string is a
// placeholder — `migrations add` does not connect; it only generates the model diff.

internal static class DesignTime
{
    public const string PlaceholderConnection =
        "Host=localhost;Port=5432;Database=hookline;Username=hookline;Password=design-time";

    public static DbContextOptionsBuilder<TContext> Build<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(PlaceholderConnection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();
}

public sealed class SharedDbContextFactory : IDesignTimeDbContextFactory<SharedDbContext>
{
    public SharedDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Build<SharedDbContext>(SharedDbContext.SchemaName).Options);
}

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Build<AuthDbContext>(AuthDbContext.SchemaName).Options);
}

public sealed class ConnectionsDbContextFactory : IDesignTimeDbContextFactory<ConnectionsDbContext>
{
    public ConnectionsDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Build<ConnectionsDbContext>(ConnectionsDbContext.SchemaName).Options, new PassthroughSecretProtector());
}
