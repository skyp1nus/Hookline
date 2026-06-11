namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>action_id values used on Block Kit buttons (matched in the interactivity handler).</summary>
public static class SlackActions
{
    /// <summary>The existing link button that deep-links to the comment on YouTube (no interactivity).</summary>
    public const string OpenComment = "open_comment";

    /// <summary>The "Reject on YouTube" button — moderates (hides) the comment via the OAuth account.</summary>
    public const string RejectComment = "reject_comment";
}
