namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>action_id values used on Block Kit buttons (matched in the interactivity handler).</summary>
public static class SlackActions
{
    /// <summary>The existing link button that deep-links to the comment on YouTube (no interactivity).</summary>
    public const string OpenComment = "open_comment";

    /// <summary>The "Reject on YouTube" button — moderates (hides) the comment via the OAuth account.</summary>
    public const string RejectComment = "reject_comment";

    /// <summary>The proactive "Re-consent to enable removal" link button shown INSTEAD of Reject when the
    /// owning Google account lacks the force-ssl scope. It deep-links to Connections → Google; like
    /// <see cref="OpenComment"/> it is a URL button, so the interactivity handler takes no server action.</summary>
    public const string ReConsentGoogle = "reconsent_google";
}
