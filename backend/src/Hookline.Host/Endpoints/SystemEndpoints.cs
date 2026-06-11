using System.Text.RegularExpressions;

using Hookline.Infrastructure.Settings;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Auth;
using Hookline.SharedKernel.Common;
using Hookline.SharedKernel.Maintenance;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hookline.Host.Endpoints;

/// <summary>Body of <c>PATCH /api/system/alerts</c> — any non-null field is applied.</summary>
public sealed record UpdateAlertsRequest(bool? UploadFailures, bool? QuotaWarnings, bool? OauthExpiry, bool? WeeklyDigest);

/// <summary>Body of <c>POST /api/system/reset</c> — must carry the literal type-to-confirm phrase.</summary>
public sealed record ResetRequest(string? Confirm);

/// <summary>
/// Host-level System endpoints shared by every module: the audit-log read + CSV export, the persisted
/// Alerts preferences, and the cross-module "Danger Zone" actions (pause-all / reset). The destructive
/// actions fan out over every registered <see cref="IMaintenanceControl"/> — the host never names a module
/// type, so the module-boundary arch tests keep holding. Every state change writes the shared audit trail.
/// </summary>
public static class SystemEndpoints
{
    private const string ResetPhrase = "RESET";

    public static void MapHooklineSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system");

        group.MapGet("/logs", async (
            ICurrentUser current, IAuditLogReader reader, string? module, int? page, int? pageSize, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var result = await reader.ListAsync(module, page ?? 1, pageSize ?? 50, ct);
            return Results.Ok(result);
        });

        // Real filtered CSV export of the Logs page. Applies the module filter server-side and the
        // level/search refinements the page uses, over a wider window (up to MaxExportRows). text/csv body;
        // the web client names the download (the BFF proxy forwards content-type but not content-disposition).
        group.MapGet("/logs/export.csv", async (
            ICurrentUser current, IAuditLogReader reader, string? module, string? level, string? q, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            const int pageSize = 200, maxExportRows = 5000;
            var rows = new List<AuditLogRecord>();
            for (var page = 1; rows.Count < maxExportRows; page++)
            {
                var batch = await reader.ListAsync(module, page, pageSize, ct);
                rows.AddRange(batch.Items);
                if (batch.Items.Count < pageSize || rows.Count >= batch.Total)
                {
                    break;
                }
            }

            var csv = Csv.Document(
                ["timestamp", "module", "level", "action", "actor", "role", "entityType", "entityId", "detail"],
                rows.Where(r => LogMatches(r, level, q)).Take(maxExportRows).Select(r =>
                {
                    var (lvl, text) = SplitLevel(r.Detail);
                    return new string?[]
                    {
                        r.Timestamp.ToString("o"), r.Module, lvl, r.Action, r.Actor, r.Role,
                        r.EntityType, r.EntityId, text,
                    };
                }));
            return Results.Text(csv, "text/csv");
        });

        // ── Alerts preferences (persisted via the shared settings store; delivery is a separate feature) ──
        group.MapGet("/alerts", async (ICurrentUser current, AlertSettingsService alerts, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var a = await alerts.GetAsync(ct);
            return Results.Ok(new { a.UploadFailures, a.QuotaWarnings, a.OauthExpiry, a.WeeklyDigest });
        });

        group.MapPatch("/alerts", async (
            UpdateAlertsRequest body, ICurrentUser current, AlertSettingsService alerts, IAuditLog audit, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var a = await alerts.UpdateAsync(body.UploadFailures, body.QuotaWarnings, body.OauthExpiry, body.WeeklyDigest, ct);
            await audit.WriteAsync("system.alerts-updated", module: "system",
                detail: $"failures={a.UploadFailures}, quota={a.QuotaWarnings}, oauth={a.OauthExpiry}, digest={a.WeeklyDigest}", ct: ct);
            return Results.Ok(new { a.UploadFailures, a.QuotaWarnings, a.OauthExpiry, a.WeeklyDigest });
        });

        // ── Danger Zone — cross-module fan-out, each module audits its own slice ──
        // The fan-out is NOT one transaction: each module's op is its own commit and may already have
        // succeeded when a LATER module throws. So we isolate every module in its own try/catch — one
        // failure never aborts the others, the host ALWAYS writes its audit entry (recording a partial
        // outcome + which module failed), and the caller gets a visible `partial` signal instead of a
        // bare 500 that would hide "some modules were already changed".
        group.MapPost("/pause-all", async (
            ICurrentUser current, IEnumerable<IMaintenanceControl> controls, IAuditLog audit, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            var (results, failures) = await FanOutAsync(controls, (c, t) => c.PauseAllAsync(t), ct);
            var total = results.Sum(r => r.Affected);
            await audit.WriteAsync("system.pause-all", module: "system",
                detail: failures.Count == 0
                    ? $"paused {total} automation(s) across {results.Count} module(s)"
                    : $"PARTIAL — paused {total} automation(s) across {results.Count} module(s); {FailDetail(failures)}",
                ct: ct);
            return Results.Ok(new
            {
                paused = total,
                partial = failures.Count > 0,
                byModule = results.Select(r => new { r.Module, r.Affected, r.Detail }),
                failed = failures.Select(f => new { f.Module, f.Error }),
            });
        });

