using Hookline.SharedKernel.Settings;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>Upload defaults editable from the Settings tab. <see cref="CategoryId"/> / <see cref="Language"/>
/// use "" to mean "don't set" (None).</summary>
public sealed record UploadSettings(
    string Visibility, int ChunkSizeMb, bool MadeForKids, bool ContainsSyntheticMedia,
    string CategoryId, string Language, bool PublicStatsViewable);

/// <summary>
/// Reads/writes the module's upload defaults through the shared <see cref="ISettingsStore"/>
/// (db-override → env → default), keyed under <c>youtube-uploads:upload:*</c>. Replaces the old
/// module-local <c>app_settings</c> table.
/// </summary>
public sealed class UploadSettingsService(ISettingsStore settings, IOptions<YouTubeUploadsOptions> options)
{
    private const string KeyVisibility = "youtube-uploads:upload:visibility";
    private const string KeyChunk = "youtube-uploads:upload:chunkSizeMb";
    private const string KeyKids = "youtube-uploads:upload:madeForKids";
    private const string KeySynthetic = "youtube-uploads:upload:containsSyntheticMedia";
    private const string KeyCategory = "youtube-uploads:upload:categoryId";
    private const string KeyLanguage = "youtube-uploads:upload:language";
    private const string KeyPublicStats = "youtube-uploads:upload:publicStatsViewable";

    public async Task<UploadSettings> GetUploadSettingsAsync(CancellationToken ct = default)
    {
        var visibility = await settings.GetAsync(KeyVisibility, "private", ct);
        var chunkRaw = await settings.GetAsync(KeyChunk, options.Value.TransferChunkSizeMb.ToString(), ct);
        var kidsRaw = await settings.GetAsync(KeyKids, "false", ct);
        var synthRaw = await settings.GetAsync(KeySynthetic, "false", ct);
        var categoryId = await settings.GetAsync(KeyCategory, "", ct);
        var language = await settings.GetAsync(KeyLanguage, "", ct);
        var publicStatsRaw = await settings.GetAsync(KeyPublicStats, "true", ct);

        var chunk = int.TryParse(chunkRaw, out var c) ? c : options.Value.TransferChunkSizeMb;
        // publicStatsViewable defaults to true (= YouTube's own default) on any unparseable value.
        var publicStats = !bool.TryParse(publicStatsRaw, out var ps) || ps;
        return new UploadSettings(
            visibility,
            chunk,
            bool.TryParse(kidsRaw, out var k) && k,
            bool.TryParse(synthRaw, out var s) && s,
            categoryId,
            language,
            publicStats);
    }

    public async Task UpdateUploadSettingsAsync(
        string visibility, int chunkSizeMb, bool madeForKids, bool containsSyntheticMedia,
        string categoryId, string language, bool publicStatsViewable, CancellationToken ct = default)
    {
        await settings.SetAsync(KeyVisibility, visibility, ct);
        await settings.SetAsync(KeyChunk, chunkSizeMb.ToString(), ct);
        await settings.SetAsync(KeyKids, madeForKids.ToString(), ct);
        await settings.SetAsync(KeySynthetic, containsSyntheticMedia.ToString(), ct);
        await settings.SetAsync(KeyCategory, categoryId, ct);
        await settings.SetAsync(KeyLanguage, language, ct);
        await settings.SetAsync(KeyPublicStats, publicStatsViewable.ToString(), ct);
    }
}
