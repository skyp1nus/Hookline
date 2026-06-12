using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hookline.Modules.YouTubeComments.Endpoints;

/// <summary>
/// The module's app API under <c>/api/youtube-comments</c> (through the BFF): API keys, monitored
/// YouTube channels, channel→Slack mappings, the Slack workspace/channel reads, and the dashboard
/// stats + 24h comments timeline. Every route is gated to an authenticated caller (the BFF resolves
/// identity behind its admin token).
/// </summary>
public static class YouTubeCommentsApiEndpoints
{
    public static void MapYouTubeCommentsApiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/youtube-comments").AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            return user.IsAuthenticated ? await next(ctx) : Results.Unauthorized();
        });

        MapApiKeys(g);
        MapChannels(g);
        MapMappings(g);
        MapSlack(g);
        MapDashboard(g);
    }

    // ── YouTube API keys (add/validate/toggle/delete + per-key quota bars) ──
    private static void MapApiKeys(RouteGroupBuilder g)
    {
        g.MapGet("/keys", async (ApiKeyService keys, CancellationToken ct) =>
            Results.Ok(await keys.ListAsync(ct)));

        g.MapPost("/keys", async (CreateApiKeyRequest request, ApiKeyService keys, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await keys.CreateAsync(request, ct));
            }
            catch (ApiKeyValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        g.MapPatch("/keys/{id:guid}/toggle", async (Guid id, ApiKeyService keys, CancellationToken ct) =>
        {
            var dto = await keys.ToggleAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        g.MapDelete("/keys/{id:guid}", async (Guid id, ApiKeyService keys, CancellationToken ct) =>
            await keys.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── monitored YouTube channels (by URL / @handle / id) ──
    private static void MapChannels(RouteGroupBuilder g)
    {
        g.MapGet("/youtube/channels", async (ChannelService channels, CancellationToken ct) =>
            Results.Ok(await channels.ListAsync(ct)));

        g.MapPost("/youtube/channels", async (AddChannelRequest request, ChannelService channels, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await channels.AddAsync(request.Input, ct));
            }
            catch (ChannelResolutionException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        g.MapDelete("/youtube/channels/{id:guid}", async (Guid id, ChannelService channels, CancellationToken ct) =>
            await channels.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── channel → Slack mappings ──
    private static void MapMappings(RouteGroupBuilder g)
    {
        g.MapGet("/mappings", async (MappingService mappings, CancellationToken ct) =>
            Results.Ok(await mappings.ListAsync(ct)));

        g.MapGet("/mappings/options", async (MappingService mappings, CancellationToken ct) =>
            Results.Ok(await mappings.GetOptionsAsync(ct)));

        g.MapPost("/mappings", async (CreateMappingRequest request, MappingService mappings, CancellationToken ct) =>
        {
            try
            {
                var created = await mappings.CreateAsync(request, ct);
                return Results.Created($"/api/youtube-comments/mappings/{created.Id}", created);
            }
            catch (MappingConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        g.MapPatch("/mappings/{id:guid}", async (Guid id, UpdateMappingRequest request, MappingService mappings, CancellationToken ct) =>
        {
            var updated = await mappings.UpdateAsync(id, request, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        g.MapDelete("/mappings/{id:guid}", async (Guid id, MappingService mappings, CancellationToken ct) =>
            await mappings.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── Slack workspaces + cached channels (connect/disconnect via the shared Connections area) ──
    private static void MapSlack(RouteGroupBuilder g)
    {
        g.MapGet("/slack/workspaces", async (SlackChannelService slack, CancellationToken ct) =>
            Results.Ok(await slack.ListWorkspacesAsync(ct)));

        g.MapGet("/slack/workspaces/{id:guid}/channels", async (Guid id, SlackChannelService slack, CancellationToken ct) =>
            Results.Ok(await slack.ListChannelsAsync(id, ct)));

        g.MapPost("/slack/workspaces/{id:guid}/refresh-channels", async (Guid id, SlackChannelService slack, CancellationToken ct) =>
        {
            var channels = await slack.RefreshChannelsAsync(id, ct);
            return channels is null ? Results.NotFound() : Results.Ok(channels);
        });

        // Freshen every active workspace's channel cache on demand (the Add-mapping dialog calls this on open so
        // a shared-Connections Slack install fills this module's picker). The /mappings/options GET stays a pure read.
        g.MapPost("/slack/refresh-channels", async (SlackChannelService slack, CancellationToken ct) =>
            Results.Ok(await slack.RefreshAllChannelsAsync(ct)));

        g.MapDelete("/slack/workspaces/{id:guid}", async (Guid id, SlackChannelService slack, CancellationToken ct) =>
            await slack.DeleteWorkspaceAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── dashboard KPIs + 24h timeline ──
    private static void MapDashboard(RouteGroupBuilder g)
    {
        g.MapGet("/dashboard/stats", async (DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetStatsAsync(ct)));

        g.MapGet("/dashboard/comments-timeline", async (DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetCommentsTimelineAsync(ct)));
    }
}
