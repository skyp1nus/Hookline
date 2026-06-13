using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.Modules.YouTubeUploads.Jobs;
using Hookline.SharedKernel.Auth;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Endpoints;

public sealed record CreateMappingDto(Guid SlackWorkspaceId, string SlackChannelId, Guid GoogleAccountId);
public sealed record UpdateMappingDto(bool? Active);
public sealed record UpdateSettingsDto(string? DefaultVisibility, int? TransferChunkSizeMb, bool? MadeForKids, bool? ContainsSyntheticMedia, string? CategoryId, string? Language, bool? PublicStatsViewable);
public sealed record CreateGoogleProjectDto(string Label, string ClientId, string ClientSecret);
public sealed record UpdateGoogleProjectDto(string? Label, string? Status);

/// <summary>
/// The module's app API under <c>/api/youtube-uploads</c> (through the BFF). The ytu uploads pages consume
/// the design-shaped <c>jobs</c> / <c>upload-history</c> / <c>upload-mappings</c> reads + cancel/confirm;
/// the rest mirror the original admin surface for connections/settings/dashboard/usage. Every route is
/// gated to an authenticated caller (the BFF resolves identity behind its admin token).
/// </summary>
public static class YouTubeUploadsApiEndpoints
{
    public static void MapYouTubeUploadsApiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/youtube-uploads").AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            return user.IsAuthenticated ? await next(ctx) : Results.Unauthorized();
        });

        MapUploadsReads(g);
        MapJobActions(g);
        MapSettings(g);
        MapSlack(g);
        MapGoogle(g);
        MapMappings(g);
        MapDashboardAndUsage(g);
        MapJobGrid(g);
    }

    // ── design-shaped reads the ytu hooks call (api.get('/youtube-uploads/…')) ──
    private static void MapUploadsReads(RouteGroupBuilder g)
    {
        g.MapGet("/jobs", async (UploadsReadService reads, CancellationToken ct) =>
            Results.Ok(await reads.GetQueueAsync(ct)));

        g.MapGet("/upload-history", async (UploadsReadService reads, CancellationToken ct) =>
            Results.Ok(await reads.GetHistoryAsync(100, ct)));

        g.MapGet("/upload-mappings", async (UploadsReadService reads, CancellationToken ct) =>
            Results.Ok(await reads.GetMappingsAsync(ct)));

        // Real filtered CSV export of the history page (server applies the same account/status/search
        // filters). Returned as text/csv; the web client turns the body into a named file download (the BFF
        // proxy forwards content-type but not content-disposition, so the filename is set client-side).
        g.MapGet("/upload-history/export.csv", async (
            UploadsReadService reads, string? account, string? status, string? q, CancellationToken ct) =>
            Results.Text(await reads.GetHistoryCsvAsync(account, status, q, ct), "text/csv"));
    }

    private static void MapJobActions(RouteGroupBuilder g)
    {
        g.MapPost("/jobs/{id:guid}/cancel", async (
            Guid id, IJobService jobs, ICancellationFlags cancelFlags, ISlackStatusService status, CancellationToken ct) =>
        {
            var (ok, error) = await YouTubeUploadsActions.CancelAsync(jobs, cancelFlags, status, id, _ => Task.CompletedTask, ct);
            if (ok) return Results.Ok(new { cancelled = true });
            return error == "not_found" ? Results.NotFound() : Results.Conflict(new { error });
        });

        g.MapPost("/jobs/{id:guid}/confirm", async (
            Guid id, IJobService jobs, SlackIngestService ingest, ISlackStatusService status, CancellationToken ct) =>
        {
            var (ok, error) = await YouTubeUploadsActions.ConfirmAsync(jobs, ingest, status, id, _ => Task.CompletedTask, ct);
            if (ok) return Results.Ok(new { confirmed = true });
            return error == "not_found" ? Results.NotFound() : Results.Conflict(new { error });
        });
    }

    // ── status + upload settings ──
    private static void MapSettings(RouteGroupBuilder g)
    {
        g.MapGet("/status", async (
            SlackChannelService workspaces, GoogleAccountsService google, GoogleProjectsService projects,
            IQuotaService quota, CancellationToken ct) =>
        {
            var wsCount = await workspaces.CountActiveWorkspacesAsync(ct);
            var conn = await google.GetConnectionAsync(ct);
            var accountCount = await google.CountAccountsAsync(ct);
            int usedUnits = 0, capUnits = 0, usedUploads = 0, uploadLimit = 0, remainingUploads = 0, totalUploads = 0;
            foreach (var c in await projects.ListAsync(ct))
            {
                if (c.Status != GoogleProjectsService.StatusActive) continue;
                var qs = await quota.GetStatusAsync(c.Id);
                usedUnits += qs.UsedUnits; capUnits += qs.CapUnits;
                usedUploads += qs.UsedUploads; uploadLimit += qs.UploadLimit;
                remainingUploads += qs.RemainingUploads; totalUploads += qs.TotalUploads;
            }
            return Results.Ok(new
            {
                slackConfigured = wsCount > 0,
                workspaceCount = wsCount,
                google = new { conn.Connected, conn.Scopes, conn.ConnectedAt, accountCount },
                quota = new { UsedUploads = usedUploads, UploadLimit = uploadLimit, RemainingUploads = remainingUploads, TotalUploads = totalUploads, UsedUnits = usedUnits, CapUnits = capUnits },
            });
        });

        g.MapGet("/settings", async (UploadSettingsService settings, CancellationToken ct) =>
        {
            var s = await settings.GetUploadSettingsAsync(ct);
            return Results.Ok(new { defaultVisibility = s.Visibility, transferChunkSizeMb = s.ChunkSizeMb, madeForKids = s.MadeForKids, containsSyntheticMedia = s.ContainsSyntheticMedia, categoryId = s.CategoryId, language = s.Language, publicStatsViewable = s.PublicStatsViewable });
        });

        g.MapPatch("/settings", async (UpdateSettingsDto dto, UploadSettingsService settings, CancellationToken ct) =>
        {
            var cur = await settings.GetUploadSettingsAsync(ct);
            var visibility = dto.DefaultVisibility is null ? cur.Visibility : YouTubeUploadService.NormalizeVisibility(dto.DefaultVisibility);
            var chunk = dto.TransferChunkSizeMb is null ? cur.ChunkSizeMb : Math.Clamp(dto.TransferChunkSizeMb.Value, 1, 1024);
            var madeForKids = dto.MadeForKids ?? cur.MadeForKids;
            var synthetic = dto.ContainsSyntheticMedia ?? cur.ContainsSyntheticMedia;
            var category = dto.CategoryId is null ? cur.CategoryId : YouTubeUploadService.NormalizeCategoryId(dto.CategoryId);
            var language = dto.Language is null ? cur.Language : YouTubeUploadService.NormalizeLanguage(dto.Language);
            var publicStats = dto.PublicStatsViewable ?? cur.PublicStatsViewable;
            await settings.UpdateUploadSettingsAsync(visibility, chunk, madeForKids, synthetic, category, language, publicStats, ct);
            return Results.Ok(new { defaultVisibility = visibility, transferChunkSizeMb = chunk, madeForKids, containsSyntheticMedia = synthetic, categoryId = category, language, publicStatsViewable = publicStats });
        });
    }

    // ── Slack workspaces + channels ──
    private static void MapSlack(RouteGroupBuilder g)
    {
        g.MapGet("/slack/workspaces", async (SlackChannelService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListWorkspacesAsync(ct)));

        g.MapGet("/slack/workspaces/{id:guid}/channels", async (Guid id, SlackChannelService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListChannelsAsync(id, ct)));

        g.MapPost("/slack/workspaces/{id:guid}/refresh-channels", async (Guid id, SlackChannelService ws, CancellationToken ct) =>
        {
            var channels = await ws.RefreshChannelsAsync(id, ct);
            return channels is null ? Results.NotFound() : Results.Ok(channels);
        });

        g.MapDelete("/slack/workspaces/{id:guid}", async (Guid id, SlackChannelService ws, CancellationToken ct) =>
            await ws.DeleteWorkspaceAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        g.MapGet("/slack/channels", async (SlackChannelService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListAllMemberChannelsAsync(ct)));

        // Freshen every active workspace's channel cache on demand (the Add-route picker calls this on open so
        // newly created/joined channels show without reconnecting Slack). The GET above stays a pure read.
        g.MapPost("/slack/refresh-channels", async (SlackChannelService ws, CancellationToken ct) =>
            Results.Ok(await ws.RefreshAllActiveWorkspacesAsync(ct)));
    }

    // ── Google projects + accounts ──
    private static void MapGoogle(RouteGroupBuilder g)
    {
        g.MapGet("/google/projects", async (GoogleProjectsService projects, IQuotaService quota, CancellationToken ct) =>
        {
            var list = await projects.ListAsync(ct);
            var result = new List<object>(list.Count);
            foreach (var c in list)
            {
                var q = await quota.GetStatusAsync(c.Id);
                var accountCount = await projects.CountAccountsAsync(c.Id, ct);
                result.Add(new { c.Id, c.Label, c.ClientId, c.Status, c.CreatedAt, c.UpdatedAt, accountCount, quota = new { q.UsedUploads, q.UploadLimit, q.RemainingUploads, q.TotalUploads, q.UsedUnits, q.CapUnits } });
            }
            return Results.Ok(result);
        });

        g.MapPost("/google/projects", async (CreateGoogleProjectDto dto, GoogleProjectsService projects, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ClientId) || string.IsNullOrWhiteSpace(dto.ClientSecret))
                return Results.BadRequest(new { error = "client_id_and_secret_required" });
            if (await projects.ClientIdExistsAsync(dto.ClientId, ct))
                return Results.Conflict(new { error = "client_id_exists" });
            try { return Results.Ok(await projects.CreateAsync(dto.Label, dto.ClientId, dto.ClientSecret, ct)); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "client_id_exists" }); }
        });

        g.MapPatch("/google/projects/{id:guid}", async (Guid id, UpdateGoogleProjectDto dto, GoogleProjectsService projects, CancellationToken ct) =>
            await projects.UpdateAsync(id, dto.Label, dto.Status, ct) ? Results.NoContent() : Results.NotFound());

        g.MapDelete("/google/projects/{id:guid}", async (Guid id, GoogleProjectsService projects, CancellationToken ct) =>
        {
            var (ok, error) = await projects.DeleteAsync(id, ct);
            if (ok) return Results.NoContent();
            return error == "not_found" ? Results.NotFound() : Results.Conflict(new { error });
        });

        g.MapGet("/google/accounts", async (GoogleAccountsService google, IQuotaService quota, CancellationToken ct) =>
        {
            var accounts = await google.ListAccountsAsync(ct);
            var result = new List<object>(accounts.Count);
            foreach (var a in accounts)
            {
                var q = await quota.GetStatusAsync(a.ProjectId);
                result.Add(new { a.Id, a.Label, a.YouTubeChannelId, a.YouTubeChannelTitle, a.AvatarUrl, a.AccountEmail, a.Status, a.CreatedAt, a.ProjectId, a.ProjectLabel, quota = new { q.UsedUploads, q.UploadLimit, q.RemainingUploads, q.TotalUploads, q.UsedUnits, q.CapUnits } });
            }
            return Results.Ok(result);
        });

        // Disconnect always proceeds: DeleteAccountAsync publishes GoogleAccountDisconnected, whose handler
        // deactivates the account↔project binding and drops every channel mapping that targets it. No
        // pre-check — the cascade severs the mappings, so a still-mapped account isn't a refusal.
        g.MapDelete("/google/accounts/{id:guid}", async (Guid id, GoogleAccountsService google, CancellationToken ct) =>
            await google.DeleteAccountAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── channel→account mappings (admin shape) ──
    private static void MapMappings(RouteGroupBuilder g)
    {
        g.MapGet("/mappings", async (ChannelMappingService mappings, CancellationToken ct) =>
            Results.Ok(await mappings.ListAsync(ct)));

        g.MapPost("/mappings", async (
            CreateMappingDto dto, ChannelMappingService mappings, ISlackStatusService status,
            SlackClient slack, SlackChannelService workspaces, CancellationToken ct) =>
        {
            var (ok, error) = await mappings.CreateAsync(dto.SlackWorkspaceId, dto.SlackChannelId, dto.GoogleAccountId, ct);
            if (!ok) return Results.Conflict(new { error });
            await status.RefreshQueueAsync(ct);

            var botToken = await workspaces.GetBotTokenForChannelAsync(dto.SlackChannelId, ct);
            if (botToken is not null)
            {
                var ts = await slack.PostMessageAsync(botToken, dto.SlackChannelId, SlackBlocks.UploadTemplateText(), ct: ct);
                if (ts is not null) await slack.PinMessageAsync(botToken, dto.SlackChannelId, ts, ct);
            }
            return Results.Ok(new { created = true });
        });

        // Toggle a route active/paused (P0). Event-driven, so this only flips the flag the ingest path
        // reads — no scheduler call. Re-render the per-channel Slack status so a paused route drops out.
        g.MapPatch("/mappings/{id:guid}", async (
            Guid id, UpdateMappingDto dto, ChannelMappingService mappings, ISlackStatusService status, CancellationToken ct) =>
        {
            if (!await mappings.UpdateAsync(id, dto.Active, ct)) return Results.NotFound();
            await status.RefreshQueueAsync(ct);
            return Results.Ok(new { id, active = dto.Active });
        });

        g.MapDelete("/mappings/{id:guid}", async (Guid id, ChannelMappingService mappings, CancellationToken ct) =>
            await mappings.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }

    // ── dashboard KPIs + daily API usage ──
    private static void MapDashboardAndUsage(RouteGroupBuilder g)
    {
        g.MapGet("/dashboard", async (
            SlackChannelService ws, GoogleAccountsService google, GoogleProjectsService projects,
            IJobService jobs, IQuotaService quota, IOptions<YouTubeUploadsOptions> opt, CancellationToken ct) =>
        {
            var workspaceCount = await ws.CountActiveWorkspacesAsync(ct);
            var accountCount = await google.CountAccountsAsync(ct);
            var uploadCapPer = opt.Value.YouTubeDailyUploadLimit;
            var unitCapPer = opt.Value.YouTubeDailyQuotaUnits;
            var projectList = await projects.ListAsync(ct);
            int usedUploadsSum = 0, usedUnitsSum = 0;
            foreach (var c in projectList)
            {
                var qs = await quota.GetStatusAsync(c.Id);
                usedUploadsSum += qs.UsedUploads;
                usedUnitsSum += qs.UsedUnits;
            }
            var (uploadsToday, uploadsLast24h, errorsLast24h) = await jobs.GetDashboardCountsAsync(ct);
            return Results.Ok(new
            {
                workspaceCount, accountCount, clientCount = projectList.Count,
                uploadsToday, uploadsLast24h, errorsLast24h,
                quotaUploadsUsed = usedUploadsSum, quotaUploadCap = uploadCapPer * projectList.Count,
                quotaUsedUnits = usedUnitsSum, quotaCapUnits = unitCapPer * projectList.Count,
            });
        });

        g.MapGet("/usage", async (
            GoogleProjectsService projects, IQuotaService quota, IApiUsageService usage,
            IOptions<YouTubeUploadsOptions> opt, CancellationToken ct) =>
        {
            var projectList = await projects.ListAsync(ct);
            var quotas = new List<ApiUsageReport.ClientQuota>(projectList.Count);
            foreach (var c in projectList)
                quotas.Add(new ApiUsageReport.ClientQuota(c.Id, c.Label, await quota.GetStatusAsync(c.Id)));
            var entries = await usage.GetTodayAsync();
            var report = ApiUsageReport.Build(PacificTime.TodayKey(), quotas, entries, opt.Value.DriveDailyQueryLimit);
            return Results.Ok(report);
        });
    }

    // ── filtered + paginated job history (admin grid; the live queue uses /jobs) ──
    private static void MapJobGrid(RouteGroupBuilder g)
    {
        g.MapGet("/jobs/grid", async (
            IJobService jobs, string? status, string? channel, string? tag, Guid? account,
            DateTimeOffset? from, DateTimeOffset? to, string? search, int? page, int? pageSize, CancellationToken ct) =>
        {
            JobState? state = Enum.TryParse<JobState>(status, ignoreCase: true, out var s) ? s : null;
            var pageNum = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var filter = new JobHistoryFilter(state, channel, tag, account, from, to, search);
            var (items, total) = await jobs.GetHistoryPagedAsync(filter, pageNum, size, ct);
            return Results.Ok(new
            {
                total,
                items = items.Select(x => new
                {
                    x.Job.Id,
                    fileName = x.Job.OriginalFileName ?? x.Job.Title,
                    state = x.Job.State.ToString(),
                    x.Job.YouTubeUrl,
                    error = x.Job.ErrorMessage,
                    tags = x.Job.Tags,
                    x.Job.SlackChannelId,
                    channelName = x.ChannelName,
                    x.Job.GoogleAccountId,
                    googleAccountLabel = x.GoogleAccountLabel,
                    x.Job.CreatedAt,
                    x.Job.UpdatedAt,
                }),
            });
        });

        g.MapGet("/jobs/filters", async (IJobService jobs, CancellationToken ct) =>
        {
            var opts = await jobs.GetJobFilterOptionsAsync(ct);
            return Results.Ok(new
            {
                channels = opts.Channels.Select(c => new { c.Id, c.Name }),
                tags = opts.Tags,
                accounts = opts.Accounts.Select(a => new { a.Id, a.Label }),
            });
        });
    }
}
