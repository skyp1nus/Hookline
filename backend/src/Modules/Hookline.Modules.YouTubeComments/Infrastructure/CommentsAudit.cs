using Hookline.SharedKernel.Audit;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Module-local audit facade that maps the legacy level/category/message shape onto the shared
/// <see cref="IAuditLog"/>. The shared log stamps the actor from <c>ICurrentUser</c> automatically
/// (the admin email for an API request, <c>system</c> inside a background job), so the legacy
/// <paramref name="actor"/> argument is accepted for call-site compatibility but ignored. The
/// category becomes the audit <c>Action</c> and the level is folded into the detail; every row is
/// tagged <c>module = "youtube-comments"</c> so the shared System→Logs page can filter to it.
/// </summary>
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
        var detail = details is null ? $"[{level}] {message}" : $"[{level}] {message} {details}";
        return audit.WriteAsync(
            action: category,
            module: ModuleName,
            entityType: entityType,
            entityId: entityId,
            detail: detail,
            ct: ct);
    }
}
