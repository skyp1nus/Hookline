using Hookline.SharedKernel.Settings;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>Upload defaults editable from the Settings tab.</summary>
public sealed record UploadSettings(string Visibility, int ChunkSizeMb, bool MadeForKids, bool ContainsSyntheticMedia);

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

    public async Task<UploadSettings> GetUploadSettingsAsync(CancellationToken ct = default)
    {
        var visibility = await settings.GetAsync(KeyVisibility, "private", ct);
        var chunkRaw = await settings.GetAsync(KeyChunk, options.Value.TransferChunkSizeMb.ToString(), ct);
        var kidsRaw = await settings.GetAsync(KeyKids, "false", ct);
        var synthRaw = await settings.GetAsync(KeySynthetic, "false", ct);

        var chunk = int.TryParse(chunkRaw, out var c) ? c : options.Value.TransferChunkSizeMb;
        return new UploadSettings(
            visibility,
            chunk,
            bool.TryParse(kidsRaw, out var k) && k,
            bool.TryParse(synthRaw, out var s) && s);
    }

    public async Task UpdateUploadSettingsAsync(
        string visibility, int chunkSizeMb, bool madeForKids, bool containsSyntheticMedia, CancellationToken ct = default)
    {
        await settings.SetAsync(KeyVisibility, visibility, ct);
        await settings.SetAsync(KeyChunk, chunkSizeMb.ToString(), ct);
        await settings.SetAsync(KeyKids, madeForKids.ToString(), ct);
        await settings.SetAsync(KeySynthetic, containsSyntheticMedia.ToString(), ct);
    }
}
