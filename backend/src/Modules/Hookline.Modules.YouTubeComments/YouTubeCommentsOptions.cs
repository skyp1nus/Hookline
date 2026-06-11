using Microsoft.Extensions.Options;

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
    /// All keys share this limit (the shared key record carries no per-key quota field). Validated on
    /// startup to be within <see cref="MinDailyQuotaUnits"/>..<see cref="MaxDailyQuotaUnits"/> — a
    /// non-positive ceiling would silently break the dashboard quota math.</summary>
    public int DailyQuotaUnits { get; set; } = 10000;

    /// <summary>Lower bound for <see cref="DailyQuotaUnits"/> (a ceiling must be at least one unit).</summary>
    public const int MinDailyQuotaUnits = 1;

    /// <summary>Upper bound for <see cref="DailyQuotaUnits"/> — generous headroom over Google's default
    /// for an approved quota increase, while still catching an absurd typo.</summary>
    public const int MaxDailyQuotaUnits = 50_000_000;

    public DeliverySettings Delivery { get; set; } = new();
    public RetentionSettings Retention { get; set; } = new();

    public sealed class SlackSettings
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";

        /// <summary>Must match an app-configured redirect URL exactly.</summary>
        public string RedirectUri { get; set; } = "";

        /// <summary>Slack app signing secret — verifies the X-Slack-Signature on the interactivity
        /// callback (the "Reject on YouTube" button). REQUIRED in Production: GuardSecurityConfig refuses
        /// to boot if it is empty or a placeholder, because the verifier is fail-closed and an empty
        /// secret would 401 every button press invisibly. Defaults to empty only for Development/tests.</summary>
        public string SigningSecret { get; set; } = "";
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

/// <summary>
/// Validates <see cref="YouTubeCommentsOptions"/> on startup (wired with <c>ValidateOnStart</c>). Today it
/// guards <see cref="YouTubeCommentsOptions.DailyQuotaUnits"/>: a non-positive ceiling would make the
/// per-key quota math (capacity = active-keys × ceiling) and the dashboard meter meaningless, so a
/// misconfiguration fails the host fast instead of silently producing a broken meter.
/// </summary>
public sealed class YouTubeCommentsOptionsValidator : IValidateOptions<YouTubeCommentsOptions>
{
    public ValidateOptionsResult Validate(string? name, YouTubeCommentsOptions options)
    {
        if (options.DailyQuotaUnits is < YouTubeCommentsOptions.MinDailyQuotaUnits
                                      or > YouTubeCommentsOptions.MaxDailyQuotaUnits)
        {
            return ValidateOptionsResult.Fail(
                $"YouTubeComments:DailyQuotaUnits must be between {YouTubeCommentsOptions.MinDailyQuotaUnits} and " +
                $"{YouTubeCommentsOptions.MaxDailyQuotaUnits:N0} (daily YouTube Data API units per key); " +
                $"got {options.DailyQuotaUnits}.");
        }

        return ValidateOptionsResult.Success;
    }
}
