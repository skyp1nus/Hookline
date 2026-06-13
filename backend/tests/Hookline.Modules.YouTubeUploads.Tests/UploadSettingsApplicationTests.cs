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

    /// <summary>Builds an <see cref="UploadSettings"/> with only the field under test varied (others = defaults).</summary>
    private static UploadSettings Settings(
        string visibility = "private", bool madeForKids = false, bool containsSyntheticMedia = false,
        string categoryId = "", string language = "", bool publicStatsViewable = true) =>
        new(visibility, ChunkSizeMb: 64, madeForKids, containsSyntheticMedia, categoryId, language, publicStatsViewable);

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
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], Settings(visibility: input));
        Assert.Equal(expected, video.Status.PrivacyStatus);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildVideo_maps_kids_and_synthetic_declarations(bool kids, bool synthetic)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], Settings(madeForKids: kids, containsSyntheticMedia: synthetic));
        Assert.Equal(kids, video.Status.SelfDeclaredMadeForKids!.Value);
        Assert.Equal(synthetic, video.Status.ContainsSyntheticMedia!.Value);
    }

    [Theory]
    [InlineData("", null)]      // None → leave categoryId unset
    [InlineData("27", "27")]    // a whitelisted id passes through
    [InlineData("999", null)]   // outside the whitelist → unset
    public void BuildVideo_maps_categoryId_against_whitelist(string input, string? expected)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], Settings(categoryId: input));
        Assert.Equal(expected, video.Snippet.CategoryId);
    }

    [Theory]
    [InlineData("", null)]      // None → leave both language fields unset
    [InlineData("uk", "uk")]    // a whitelisted code lands on BOTH default + audio language
    [InlineData("xx", null)]    // unknown code → unset
    public void BuildVideo_maps_language_to_both_default_and_audio(string input, string? expected)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], Settings(language: input));
        Assert.Equal(expected, video.Snippet.DefaultLanguage);
        Assert.Equal(expected, video.Snippet.DefaultAudioLanguage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildVideo_maps_publicStatsViewable_to_status(bool value)
    {
        var video = YouTubeUploadService.BuildVideo("Clip", null, [], Settings(publicStatsViewable: value));
        Assert.Equal(value, video.Status.PublicStatsViewable!.Value);
    }

    // ── persistence: the Settings page writes, the upload job reads back the same values ──

    [Fact]
    public async Task UploadSettings_round_trip_persists_all_video_settings()
    {
        var svc = NewService(out _);

        await svc.UpdateUploadSettingsAsync(
            "public", chunkSizeMb: 64, madeForKids: true, containsSyntheticMedia: true,
            categoryId: "27", language: "uk", publicStatsViewable: false);
        var s = await svc.GetUploadSettingsAsync();

        Assert.Equal("public", s.Visibility);
        Assert.True(s.MadeForKids);
        Assert.True(s.ContainsSyntheticMedia);
        Assert.Equal("27", s.CategoryId);
        Assert.Equal("uk", s.Language);
        Assert.False(s.PublicStatsViewable);
    }

    [Fact]
    public async Task UploadSettings_default_to_private_and_no_declarations_when_unset()
    {
        var svc = NewService(out _);

        var s = await svc.GetUploadSettingsAsync();

        Assert.Equal("private", s.Visibility);
        Assert.False(s.MadeForKids);
        Assert.False(s.ContainsSyntheticMedia);
        Assert.Equal("", s.CategoryId);          // None by default
        Assert.Equal("", s.Language);            // None by default
        Assert.True(s.PublicStatsViewable);      // YouTube's own default
    }

    // ── the full chain: persisted settings flow into the uploaded video resource ──

    [Fact]
    public async Task Persisted_settings_flow_into_the_uploaded_video_resource()
    {
        var svc = NewService(out _);
        await svc.UpdateUploadSettingsAsync(
            "unlisted", chunkSizeMb: 64, madeForKids: true, containsSyntheticMedia: true,
            categoryId: "27", language: "uk", publicStatsViewable: false);

        // exactly what UploadJobHandler does: read settings, then hand them to the upload builder.
        var s = await svc.GetUploadSettingsAsync();
        var video = YouTubeUploadService.BuildVideo("Clip", "desc", [], s);

        Assert.Equal("unlisted", video.Status.PrivacyStatus);
        Assert.True(video.Status.SelfDeclaredMadeForKids!.Value);
        Assert.True(video.Status.ContainsSyntheticMedia!.Value);
        Assert.Equal("27", video.Snippet.CategoryId);
        Assert.Equal("uk", video.Snippet.DefaultLanguage);
        Assert.Equal("uk", video.Snippet.DefaultAudioLanguage);
        Assert.False(video.Status.PublicStatsViewable!.Value);
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
