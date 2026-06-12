using Hookline.Modules.YouTubeComments.Endpoints;
using Hookline.Modules.YouTubeComments.Features;
using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.Modules.YouTubeComments.Jobs;

using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Maintenance;
using Hookline.SharedKernel.Messaging;
using Hookline.SharedKernel.Modules;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments;

/// <summary>
/// The "YouTube Comments" tool. Per-mapping polling → commentThreads.list → exactly-once via
/// processed_comments → Block Kit → Slack, with a deep reply sweep and durable delivery retry. Monitoring
/// is OAuth-only: it resolves a force-ssl access credential for the channel's owning Google account via
/// the shared <see cref="IGoogleChannelCredentials"/> contract — the SAME credential that powers the
/// "Reject on YouTube" button, so monitoring and moderation are gated alike. Slack workspaces + Google
/// accounts come from the shared Connections subsystem; jobs run via the shared scheduler under the
/// system principal writing the shared audit trail.
/// </summary>
public sealed class YouTubeCommentsModule : IModule
{
    public string Name => "youtube-comments";

    public IEnumerable<ConnectionRequirement> RequiredConnections =>
    [
        new(ConnectionType.Slack, Required: true, Note: "Workspace bot token for the mapped channels."),
        new(ConnectionType.Google, Required: true, Note: "A force-ssl account owns each monitored channel — enables both comment monitoring and 'Reject on YouTube'."),
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        // Bind + validate on startup so a misconfigured quota ceiling fails fast instead of silently
        // breaking the quota math (a non-positive limit makes capacity/percent meaningless). The
        // validator (YouTubeCommentsOptionsValidator) is a dedicated, unit-testable type.
        services.AddOptions<YouTubeCommentsOptions>()
            .Bind(config.GetSection(YouTubeCommentsOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<YouTubeCommentsOptions>, YouTubeCommentsOptionsValidator>();

        services.AddDbContext<YouTubeCommentsDbContext>(options => options
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", YouTubeCommentsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention());

        // Typed Slack HTTP client (per-call bot token).
        services.AddHttpClient<ISlackClient, SlackClient>();

        // Stateless YouTube read client (builds a YouTubeService per call from the OAuth access token).
        services.AddSingleton<IYouTubeClient, YouTubeClient>();

        // Slack interactivity (the "Reject on YouTube" button): signature verifier + the OAuth-authorized
        // moderation write client. IGoogleChannelCredentials (the access-token provider) is implemented by
        // the Uploads module; both monitoring (the polling jobs) and CommentModerationService consume it
        // optionally via IEnumerable<> and degrade to an honest gated state if absent.
        services.AddSingleton<SlackSignatureVerifier>();
        services.AddSingleton<IYouTubeModerationClient, YouTubeModerationClient>();
        services.AddScoped<CommentModerationService>();

        // Scoped services (touch the DbContext / shared accessors).
        services.AddScoped<ICommentsAudit, CommentsAudit>();
        services.AddScoped<IPollingScheduler, HangfirePollingScheduler>();
        services.AddScoped<ChannelService>();
        services.AddScoped<SlackChannelService>();
        services.AddScoped<MappingService>();
        services.AddScoped<DashboardService>();
        // Read-only cross-table aggregate for the host's /api/overview Comments panel.
        services.AddScoped<CommentsOverviewService>();

        // Recurring + enqueued job handlers (Hangfire activates from a per-job DI scope).
        services.AddScoped<PollCommentsJob>();
        services.AddScoped<DeepReplySweepJob>();
        services.AddScoped<DeliveryRetryJob>();
        services.AddScoped<CleanupJob>();

        // React to a shared Slack-workspace disconnect (deactivate its mappings + tear down jobs) — guide §5.
        services.AddScoped<IIntegrationEventHandler<SlackWorkspaceDisconnected>, SlackWorkspaceDisconnectedHandler>();

        // System "Danger Zone" fan-out (pause-all / reset) — host resolves it via the shared contract.
        services.AddScoped<IMaintenanceControl, CommentsMaintenanceControl>();

        // Startup: reconcile per-mapping recurring jobs + register the static delivery/retention jobs.
        services.AddHostedService<YouTubeCommentsStartupService>();

        // DEV-ONLY inbound Slack transport (Socket Mode). No-op unless YouTubeComments:Slack:SocketMode:Enabled
        // is true; REFUSED at boot in Production by GuardSecurityConfig. The signature-verified HTTP webhook
        // (/slack/youtube-comments/interactivity) remains the canonical production path; this module-local service
        // just lets a developer test the "Reject on YouTube" button without a public tunnel. No cross-module dispatcher.
        services.AddHostedService<SlackSocketModeService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapYouTubeCommentsProviderEndpoints(); // /slack/youtube-comments/oauth/* (backend-direct)
        endpoints.MapYouTubeCommentsApiEndpoints();      // /api/youtube-comments/* (through the BFF)
    }

    // Per-mapping recurring polls + the static delivery/retention jobs are registered by
    // YouTubeCommentsStartupService (their crons are config-driven and the per-mapping set is dynamic),
    // so there is nothing to register at this fixed-list hook.
    public void RegisterJobs(IJobScheduler scheduler) { }

    public DbContext Migrate(IServiceProvider services) =>
        services.GetRequiredService<YouTubeCommentsDbContext>();
}
