using Hookline.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Auth;

/// <summary>Auth schema — the hub's users.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : HooklineDbContext(options)
{
    public const string SchemaName = "auth";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);

        var user = model.Entity<User>();
        user.ToTable("users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Email).IsRequired().HasMaxLength(320);
        user.HasIndex(u => u.Email).IsUnique();
        user.Property(u => u.PasswordHash).IsRequired();
        user.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        user.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);

        // Race-safe single-Owner guarantee: a partial unique index means two concurrent
        // "create Owner" requests can never both succeed (one hits a unique violation).
        user.HasIndex(u => u.Role)
            .IsUnique()
            .HasFilter("role = 'Owner'")
            .HasDatabaseName("ux_users_single_owner");
    }
}
