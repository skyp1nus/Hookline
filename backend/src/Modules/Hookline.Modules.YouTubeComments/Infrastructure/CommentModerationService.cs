using System.Net;

using Google;

using Hookline.Modules.YouTubeComments.Domain;
using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>The Slack user who pressed the button — the honest moderation actor (the shared audit actor
/// is "system" on a provider callback, so this is recorded explicitly).</summary>
public readonly record struct SlackActor(string? UserId, string? UserName)
{
    /// <summary>Human label for the Slack card status line ("removed by …").</summary>
    public string Display => string.IsNullOrEmpty(UserName)
        ? (string.IsNullOrEmpty(UserId) ? "a Slack user" : $"<@{UserId}>")
        : $"@{UserName}";

    /// <summary>The value stamped into the shared audit <c>Actor</c> column. Carries BOTH the Slack
    /// handle and the immutable user id for non-repudiation, so System→Logs shows who moderated a
    /// comment instead of "anonymous" (the /slack callback bypasses identity, so the request principal
    /// is anonymous and cannot be the audit actor).</summary>
    public string AuditActor
    {
        get
        {
            var hasId = !string.IsNullOrEmpty(UserId);
            var hasName = !string.IsNullOrEmpty(UserName);
            if (hasId && hasName) return $"@{UserName} (slack:{UserId})";
            if (hasName) return $"@{UserName}";
            if (hasId) return $"slack:{UserId}";
            return "a Slack user";
        }
    }
}

/// <summary>The terminal outcome of a reject action, mapped by the endpoint to a Slack response.</summary>
public enum ModerationOutcome
{
    /// <summary>Rejected on YouTube just now.</summary>
    Rejected,

    /// <summary>A prior moderation row already exists (double-click / already actioned).</summary>
    AlreadyDone,

    /// <summary>YouTube reported the comment is already gone (404) — treated as success.</summary>
    AlreadyGoneOnYouTube,

    /// <summary>No force-ssl Google account is connected for this channel — honest, not silent.</summary>
    NotConnected,

    /// <summary>YouTube refused (not the channel owner / insufficient permission).</summary>
    Forbidden,

    /// <summary>The OAuth project's quota is exhausted.</summary>
    QuotaExceeded,

    /// <summary>An unexpected failure — logged + audited.</summary>
    Failed,
}

/// <summary>Outcome + a human message safe to show in Slack.</summary>
public sealed record ModerationResult(ModerationOutcome Outcome, string Message)
{
    public bool CardShouldShowRejected => Outcome is ModerationOutcome.Rejected
        or ModerationOutcome.AlreadyDone or ModerationOutcome.AlreadyGoneOnYouTube;
}

/// <summary>
/// Performs the "Reject on YouTube" action: resolves the moderation credential for the comment's
/// owning channel (via the shared <see cref="IGoogleChannelCredentials"/> contract — never a reference
/// to the Uploads module), calls <c>comments.setModerationStatus = rejected</c>, and records a durable
/// idempotency row + an audit entry. Every terminal path is honest — no scope, not owner, already
/// gone, and quota each map to a distinct message rather than a silent failure.
/// </summary>
public sealed class CommentModerationService(
    YouTubeCommentsDbContext db,
    IEnumerable<IGoogleChannelCredentials> channelCredentialProviders,
    IGoogleConnections googleConnections,
    IYouTubeModerationClient moderation,
    ICommentsAudit audit,
    ILogger<CommentModerationService> logger)
{
    // Optional dependency: the credentials contract is implemented by the Uploads module. In a build
    // without it (Uploads absent), moderation degrades to an honest "unavailable", never a crash.
    private readonly IGoogleChannelCredentials? _channelCredentials = channelCredentialProviders.FirstOrDefault();

    /// <summary>
    /// True when an ACTIVE Google account owns <paramref name="youtubeChannelId"/> AND has been granted
    /// the force-ssl scope — i.e. the "Reject" button can act. Reads the shared store's scope snapshot
    /// (a contract call, not a join); used for scope-detection surfaces.
    /// </summary>
    public async Task<bool> CanModerateAsync(string youtubeChannelId, CancellationToken ct = default)
    {
        if (_channelCredentials is null || string.IsNullOrEmpty(youtubeChannelId))
            return false;

        foreach (var summary in await googleConnections.ListAsync(ct))
        {
            if (!summary.IsActive)
                continue;
            var detail = await googleConnections.GetAsync(summary.Id, ct);
            if (detail is { IsActive: true, ChannelId: { } channelId }
                && string.Equals(channelId, youtubeChannelId, StringComparison.Ordinal)
                && detail.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(GoogleScopes.YouTubeForceSsl, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<ModerationResult> RejectAsync(
        Guid mappingId, string commentId, SlackActor actor, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(commentId))
            return new ModerationResult(ModerationOutcome.Failed, "Missing comment reference.");

        // Resolve the owning YouTube channel id for this mapping (also tells us the mapping exists).
        var youtubeChannelId = await db.ChannelMappings.AsNoTracking()
            .Where(m => m.Id == mappingId)
            .Select(m => m.YouTubeChannel!.YouTubeChannelId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(youtubeChannelId))
            return new ModerationResult(ModerationOutcome.Failed, "This comment's channel mapping no longer exists.");

        // Idempotency: a prior row short-circuits a double-click without touching YouTube.
        var existing = await db.CommentModerations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MappingId == mappingId && x.CommentId == commentId, ct);
        if (existing is not null)
            return new ModerationResult(ModerationOutcome.AlreadyDone,
                $"Already removed by {Actor(existing)}.");

        if (_channelCredentials is null)
            return new ModerationResult(ModerationOutcome.NotConnected,
                "Comment removal is unavailable — the Google credentials provider is not loaded.");

        var credential = await _channelCredentials.GetModerationCredentialAsync(youtubeChannelId, ct);
        if (credential is null)
        {
            await LogAsync(AuditLevel.Warning, commentId, youtubeChannelId, actor,
                "Reject skipped: no force-ssl Google account connected for the channel");
            return new ModerationResult(ModerationOutcome.NotConnected,
                "No Google account with comment-management permission is connected for this channel. "
                + "Re-connect it in Connections → Google (grant the comment-management permission) to enable removal.");
        }

        try
        {
            await moderation.RejectAsync(credential.AccessToken, commentId, ct);
        }
        catch (GoogleApiException ex) when (ex.HasReason("quotaExceeded"))
        {
            await LogAsync(AuditLevel.Warning, commentId, youtubeChannelId, actor, "Reject failed: OAuth quota exhausted");
            return new ModerationResult(ModerationOutcome.QuotaExceeded,
                "YouTube quota for the connected account is exhausted — try again later.");
        }
        catch (GoogleApiException ex) when (IsAlreadyGone(ex))
        {
            try { await RecordAsync(mappingId, commentId, actor, CommentModeration.StatusAlreadyGone, ct); }
            catch (DbUpdateException) { /* a concurrent click already recorded it (unique index) — fine */ }
            await LogAsync(AuditLevel.Information, commentId, youtubeChannelId, actor, "Comment already gone on YouTube (treated as removed)");
            return new ModerationResult(ModerationOutcome.AlreadyGoneOnYouTube, "That comment was already gone on YouTube.");
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            await LogAsync(AuditLevel.Warning, commentId, youtubeChannelId, actor,
                $"Reject forbidden by YouTube: {ex.Error?.Message ?? ex.Message}");
            return new ModerationResult(ModerationOutcome.Forbidden,
                "YouTube refused the removal — the connected account isn't authorized to moderate this comment.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reject failed for comment {CommentId} on mapping {MappingId}", commentId, mappingId);
            await LogAsync(AuditLevel.Error, commentId, youtubeChannelId, actor, $"Reject failed: {ex.Message}");
            return new ModerationResult(ModerationOutcome.Failed, "Couldn't remove the comment — an unexpected error occurred.");
        }

        try
        {
            await RecordAsync(mappingId, commentId, actor, CommentModeration.StatusRejected, ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent click (unique index). The reject itself succeeded/idempotent.
            return new ModerationResult(ModerationOutcome.AlreadyDone, "Already removed.");
        }

        await LogAsync(AuditLevel.Information, commentId, youtubeChannelId, actor, "Comment rejected on YouTube");
        return new ModerationResult(ModerationOutcome.Rejected, "🚫 Removed on YouTube.");
    }

    private async Task RecordAsync(Guid mappingId, string commentId, SlackActor actor, string status, CancellationToken ct)
    {
        db.CommentModerations.Add(new CommentModeration
        {
            MappingId = mappingId,
            CommentId = commentId,
            Action = CommentModeration.ActionRejected,
            Status = status,
            SlackUserId = actor.UserId,
            SlackUserName = actor.UserName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private Task LogAsync(string level, string commentId, string channelId, SlackActor actor, string message) =>
        audit.LogAsync(level, "Moderation", message, "Comment", commentId,
            // Stamp the moderating Slack user as the explicit audit ACTOR (the /slack callback is
            // identity-bypassed, so the request principal is anonymous). The id/name also stay in the
            // detail JSON + the comment_moderations row for cross-referencing.
            actor: actor.AuditActor,
            details: $"{{\"channel\":\"{channelId}\",\"slackUser\":\"{actor.UserId}\",\"slackUserName\":\"{actor.UserName}\"}}");

    private static string Actor(CommentModeration row) =>
        string.IsNullOrEmpty(row.SlackUserName)
            ? (string.IsNullOrEmpty(row.SlackUserId) ? "a Slack user" : $"<@{row.SlackUserId}>")
            : $"@{row.SlackUserName}";

    /// <summary>A 404 (or commentNotFound) means the comment no longer exists — already gone.</summary>
    private static bool IsAlreadyGone(GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.NotFound || ex.HasReason("commentNotFound");
}
