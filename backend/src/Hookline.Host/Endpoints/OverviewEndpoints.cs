using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hookline.Host.Endpoints;

/// <summary>
/// Host-level Overview endpoint. The landing page needs a CROSS-MODULE view, which no single module can
/// produce without breaking isolation — so the host (which references both modules) composes it: it resolves
/// each module's own read-only overview service (<see cref="CommentsOverviewService"/> from the Comments
/// module, <see cref="UploadsOverviewService"/> from the Uploads module — neither references the other) and
/// returns a single <c>{ comments, uploads }</c> JSON object. Identity middleware already covers <c>/api/*</c>,
/// mirroring how <c>/api/system</c> is mapped.
/// </summary>
public static class OverviewEndpoints
{
    public static IEndpointRouteBuilder MapHooklineOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/overview", async (
            ICurrentUser current,
            CommentsOverviewService comments,
            UploadsOverviewService uploads,
            CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var commentsPanel = await comments.GetAsync(ct);
            var uploadsPanel = await uploads.GetAsync(ct);
            return Results.Ok(new { comments = commentsPanel, uploads = uploadsPanel });
        });

        return app;
    }
}
