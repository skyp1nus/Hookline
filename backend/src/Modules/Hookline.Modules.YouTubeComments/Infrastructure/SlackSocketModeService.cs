using System.Text.Json;

using Hookline.Modules.YouTubeComments.Endpoints;
using Hookline.SharedKernel.Slack;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// DEV-ONLY inbound Slack transport for the YouTube Comments module. Extends the shared Socket Mode
/// base to route <c>interactive</c> envelopes (the "Reject on YouTube" button) to the same reusable
/// handler the HTTP webhook uses. Comments has no Slack events surface, so only interactivity is routed.
/// </summary>
public sealed class SlackSocketModeService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<YouTubeCommentsOptions> options,
    ILogger<SlackSocketModeService> logger)
    : SlackSocketModeServiceBase(scopeFactory, httpClientFactory, logger)
{
    protected override string ModuleName => "YouTube Comments";
    protected override bool IsEnabled => options.Value.Slack.SocketMode.Enabled;
    protected override string? AppToken => options.Value.Slack.AppToken;

    protected override async Task DispatchEnvelopeAsync(IServiceProvider sp, string? type, JsonElement payload, CancellationToken ct)
    {
        if (type != "interactive")
            return;

        await YouTubeCommentsProviderEndpoints.DispatchBlockActionsAsync(
            payload,
            sp.GetRequiredService<CommentModerationService>(),
            sp.GetRequiredService<ISlackClient>(),
            ct);
    }
}
