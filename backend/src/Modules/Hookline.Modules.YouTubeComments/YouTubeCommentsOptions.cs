namespace Hookline.Modules.YouTubeComments;

/// <summary>
/// Module configuration, bound from the <c>YouTubeComments</c> config section. The Slack app
/// credentials + OAuth redirect live here; YouTube comment polling uses API keys (stored in the
/// shared Connections subsystem), not OAuth. The shared <c>TokenEncryption:Key</c> drives the shared
/// protector (registered in Infrastructure), so it is not duplicated here.
/// </summary>
public sealed class YouTubeCommentsOptions
{
    public const string Section = "YouTubeComments";

    public SlackSettings Slack { get; set; } = new();

    /// <summary>This backend's public URL (provider OAuth redirects resolve against it).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Web panel origin — OAuth callbacks bounce the browser back here.</summary>
    public string AdminPanelUrl { get; set; } = "http://localhost:3000";

    /// <summary>Uniform daily YouTube Data API unit ceiling per key (Google default 10000/key/day).
    /// All keys share this limit (the shared key record carries no per-key quota field).</summary>
    public int DailyQuotaUnits { get; set; } = 10000;

    public DeliverySettings Delivery { get; set; } = new();
    public RetentionSettings Retention { get; set; } = new();

    public sealed class SlackSettings
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";

        /// <summary>Must match an app-configured redirect URL exactly.</summary>
        public string RedirectUri { get; set; } = "";
    }

    /// <summary>Controls the recurring delivery-retry job that drains <c>pending_deliveries</c>.</summary>
    public sealed class DeliverySettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Give up (dead-letter) after this many failed attempts.</summary>
        public int MaxAttempts { get; set; } = 8;

        /// <summary>Cron cadence for the retry sweep. Default: every minute.</summary>
        public string Cron { get; set; } = "* * * * *";
    }

    /// <summary>Controls the recurring retention cleanup of the module-local <c>processed_comments</c> ledger.
    /// Audit-log retention is owned by the shared host, not this module.</summary>
    public sealed class RetentionSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Delete processed_comments older than this many days. 0 disables it (the table is a dedup
        /// ledger, so a row is only safe to drop once its comment has fallen out of the poll window).
        /// </summary>
        public int ProcessedCommentDays { get; set; } = 90;

        /// <summary>Cron cadence for the cleanup job. Default: daily at 03:00 UTC.</summary>
        public string Cron { get; set; } = "0 3 * * *";
    }
}
