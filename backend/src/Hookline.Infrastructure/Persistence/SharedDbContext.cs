using Hookline.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Persistence;

/// <summary>Cross-cutting shared schema — audit log + hub settings.</summary>
public sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : HooklineDbContext(options)
{
    public const string SchemaName = "shared";

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);

        var audit = model.Entity<AuditLogEntry>();
        audit.ToTable("audit_logs");
        audit.HasKey(a => a.Id);
        audit.Property(a => a.Id).ValueGeneratedOnAdd();
        audit.Property(a => a.Actor).IsRequired().HasMaxLength(320);
        audit.Property(a => a.Action).IsRequired().HasMaxLength(120);
        audit.Property(a => a.Module).HasMaxLength(60);
        audit.Property(a => a.EntityType).HasMaxLength(120);
        audit.Property(a => a.EntityId).HasMaxLength(200);
        audit.HasIndex(a => a.Timestamp);
        audit.HasIndex(a => a.Module);

        var setting = model.Entity<AppSetting>();
        setting.ToTable("app_settings");
        setting.HasKey(s => s.Key);
        setting.Property(s => s.Key).HasMaxLength(200);
        setting.Property(s => s.Value).IsRequired();
    }
}