        group.MapPost("/reset", async (
            ResetRequest body, ICurrentUser current, IEnumerable<IMaintenanceControl> controls, IAuditLog audit, CancellationToken ct) =>
        {
            if (!current.IsAuthenticated)
            {
                return Results.Problem(statusCode: 401, title: "unauthorized");
            }

            if (!string.Equals(body?.Confirm, ResetPhrase, StringComparison.Ordinal))
            {
                return Results.Problem(statusCode: 400, title: "confirmation_required",
                    detail: $"Type {ResetPhrase} to confirm — this clears operational data and can't be undone.");
            }

            var (results, failures) = await FanOutAsync(controls, (c, t) => c.ResetDataAsync(t), ct);
            var total = results.Sum(r => r.Affected);
            var perModule = string.Join("; ", results.Select(r => $"{r.Module} → {r.Detail}"));
            // Written AFTER the wipe and NEVER cleared (reset keeps audit_logs), so the trail always records it
            // — including a partial outcome where one module wiped and another failed.
            await audit.WriteAsync("system.reset", module: "system",
                detail: failures.Count == 0
                    ? $"cleared {total} operational row(s): {perModule}"
                    : $"PARTIAL — cleared {total} operational row(s) [{perModule}]; {FailDetail(failures)}",
                ct: ct);
            return Results.Ok(new
            {
                cleared = total,
                partial = failures.Count > 0,
                byModule = results.Select(r => new { r.Module, r.Affected, r.Detail }),
                failed = failures.Select(f => new { f.Module, f.Error }),
            });
        });
    }

    /// <summary>
    /// Runs a Danger-Zone op across every registered module independently. A module that throws is recorded
    /// in <paramref name="op"/>'s failure list and the loop CONTINUES — the operation is operational-only, so
    /// there is no cross-module rollback; the goal is to apply what we can and report the rest, never to abort
    /// silently after a partial change. Returns the per-module successes plus (module, error) failures.
    /// </summary>
    private static async Task<(List<MaintenanceResult> Results, List<(string Module, string Error)> Failures)> FanOutAsync(
        IEnumerable<IMaintenanceControl> controls,
        Func<IMaintenanceControl, CancellationToken, Task<MaintenanceResult>> op,
        CancellationToken ct)
    {
        var results = new List<MaintenanceResult>();
        var failures = new List<(string Module, string Error)>();
        foreach (var control in controls)
        {
            try
            {
                results.Add(await op(control, ct));
            }
            catch (Exception ex)
            {
                failures.Add((control.Module, ex.Message));
            }
        }

        return (results, failures);
    }

    private static string FailDetail(IReadOnlyList<(string Module, string Error)> failures) =>
        $"{failures.Count} module(s) FAILED: " + string.Join("; ", failures.Select(f => $"{f.Module}: {f.Error}"));

    // ── Mirror the web Logs view's level/search derivation so the CSV matches what the page shows ──

    private static readonly Regex LevelMarker = new(@"^\[(\w+)\]\s*", RegexOptions.Compiled);

    /// <summary>Splits the folded <c>[Level]</c> marker off the detail, returning (level, remaining text).</summary>
    private static (string Level, string Text) SplitLevel(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return ("info", "");
        }

        var m = LevelMarker.Match(detail);
        if (!m.Success)
        {
            return ("info", detail);
        }

        var level = m.Groups[1].Value.ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" or "warn" => "warn",
            "success" => "success",
            _ => "info",
        };
        return (level, detail[m.Length..]);
    }

    private static bool LogMatches(AuditLogRecord r, string? level, string? q)
    {
        var (lvl, text) = SplitLevel(r.Detail);
        if (!string.IsNullOrEmpty(level) && level != "all" && lvl != level)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            var message = (string.IsNullOrEmpty(text) ? r.Action : text).ToLowerInvariant();
            var target = (r.EntityId ?? r.EntityType ?? r.Actor)?.ToLowerInvariant() ?? "";
            if (!message.Contains(needle) && !target.Contains(needle))
            {
                return false;
            }
        }

        return true;
    }
}
