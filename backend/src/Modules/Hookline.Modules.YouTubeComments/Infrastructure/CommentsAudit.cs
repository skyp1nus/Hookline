using Hookline.SharedKernel.Audit;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// The closed set of severity levels this module stamps onto audit entries. The shared audit entry has
/// no dedicated severity column by design (see <see cref="CommentsAudit"/>): the level is folded into
/// the <c>Detail</c> as a leading <c>[Level]</c> marker and queried back through
/// <see cref="IAuditLogReader.CountSinceAsync"/>'s <c>detailPrefix</c>. Using these constants instead of
/// bare strings keeps the vocabulary fixed so a typo can't silently fall out of a level-filtered count.
/// </summary>
public static class AuditLevel
{
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
}

/// <summary>
/// Module-local audit facade that maps the legacy level/category/message shape onto the shared
/// <see cref="IAuditLog"/>. The shared log stamps the actor from <c>ICurrentUser</c> automatically
/// (the admin email for an API request, <c>system</c> inside a background job), so the legacy
/// <paramref name="actor"/> argument is accepted for call-site compatibility but ignored. The
/// category becomes the audit <c>Action</c> and the level is folded into the detail; every row is
/// tagged <c>module = "youtube-comments"</c> so the shared System→Logs page can filter to it.
/// </summary>
/// <remarks>
/// <para><b>Severity contract (intentional, queryable).</b> The shared audit entry deliberately carries
/// <i>no</i> separate Level/Severity column. Instead, severity is folded into <c>Detail</c> as a leading
/// <c>[Level]</c> marker (e.g. <c>"[Error] Delivery failed…"</c>). This is the supported query mechanism:
/// <see cref="IAuditLogReader.CountSinceAsync"/> filters by <c>detailPrefix</c> via a literal
/// <c>StartsWith</c> (Postgres treats <c>[</c>/<c>]</c> literally), which is how the dashboard's
/// "errors · 24h" KPI is computed. The marker format lives in one place —
/// <see cref="CommentsAudit.DetailPrefix"/> — and both the writer here and the reader on the dashboard
/// use it, so the two ends cannot drift. Levels come from <see cref="AuditLevel"/>.</para>
/// </remarks>
public interface ICommentsAudit
{
    Task LogAsync(
        string level,
        string category,
        string message,
        string? entityType = null,
        string? entityId = null,
        string? actor = null,
        string? details = null,
        CancellationToken ct = default);
}

public sealed class CommentsAudit(IAuditLog audit) : ICommentsAudit
{
    public const string ModuleName = "youtube-comments";

    /// <summary>
    /// The canonical detail prefix for a severity <paramref name="level"/> (e.g. <c>"[Error]"</c>). This
    /// is the single source of truth shared by the writer (this facade) and the reader (the dashboard's
    /// error-count query), so a level-filtered count can never drift from how the level is stamped.
    /// </summary>
    public static string DetailPrefix(string level) => $"[{level}]";

    public Task LogAsync(
        string level,
        string category,
        string message,
        string? entityType = null,
        string? entityId = null,
        string? actor = null,
        string? details = null,
        CancellationToken ct = default)
    {
        var prefix = DetailPrefix(level);
        var detail = details is null ? $"{prefix} {message}" : $"{prefix} {message} {details}";
        return audit.WriteAsync(
            action: category,
            module: ModuleName,
            entityType: entityType,
            entityId: entityId,
            detail: detail,
            ct: ct);
    }
}
