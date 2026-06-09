using Hookline.SharedKernel.Persistence;
using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Infrastructure.Connections;

/// <summary>
/// Connections schema — the shared external-credential store. Every secret column is
/// encrypted at rest through <see cref="ISecretProtector"/>. Multiple credential sets
/// per provider are supported (rows).
/// </summary>
public sealed class ConnectionsDbContext(
    DbContextOptions<ConnectionsDbContext> options,
    ISecretProtector protector) : HooklineDbContext(options)
{
    public const string SchemaName = "connections";

    public DbSet<SlackWorkspace> SlackWorkspaces => Set<SlackWorkspace>();
    public DbSet<GoogleAccount> GoogleAccounts => Set<GoogleAccount>();
    public DbSet<YouTubeApiKey> YouTubeApiKeys => Set<YouTubeApiKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);

        var slack = model.Entity<SlackWorkspace>();
        slack.ToTable("slack_workspaces");
        slack.HasKey(w => w.Id);
        slack.HasIndex(w => w.TeamId).IsUnique();
        slack.Property(w => w.TeamId).IsRequired().HasMaxLength(64);
        slack.Property(w => w.TeamName).HasMaxLength(200);
        slack.Property(w => w.BotTokenEncrypted).IsEncrypted(protector);

        var google = model.Entity<GoogleAccount>();
        google.ToTable("google_accounts");
        google.HasKey(g => g.Id);
        google.Property(g => g.ChannelTitle).HasMaxLength(200);
        google.Property(g => g.RefreshTokenEncrypted).IsEncrypted(protector);

        var key = model.Entity<YouTubeApiKey>();
        key.ToTable("api_keys");
        key.HasKey(k => k.Id);
        key.Property(k => k.Name).IsRequired().HasMaxLength(120);
        key.Property(k => k.KeyHint).HasMaxLength(40);
        key.Property(k => k.ApiKeyEncrypted).IsEncrypted(protector);
    }
}
