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
/// processed_comments → Block Kit → Slack, with a deep reply sweep, durable delivery retry, and
/// multiple API keys rotated by remaining Pacific-day quota. Slack workspaces + YouTube API keys come
/// from the shared Connections subsystem; jobs run via the shared scheduler under the system principal
/// writing the shared audit trail. Comment polling uses API KEYS (not OAuth) — correct for monitoring.
/// </summary>
public sealed class YouTubeCommentsModule : IModule
{
    public string Name => "youtube-comments";

    public IEnumerable<ConnectionRequirement> RequiredConnections =>
    [
        new(ConnectionType.Slack, Required: true, Note: "Workspace bot token for the mapped channels."),
        new(ConnectionType.YouTubeApiKey, Required: true, Note: "Quota-rotated keys for comment polling."),
        new(ConnectionType.Google, Required: false, Note: "Optional: a force-ssl account enables 'Reject on YouTube' from Slack."),
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

        // Stateless YouTube client (builds a YouTubeService per call from the leased API key).
        services.AddSingleton<IYouTubeClient, YouTubeClient>();

        // Slack interactivity (the "Reject on YouTube" button): signature verifier + the OAuth-authorized
        // moderation write client. IGoogleChannelCredentials (the access-token provider) is implemented by
        // the Uploads module; CommentModerationService consumes it optionally and degrades honestly if absent.
        services.AddSingleton<SlackSignatureVerifier>();
        services.AddSingleton<IYouTubeModerationClient, YouTubeModerationClient>();
        services.AddScoped<CommentModerationService>();

        // Scoped services (touch the DbContext / shared accessors).
        services.AddScoped<ICommentsAudit, CommentsAudit>();
        services.AddScoped<IYouTubeApiKeyProvider, YouTubeApiKeyProvider>();
        services.AddScoped<IPollingScheduler, HangfirePollingScheduler>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ChannelService>();
        services.AddScoped<SlackChannelService>();
        services.AddScoped<MappingService>();
        services.AddScoped<DashboardService>();

        // Recurring + enqueued job handlers (Hangfire activates from a per-job DI scope).
        services.AddScoped<PollCommentsJob>();
        services.AddScoped<DeepReplySweepJob>();
        services.AddScoped<DeliveryRetryJob>();
        services.AddScoped<CleanupJob>();

        // React to shared Connections disconnects (deactivate mappings / prune quota) — guide §5.
        services.AddScoped<IIntegrationEventHandler<SlackWorkspaceDisconnected>, SlackWorkspaceDisconnectedHandler>();
        services.AddScoped<IIntegrationEventHandler<YouTubeApiKeyDisconnected>, YouTubeApiKeyDisconnectedHandler>();

        // System "Danger Zone" fan-out (pause-all / reset) — host resolves it via the shared contract.
        services.AddScoped<IMaintenanceControl, CommentsMaintenanceControl>();

        // Startup: reconcile per-mapping recurring jobs + register the static delivery/retention jobs.
        services.AddHostedService<YouTubeCommentsStartupService>();
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
