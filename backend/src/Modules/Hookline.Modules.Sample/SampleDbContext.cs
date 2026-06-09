using Hookline.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookline.Modules.Sample;

/// <summary>A ping row — just enough to give the module a real schema + migration.</summary>
public sealed class SamplePing
{
    public long Id { get; set; }
    public DateTimeOffset PingedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The sample module's context (schema <c>_sample</c>, deleted in Phase 1).</summary>
public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : HooklineDbContext(options)
{
    public const string SchemaName = "_sample";

    public DbSet<SamplePing> Pings => Set<SamplePing>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);
        var ping = model.Entity<SamplePing>();
        ping.ToTable("sample_pings");
        ping.HasKey(p => p.Id);
        ping.Property(p => p.Id).ValueGeneratedOnAdd();
    }
}

public sealed class SampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    public SampleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=hookline;Username=hookline;Password=design-time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", SampleDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SampleDbContext(options);
    }
}
