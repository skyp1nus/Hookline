using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Settings;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// Pins the default-video-settings pipeline end-to-end at every seam reachable without the YouTube network:
/// (1) the Settings page's three knobs persist + read back through <see cref="UploadSettingsService"/>, and
/// (2) those persisted values land on the exact <c>videos.insert</c> resource fields YouTube reads
/// (<see cref="YouTubeUploadService.BuildVideo"/>). The only untested hop is the live insert itself; the
/// <see cref="UploadJobHandler"/> → <see cref="YouTubeUploadService.UploadAsync"/> call is type-checked by build.
/// </summary>
public sealed class UploadSettingsApplicationTests
{
    private static UploadSettingsService NewService(out FakeSettingsStore store)
    {
        store = new FakeSettingsStore();
        return new UploadSettingsService(store, Options.Create(new YouTubeUploadsOptions()));
    }

    // ── settings → Video resource (the place persisted settings become a YouTube upload) ──

    [Theory]
    [InlineData("public", "public")]
    [InlineData("unlisted", "unlisted")]
    [InlineData("private", "private")]
    [InlineData("PUBLIC", "public")]   // normalized to lowercase
    [InlineData("garbage", "private")] // anything invalid falls back to private
    [InlineData("", "private")]
    public void BuildVideo_maps_visibility_to_privacyStatus(string input, string expected)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], input, madeForKids: false, containsSyntheticMedia: false);
        Assert.Equal(expected, video.Status.PrivacyStatus);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildVideo_maps_kids_and_synthetic_declarations(bool kids, bool synthetic)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], "private", kids, synthetic);
        Assert.Equal(kids, video.Status.SelfDeclaredMadeForKids!.Value);
        Assert.Equal(synthetic, video.Status.ContainsSyntheticMedia!.Value);
    }

    // ── persistence: the Settings page writes, the upload job reads back the same values ──

    [Fact]
    public async Task UploadSettings_round_trip_persists_all_three_video_settings()
    {
        var svc = NewService(out _);

        await svc.UpdateUploadSettingsAsync("public", chunkSizeMb: 64, madeForKids: true, containsSyntheticMedia: true);
        var s = await svc.GetUploadSettingsAsync();

        Assert.Equal("public", s.Visibility);
        Assert.True(s.MadeForKids);
        Assert.True(s.ContainsSyntheticMedia);
    }

    [Fact]
    public async Task UploadSettings_default_to_private_and_no_declarations_when_unset()
    {
        var svc = NewService(out _);

        var s = await svc.GetUploadSettingsAsync();

        Assert.Equal("private", s.Visibility);
        Assert.False(s.MadeForKids);
        Assert.False(s.ContainsSyntheticMedia);
    }

    // ── the full chain: persisted settings flow into the uploaded video resource ──

    [Fact]
    public async Task Persisted_settings_flow_into_the_uploaded_video_resource()
    {
        var svc = NewService(out _);
        await svc.UpdateUploadSettingsAsync("unlisted", chunkSizeMb: 64, madeForKids: true, containsSyntheticMedia: true);

        // exactly what UploadJobHandler does: read settings, then hand them to the upload builder.
        var s = await svc.GetUploadSettingsAsync();
        var video = YouTubeUploadService.BuildVideo("Clip", "desc", [], s.Visibility, s.MadeForKids, s.ContainsSyntheticMedia);

        Assert.Equal("unlisted", video.Status.PrivacyStatus);
        Assert.True(video.Status.SelfDeclaredMadeForKids!.Value);
        Assert.True(video.Status.ContainsSyntheticMedia!.Value);
    }
}

/// <summary>In-memory <see cref="ISettingsStore"/> — mirrors the DB-override layer's get/set without a DbContext.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _store = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

    public Task<string> GetAsync(string key, string fallback, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v : fallback);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }
}
