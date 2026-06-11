using Hookline.Modules.YouTubeUploads.Endpoints;
using Hookline.Modules.YouTubeUploads.Features;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.Modules.YouTubeUploads.Jobs;

using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Maintenance;
using Hookline.SharedKernel.Messaging;
using Hookline.SharedKernel.Modules;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hookline.Modules.YouTubeUploads;

/// <summary>
/// The "YouTube Uploads" tool. Templated Slack message →
/// Drive download → private YouTube upload, with live status, cancel/confirm, per-project quota
/// rotation and no-duplicate-upload guarantees. Slack workspaces + Google accounts come from the
/// shared Connections subsystem; secrets via the shared protector; jobs via the shared scheduler.
/// </summary>
public sealed class YouTubeUploadsModule : IModule
{
    public string Name => "youtube-uploads";

    public IEnumerable<ConnectionRequirement> RequiredConnections =>
    [
        new(ConnectionType.Slack, Required: true, Note: "Workspace bot token for the listening channels."),
        new(ConnectionType.Google, Required: true, Note: "YouTube upload + Drive readonly (per project)."),
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.Configure<YouTubeUploadsOptions>(config.GetSection(YouTubeUploadsOptions.Section));

        services.AddDbContext<YouTubeUploadsDbContext>(options => options
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", YouTubeUploadsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        // Typed Slack HTTP client (per-call bot token).
        services.AddHttpClient<SlackClient>();

        // Stateless / Redis / in-memory singletons.
        services.AddSingleton<IDedupService, DedupService>();
        services.AddSingleton<ICancellationFlags, CancellationFlags>();
        services.AddSingleton<IQuotaService, QuotaService>();
        services.AddSingleton<IApiUsageService, ApiUsageService>();
        services.AddSingleton<IProgressTracker, ProgressTracker>();
        services.AddSingleton<SlackSignatureVerifier>();
        services.AddSingleton<SlackTemplateParser>();
        services.AddSingleton<GoogleCredentialFactory>();
        services.AddSingleton<DriveDownloadService>();
        services.AddSingleton<YouTubeUploadService>();
        services.AddSingleton<ISlackStatusService, SlackStatusService>();

        // Scoped services (touch the DbContext / shared accessors).
        services.AddScoped<UploadSettingsService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<ChannelMappingService>();
        services.AddScoped<GoogleProjectsService>();
        services.AddScoped<GoogleAccountsService>();

        // Implements the shared IGoogleChannelCredentials contract — hands the YouTube Comments module a
        // moderation-capable (force-ssl) access token for a channel's owning account, without exposing
        // client secrets or coupling Comments to this module. (Owner of OAuth clients = owner of this impl.)
        services.AddScoped<IGoogleChannelCredentials, GoogleChannelCredentials>();
        services.AddScoped<SlackChannelService>();
        services.AddScoped<UploadsReadService>();
        services.AddScoped<SlackIngestService>();
        services.AddScoped<UploadJobHandler>();

        // React to shared Connections disconnects (deactivate bindings/mappings) — guide §5.
        services.AddScoped<IIntegrationEventHandler<GoogleAccountDisconnected>, GoogleAccountDisconnectedHandler>();
        services.AddScoped<IIntegrationEventHandler<SlackWorkspaceDisconnected>, SlackWorkspaceDisconnectedHandler>();

        // System "Danger Zone" fan-out (pause-all / reset) — host resolves it via the shared contract.
        services.AddScoped<IMaintenanceControl, UploadsMaintenanceControl>();

        // Startup: seed env project + self-heal interrupted jobs (after migrations).
        services.AddHostedService<YouTubeUploadsStartupService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapYouTubeUploadsProviderEndpoints(); // /slack/*, /google/* (backend-direct)
        endpoints.MapYouTubeUploadsApiEndpoints();      // /api/youtube-uploads/* (through the BFF)
    }

    public void RegisterJobs(IJobScheduler scheduler) =>
        // Re-render every channel's status in place so >24h recent items expire without new activity.
        scheduler.AddOrUpdateRecurring<ISlackStatusService>(
            "youtube-uploads.status-refresh",
            s => s.RefreshAllInPlaceAsync(CancellationToken.None),
            "*/30 * * * *");

    public DbContext Migrate(IServiceProvider services) =>
        services.GetRequiredService<YouTubeUploadsDbContext>();
}
