namespace Hookline.Modules.YouTubeComments.Domain;

/// <summary>
/// A durable record that a comment was moderated (rejected) from Slack — the idempotency ledger for the
/// "Reject on YouTube" action. The unique (MappingId, CommentId) key makes a double-click a no-op and
/// records WHO actioned it (the Slack user) for the "rejected by" card update + audit cross-check.
/// Module-local; references the mapping by id only.
/// </summary>
public class CommentModeration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MappingId { get; set; }

    /// <summary>The YouTube comment id that was moderated.</summary>
    public string CommentId { get; set; } = default!;

    /// <summary>The moderation action taken — currently always <c>Rejected</c> (room for future kinds).</summary>
    public string Action { get; set; } = ActionRejected;

    /// <summary>Outcome status: <c>Rejected</c> (we rejected it) or <c>AlreadyGone</c> (404 on YouTube).</summary>
    public string Status { get; set; } = StatusRejected;

    /// <summary>The Slack user who pressed the button (the honest actor — the audit actor is "system").</summary>
    public string? SlackUserId { get; set; }
    public string? SlackUserName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public const string ActionRejected = "Rejected";
    public const string StatusRejected = "Rejected";
    public const string StatusAlreadyGone = "AlreadyGone";
}
