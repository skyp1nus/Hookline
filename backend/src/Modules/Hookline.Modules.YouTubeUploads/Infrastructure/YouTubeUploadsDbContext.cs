using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.SharedKernel.Persistence;
using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// YouTubeUploads module schema (<c>youtube_uploads</c>). Owns the upload pipeline (jobs + history),
/// channel→account mappings, the module-local channel cache, and the multi-OAuth-client
/// "Projects" + account bindings (rotation/quota). Slack workspaces + Google account records
/// live in the shared <c>connections</c> schema — referenced here only by plain id values.
/// The one encrypted column (project client secret) runs through the shared <c>ISecretProtector</c>.
/// </summary>
public sealed class YouTubeUploadsDbContext(
    DbContextOptions<YouTubeUploadsDbContext> options,
    ISecretProtector protector) : HooklineDbContext(options)
{
    public const string SchemaName = "youtube_uploads";

    public DbSet<UploadJob> Jobs => Set<UploadJob>();
    public DbSet<JobStateHistory> JobHistory => Set<JobStateHistory>();
    public DbSet<ChannelMapping> ChannelMappings => Set<ChannelMapping>();
    public DbSet<SlackChannel> SlackChannels => Set<SlackChannel>();
    public DbSet<GoogleProject> GoogleProjects => Set<GoogleProject>();
    public DbSet<GoogleAccountBinding> GoogleAccountBindings => Set<GoogleAccountBinding>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(SchemaName);

        var job = b.Entity<UploadJob>();
        job.ToTable("upload_jobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        // List<string> -> jsonb (without this Npgsql would pick native text[]).
        job.Property(x => x.Tags).HasColumnType("jsonb");
        job.HasIndex(x => x.SlackEventId);
        job.HasIndex(x => x.State);
        job.HasIndex(x => x.CreatedAt);
        // Per-channel status queries (recent + last-24h count), also run on a 30-min timer across
        // every channel: filter by SlackChannelId + UpdatedAt range, DESC sort (backward index scan).
        job.HasIndex(x => new { x.SlackChannelId, x.UpdatedAt });
        job.HasMany(x => x.History)
           .WithOne(h => h.Job!)
           .HasForeignKey(h => h.JobId)
           .OnDelete(DeleteBehavior.Cascade);
        // GoogleAccountId points at the shared connections account — plain value, no cross-schema FK.

        var hist = b.Entity<JobStateHistory>();
        hist.ToTable("job_state_history");
        hist.HasKey(x => x.Id);
        hist.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32);
        hist.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32);
        hist.HasIndex(x => x.JobId);

        var ch = b.Entity<SlackChannel>();
        ch.ToTable("slack_channels");
        ch.HasKey(x => x.Id);
        ch.HasIndex(x => new { x.WorkspaceId, x.SlackChannelId }).IsUnique();
        ch.HasIndex(x => x.SlackChannelId);

        var project = b.Entity<GoogleProject>();
        project.ToTable("google_projects");
        project.HasKey(x => x.Id);
        project.Property(x => x.Status).HasMaxLength(32);
        project.Property(x => x.EncryptedClientSecret).IsEncrypted(protector);
        // One row per Google Cloud project: the same OAuth client id must not be added twice
        // (else its single real per-project quota would be tracked as two counters → over-counting).
        project.HasIndex(x => x.ClientId).IsUnique();

        var binding = b.Entity<GoogleAccountBinding>();
        binding.ToTable("google_account_bindings");
        binding.HasKey(x => x.Id);
        binding.Property(x => x.Status).HasMaxLength(32);
        // One binding per shared account (an account is issued by exactly one project).
        binding.HasIndex(x => x.AccountId).IsUnique();
        binding.HasIndex(x => x.YouTubeChannelId); // rotation gathers all bindings sharing a channel
        binding.HasIndex(x => x.ProjectId);
        // Restrict: a project can't be deleted while bindings still reference it (issuing-client
        // binding is permanent). The admin API surfaces this as a 409 before EF would throw.
        binding.HasOne(x => x.Project)
               .WithMany()
               .HasForeignKey(x => x.ProjectId)
               .OnDelete(DeleteBehavior.Restrict);

        var cm = b.Entity<ChannelMapping>();
        cm.ToTable("channel_mappings");
        cm.HasKey(x => x.Id);
        cm.HasIndex(x => x.SlackChannelId).IsUnique();
        // SlackWorkspaceId + GoogleAccountId reference the shared connections schema — plain values.
    }
}

/// <summary>Design-time factory for <c>dotnet ef</c> (mirrors the module's runtime registration).</summary>
public sealed class YouTubeUploadsDbContextFactory : IDesignTimeDbContextFactory<YouTubeUploadsDbContext>
{
    public YouTubeUploadsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<YouTubeUploadsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=hookline;Username=hookline;Password=design-time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", YouTubeUploadsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new YouTubeUploadsDbContext(options, new PassthroughSecretProtector());
    }

    /// <summary>Design-time only no-op protector (migrations never touch real ciphertext).</summary>
    private sealed class PassthroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
        public bool TryUnprotect(string ciphertext, out string plaintext)
        {
            plaintext = ciphertext;
            return true;
        }
    }
}
