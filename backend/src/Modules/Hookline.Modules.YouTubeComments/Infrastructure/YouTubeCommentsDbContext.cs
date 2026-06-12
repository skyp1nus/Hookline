using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// YouTube Comments module schema (<c>youtube_comments</c>). Owns the monitored YouTube channels,
/// the Slack-channel mapping-picker cache, the channel→Slack mappings, the exactly-once dedup ledger
/// (<c>processed_comments</c>) and the durable retry queue (<c>pending_deliveries</c>). Slack workspaces
/// live in the shared <c>connections</c> schema, and monitoring resolves a force-ssl OAuth credential
/// via the shared <c>IGoogleChannelCredentials</c> contract — both referenced only by plain id values
/// (no cross-schema FK). The module has no encrypted columns of its own (every secret is in the shared store).
/// </summary>
public sealed class YouTubeCommentsDbContext(DbContextOptions<YouTubeCommentsDbContext> options)
    : HooklineDbContext(options)
{
    public const string SchemaName = "youtube_comments";

    public DbSet<YouTubeChannel> YouTubeChannels => Set<YouTubeChannel>();
    public DbSet<SlackChannel> SlackChannels => Set<SlackChannel>();
    public DbSet<ChannelMapping> ChannelMappings => Set<ChannelMapping>();
    public DbSet<ProcessedComment> ProcessedComments => Set<ProcessedComment>();
    public DbSet<PendingDelivery> PendingDeliveries => Set<PendingDelivery>();
    public DbSet<CommentModeration> CommentModerations => Set<CommentModeration>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(SchemaName);

        var yt = b.Entity<YouTubeChannel>();
        yt.ToTable("youtube_channels");
        yt.HasKey(x => x.Id);
        // Pin to a clean column name (the snake_case convention would emit `you_tube_channel_id`).
        yt.Property(x => x.YouTubeChannelId).HasColumnName("youtube_channel_id").IsRequired().HasMaxLength(40);
        yt.HasIndex(x => x.YouTubeChannelId).IsUnique();
        yt.Property(x => x.Title).IsRequired().HasMaxLength(200);
        yt.Property(x => x.ThumbnailUrl).HasMaxLength(500);
        yt.Property(x => x.Handle).HasMaxLength(120);

        var ch = b.Entity<SlackChannel>();
        ch.ToTable("slack_channels");
        ch.HasKey(x => x.Id);
        // WorkspaceId references the shared connections workspace — plain value, no FK.
        ch.HasIndex(x => new { x.WorkspaceId, x.SlackChannelId }).IsUnique();
        ch.Property(x => x.SlackChannelId).IsRequired().HasMaxLength(40);
        ch.Property(x => x.Name).IsRequired().HasMaxLength(120);

        var cm = b.Entity<ChannelMapping>();
        cm.ToTable("channel_mappings");
        cm.HasKey(x => x.Id);
        cm.Property(x => x.YouTubeChannelId).HasColumnName("youtube_channel_id");
        cm.HasIndex(x => new { x.YouTubeChannelId, x.SlackChannelId }).IsUnique();
        cm.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
        cm.Property(x => x.ReplySweepFrequency).HasConversion<string>().HasMaxLength(20);
        cm.Property(x => x.LastError).HasMaxLength(1000);
        cm.HasOne(x => x.YouTubeChannel)
            .WithMany(x => x.Mappings)
            .HasForeignKey(x => x.YouTubeChannelId)
            .OnDelete(DeleteBehavior.Cascade);
        cm.HasOne(x => x.SlackChannel)
            .WithMany(x => x.Mappings)
            .HasForeignKey(x => x.SlackChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        var pc = b.Entity<ProcessedComment>();
        pc.ToTable("processed_comments");
        pc.HasKey(x => new { x.MappingId, x.CommentId });
        pc.Property(x => x.CommentId).HasMaxLength(100);
        pc.Property(x => x.VideoId).IsRequired().HasMaxLength(40);
        pc.Property(x => x.SlackMessageTs).HasMaxLength(40);
        pc.Property(x => x.ParentCommentId).HasMaxLength(100);
        pc.HasIndex(x => x.ProcessedAt);
        pc.HasOne(x => x.Mapping)
            .WithMany(x => x.ProcessedComments)
            .HasForeignKey(x => x.MappingId)
            .OnDelete(DeleteBehavior.Cascade);

        var pd = b.Entity<PendingDelivery>();
        pd.ToTable("pending_deliveries");
        pd.HasKey(x => x.Id);
        pd.Property(x => x.CommentId).IsRequired().HasMaxLength(100);
        pd.Property(x => x.ParentCommentId).HasMaxLength(100);
        pd.Property(x => x.VideoId).IsRequired().HasMaxLength(40);
        pd.Property(x => x.PayloadJson).IsRequired();
        pd.Property(x => x.LastError).HasMaxLength(1000);
        pd.HasIndex(x => x.NextAttemptAt);
        pd.HasOne(x => x.Mapping)
            .WithMany()
            .HasForeignKey(x => x.MappingId)
            .OnDelete(DeleteBehavior.Cascade);

        var mod = b.Entity<CommentModeration>();
        mod.ToTable("comment_moderations");
        mod.HasKey(x => x.Id);
        mod.Property(x => x.CommentId).IsRequired().HasMaxLength(100);
        mod.Property(x => x.Action).IsRequired().HasMaxLength(20);
        mod.Property(x => x.Status).IsRequired().HasMaxLength(20);
        mod.Property(x => x.SlackUserId).HasMaxLength(40);
        mod.Property(x => x.SlackUserName).HasMaxLength(120);
        // Idempotency guard: one moderation row per (mapping, comment). A racing double-click hits this
        // unique index on insert (caught + treated as already-done).
        mod.HasIndex(x => new { x.MappingId, x.CommentId }).IsUnique();
        mod.HasOne<ChannelMapping>()
            .WithMany()
            .HasForeignKey(x => x.MappingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Design-time factory for <c>dotnet ef</c> (mirrors the module's runtime registration).</summary>
public sealed class YouTubeCommentsDbContextFactory : IDesignTimeDbContextFactory<YouTubeCommentsDbContext>
{
    public YouTubeCommentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<YouTubeCommentsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=hookline;Username=hookline;Password=design-time",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", YouTubeCommentsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new YouTubeCommentsDbContext(options);
    }
}
