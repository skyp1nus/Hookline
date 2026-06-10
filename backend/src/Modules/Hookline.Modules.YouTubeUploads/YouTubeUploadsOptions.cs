namespace Hookline.Modules.YouTubeUploads;

/// <summary>
/// Module configuration, bound from the <c>YouTubeUploads</c> config section. Slack app credentials +
/// the Google OAuth redirect live here; per-project Google client id/secret come from the
/// module-local Projects store (not config). The shared <c>TokenEncryption:Key</c> drives the
/// shared protector (registered in Infrastructure), so it is not duplicated here.
/// </summary>
public sealed class YouTubeUploadsOptions
{
    public const string Section = "YouTubeUploads";

    public SlackSettings Slack { get; set; } = new();
    public GoogleSettings Google { get; set; } = new();

    /// <summary>This backend's public URL (provider OAuth redirects resolve against it).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Web panel origin — OAuth callbacks bounce the browser back here.</summary>
    public string AdminPanelUrl { get; set; } = "http://localhost:3000";

    /// <summary>Drive staging directory for downloads.</summary>
    public string TempDownloadDir { get; set; } = "./tmp";

    /// <summary>Per-project daily <c>videos.insert</c> ceiling (Google default 100/project/day) — the real
    /// upload gate, separate from the informational unit pool below.</summary>
    public int YouTubeDailyUploadLimit { get; set; } = 100;

    /// <summary>Informational daily unit pool for NON-upload endpoints (Google default ~10000).</summary>
    public int YouTubeDailyQuotaUnits { get; set; } = 10000;

    /// <summary>Informational daily Drive query ceiling per project (never gates).</summary>
    public long DriveDailyQueryLimit { get; set; } = 1_000_000_000;

    /// <summary>Drive download + YouTube upload chunk size in MB.</summary>
    public int TransferChunkSizeMb { get; set; } = 64;

    /// <summary>Transfer chunk size in bytes (clamped to ≥1 MB).</summary>
    public int TransferChunkSizeBytes => (TransferChunkSizeMb < 1 ? 1 : TransferChunkSizeMb) * 1024 * 1024;

    public sealed class SlackSettings
    {
        /// <summary>App-level signing secret (env only — OAuth install does not return it).</summary>
        public string? SigningSecret { get; set; }
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        /// <summary>Must match an app-configured redirect URL exactly.</summary>
        public string RedirectUri { get; set; } = "";
    }

    public sealed class GoogleSettings
    {
        /// <summary>Every project's OAuth client must register this exact redirect URI.</summary>
        public string RedirectUri { get; set; } = "";

        // Optional seed: becomes the "Default (env)" Project on first boot if no project exists yet.
        public string? SeedClientId { get; set; }
        public string? SeedClientSecret { get; set; }
        public string SeedLabel { get; set; } = "Default (env)";
    }
}
