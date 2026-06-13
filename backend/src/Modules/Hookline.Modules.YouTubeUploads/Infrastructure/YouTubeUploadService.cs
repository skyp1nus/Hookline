using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record YouTubeUploadResult(string VideoId, string Url);

public sealed class YouTubeUploadService(GoogleCredentialFactory factory)
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;

    public YouTubeService BuildService(string clientId, string clientSecret, string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(clientId, clientSecret, refreshToken),
            ApplicationName = "YouTubeUploads",
        });

    /// <summary>The authenticated account's own channel id + title + avatar (channels.list?mine=true, ~1 unit).
    /// The single caller (<c>GoogleOAuthService.ExchangeAndStoreAsync</c>) meters this against the unit pool;
    /// any new caller must charge <c>IQuotaService.ChargeUnitsAsync</c> too.</summary>
    public async Task<(string? Id, string? Title, string? AvatarUrl)> GetChannelInfoAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
    {
        var request = BuildService(clientId, clientSecret, refreshToken).Channels.List("snippet");
        request.Mine = true;
        var response = await request.ExecuteAsync(ct);
        var item = response.Items?.FirstOrDefault();
        var thumbs = item?.Snippet?.Thumbnails;
        var avatar = thumbs?.High?.Url ?? thumbs?.Medium?.Url ?? thumbs?.Default__?.Url;
        return (item?.Id, item?.Snippet?.Title, avatar);
    }

    /// <summary>
    /// Resumable upload as PRIVATE. <paramref name="onBytes"/> fires during transfer;
    /// <paramref name="onProcessing"/> fires once all bytes are sent (YouTube still transcodes).
    /// Returns the new video id. Note: a failed resumable upload surfaces via IUploadProgress,
    /// not an exception — we inspect the result status explicitly.
    /// </summary>
    public async Task<YouTubeUploadResult> UploadAsync(
        YouTubeService service,
        Stream videoStream,
        string title,
        string? description,
        IList<string> tags,
        Action<long> onBytes,
        Action onProcessing,
        UploadSettings settings,
        int chunkSize,
        CancellationToken ct)
    {
        var video = BuildVideo(title, description, tags, settings);

        var request = service.Videos.Insert(video, "snippet,status", videoStream, "video/*");
        request.NotifySubscribers = false;
        request.ChunkSize = chunkSize; // fewer resumable-upload requests on large files

        string? videoId = null;
        var processingFired = false;
        request.ProgressChanged += p =>
        {
            switch (p.Status)
            {
                case UploadStatus.Uploading:
                    onBytes(p.BytesSent);
                    break;
                case UploadStatus.Completed when !processingFired:
                    processingFired = true;
                    onProcessing();
                    break;
            }
        };
        request.ResponseReceived += v => videoId = v.Id;

        var result = await request.UploadAsync(ct);
        if (result.Status == UploadStatus.Failed)
            throw new InvalidOperationException("YouTube upload failed.", result.Exception);
        if (string.IsNullOrEmpty(videoId))
            throw new InvalidOperationException("YouTube upload completed but returned no video id.");

        return new YouTubeUploadResult(videoId, $"https://youtu.be/{videoId}");
    }

    /// <summary>
    /// Builds the <c>videos.insert</c> resource from the title/description/tags plus the default video
    /// settings (Settings page → <see cref="UploadSettingsService"/>). This is the single place the persisted
    /// settings become a YouTube <see cref="Video"/>; kept separate from the network call so the
    /// settings→resource mapping is unit-testable without hitting the API. Studio "Upload defaults" are
    /// ignored for API uploads, so every default we want applied is set here explicitly.
    /// </summary>
    internal static Video BuildVideo(string title, string? description, IList<string> tags, UploadSettings s)
    {
        // None ("") on category/language means leave the field unset (null), not blank.
        var language = NormalizeLanguage(s.Language) is { Length: > 0 } l ? l : null;
        return new Video
        {
            Snippet = new VideoSnippet
            {
                Title = NormalizeTitle(title),
                Description = NormalizeDescription(description),
                Tags = NormalizeTags(tags),
                CategoryId = NormalizeCategoryId(s.CategoryId) is { Length: > 0 } c ? c : null,
                DefaultLanguage = language,
                DefaultAudioLanguage = language,
            },
            Status = new VideoStatus
            {
                PrivacyStatus = NormalizeVisibility(s.Visibility),
                // YouTube COPPA self-declaration: false = "No, not made for kids". madeForKids is read-only.
                SelfDeclaredMadeForKids = s.MadeForKids,
                // Altered/synthetic (AI) content disclosure: false = "No". Settable since the Oct-2024 API rev.
                ContainsSyntheticMedia = s.ContainsSyntheticMedia,
                // Shows the public like count on the watch page; true = YouTube's own default.
                PublicStatsViewable = s.PublicStatsViewable,
            },
        };
    }

    /// <summary>
    /// Sets a custom thumbnail on an existing video (<c>thumbnails.set</c>, ~50 units). Best-effort:
    /// returns false instead of throwing on a failed upload. The channel must have the custom-thumbnail
    /// feature enabled (verified account) or YouTube returns 403. NOTE: YouTube silently IGNORES custom
    /// thumbnails on Shorts — the call can report success yet the Short keeps its auto-generated frame.
    /// </summary>
    public async Task<bool> SetThumbnailAsync(
        YouTubeService service, string videoId, Stream image, string contentType, CancellationToken ct = default)
    {
        var request = service.Thumbnails.Set(videoId, image, contentType);
        var result = await request.UploadAsync(ct);
        if (result.Status != UploadStatus.Completed)
            throw new InvalidOperationException("thumbnails.set did not complete.", result.Exception);
        return true;
    }

    // YouTube rejects any '<' or '>' in a title/description (invalidTitle / invalidDescription).
    // Slack markup is already unwrapped upstream; these strips are the last-line guard and also
    // sanitise jobs whose description was captured before that unwrap existed.
    internal static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Untitled upload";
        title = StripAngleBrackets(title).Trim();
        if (title.Length == 0) return "Untitled upload";
        return title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];
    }

    internal static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) return string.Empty;
        // Also strip + convert at upload time (not just parse) so jobs queued before these fixes existed still
        // upload clean text. Idempotent — already-unwrapped/converted text has nothing left to change.
        var d = SlackEmoji.ShortcodesToUnicode(StripAngleBrackets(description));
        return d.Length <= MaxDescriptionLength ? d : d[..MaxDescriptionLength];
    }

    private static string StripAngleBrackets(string s) =>
        s.Replace("<", string.Empty).Replace(">", string.Empty);

    // YouTube returns invalidTags when a tag holds a '<'/'>' or when the tags' combined length
    // exceeds ~500 chars. It serialises tags as an array and wraps any tag containing whitespace in
    // double quotes — those quotes count toward the limit, so a spaced tag costs length + 2. We strip
    // brackets, trim/dedupe, then keep tags until the quote-aware budget is spent. Margin under 500.
    private const int MaxTagLength = 100;
    private const int MaxTagsTotalChars = 480;

    internal static IList<string>? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null) return null;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = 0;
        foreach (var raw in tags)
        {
            var t = StripAngleBrackets(raw).Trim();
            if (t.Length > MaxTagLength) t = t[..MaxTagLength].Trim();
            if (t.Length == 0 || !seen.Add(t)) continue;
            var cost = t.Length + (t.Any(char.IsWhiteSpace) ? 2 : 0);
            if (budget + cost > MaxTagsTotalChars) break;
            budget += cost;
            result.Add(t);
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Only YouTube's three privacy values are valid; anything else falls back to private.</summary>
    public static string NormalizeVisibility(string? visibility) => visibility?.Trim().ToLowerInvariant() switch
    {
        "public" => "public",
        "unlisted" => "unlisted",
        _ => "private",
    };

    // The assignable videoCategory ids that are insertable in the major markets. The Settings picker offers
    // exactly these; anything outside the set (incl. None) maps to "" so BuildVideo leaves categoryId unset.
    private static readonly HashSet<string> AllowedCategoryIds =
    [
        "1", "2", "10", "15", "17", "19", "20", "22", "23", "24", "25", "26", "27", "28", "29",
    ];

    /// <summary>The category id if it is in the assignable whitelist; otherwise "" (= don't set categoryId).</summary>
    public static string NormalizeCategoryId(string? categoryId)
    {
        var c = categoryId?.Trim() ?? string.Empty;
        return AllowedCategoryIds.Contains(c) ? c : string.Empty;
    }

    // BCP-47 codes the Settings picker offers (snippet.defaultLanguage + defaultAudioLanguage). Exact match —
    // the picker only ever sends a code from this list, and anything else (incl. None) maps to "" (don't set).
    private static readonly HashSet<string> AllowedLanguages =
    [
        "en", "uk", "ru", "pl", "es", "de", "fr", "pt", "it", "nl", "tr", "ar", "hi", "ja", "ko", "zh-Hans", "zh-Hant",
    ];

    /// <summary>The language code as-is if it is in the BCP-47 whitelist; otherwise "" (= don't set either language).</summary>
    public static string NormalizeLanguage(string? language)
    {
        var l = language?.Trim() ?? string.Empty;
        return AllowedLanguages.Contains(l) ? l : string.Empty;
    }
}
