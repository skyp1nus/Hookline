using Microsoft.EntityFrameworkCore;

namespace Hookline.SharedKernel.Persistence;

/// <summary>
/// Base for every module/shared <see cref="DbContext"/>. Each derived context maps
/// to its own schema with its own migration history (configured at registration via
/// <c>UseSnakeCaseNamingConvention()</c> + <c>MigrationsHistoryTable</c>). UTC is
/// enforced here so timestamps are consistent across the hub.
/// </summary>
public abstract class HooklineDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        // Persist DateTime as UTC; Npgsql maps DateTimeOffset to timestamptz already.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
