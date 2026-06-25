using System.Text.Json;

using Hookline.Modules.YouTubeUploads.Endpoints;
using Hookline.Modules.YouTubeUploads.Jobs;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Slack;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// DEV-ONLY inbound Slack transport for the YouTube Uploads module. Extends the shared Socket Mode
/// base to route <c>events_api</c> and <c>interactive</c> envelopes to the same reusable handlers
/// the HTTP webhook uses.
/// </summary>
public sealed class SlackSocketModeService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<YouTubeUploadsOptions> options,
    ILogger<SlackSocketModeService> logger)
    : SlackSocketModeServiceBase(scopeFactory, httpClientFactory, logger)
{
    protected override string ModuleName => "YouTube Uploads";
    protected override bool IsEnabled => options.Value.Slack.SocketMode.Enabled;
    protected override string? AppToken => options.Value.Slack.AppToken;

    protected override async Task DispatchEnvelopeAsync(IServiceProvider sp, string? type, JsonElement payload, CancellationToken ct)
    {
        switch (type)
        {
            case "events_api":
                var payloadType = payload.TryGetProperty("type", out var pt) ? pt.GetString() : null;
                if (payloadType == "event_callback")
                {
                    await YouTubeUploadsProviderEndpoints.ProcessEventCallbackAsync(
                        payload,
                        sp.GetRequiredService<IDedupService>(),
                        sp.GetRequiredService<IJobScheduler>());
                }
                break;

            case "interactive":
                await YouTubeUploadsProviderEndpoints.DispatchBlockActionsAsync(
                    payload,
                    sp.GetRequiredService<IJobService>(),
                    sp.GetRequiredService<ICancellationFlags>(),
                    sp.GetRequiredService<ISlackStatusService>(),
                    sp.GetRequiredService<SlackClient>(),
                    sp.GetRequiredService<SlackIngestService>(),
                    ct);
                break;
        }
    }
}
