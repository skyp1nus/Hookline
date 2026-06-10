using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hookline.Host.Endpoints;

/// <summary>
/// Host-level System endpoints shared by every module. <c>GET /api/system/logs</c> pages over the
/// shared audit trail (optionally filtered to one module) — this is the read side the
/// System→Logs page renders, reused by YouTube Uploads, YouTube Comments and any future module.
/// </summary>
public static class SystemEndpoints
{
    public static void MapHooklineSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system");

        group.MapGet("/logs", async (
            ICurrentUser current,
            IAuditLogReader reader,
            string? module,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var result = await reader.ListAsync(module, page ?? 1, pageSize ?? 50, ct);
            return Results.Ok(result);
        });
    }
}
