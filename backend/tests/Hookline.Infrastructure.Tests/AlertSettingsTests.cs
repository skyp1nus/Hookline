using Hookline.Infrastructure.Settings;
using Hookline.SharedKernel.Settings;

namespace Hookline.Infrastructure.Tests;

/// <summary>
/// Alerts persistence (P1). Pins the defaults (failures/quota/oauth on, digest off), that a partial PATCH
/// persists only the supplied fields, and that the saved state round-trips through a fresh read — which is
/// what makes the toggles survive a backend restart (the store is DB-backed in production).
/// </summary>
public sealed class AlertSettingsTests
{
    /// <summary>In-memory <see cref="ISettingsStore"/> — the DB-backed store in production behaves the same.</summary>
    private sealed class DictSettingsStore : ISettingsStore
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

    [Fact]
    public async Task Defaults_when_nothing_is_persisted()
    {
        var svc = new AlertSettingsService(new DictSettingsStore());
        var a = await svc.GetAsync();
        Assert.True(a.UploadFailures);
        Assert.True(a.QuotaWarnings);
        Assert.True(a.OauthExpiry);
        Assert.False(a.WeeklyDigest);
    }

    [Fact]
    public async Task Partial_update_persists_and_round_trips_through_a_fresh_read()
    {
        var store = new DictSettingsStore();

        // First service instance writes the change…
        var saved = await new AlertSettingsService(store).UpdateAsync(
            uploadFailures: false, quotaWarnings: null, oauthExpiry: null, weeklyDigest: true);
        Assert.False(saved.UploadFailures);
        Assert.True(saved.WeeklyDigest);
        Assert.True(saved.QuotaWarnings); // untouched → still the default

        // …a brand-new instance reading the same store sees the persisted state (survives a "restart").
        var reread = await new AlertSettingsService(store).GetAsync();
        Assert.False(reread.UploadFailures);
        Assert.True(reread.WeeklyDigest);
        Assert.True(reread.QuotaWarnings);
        Assert.True(reread.OauthExpiry);
    }
}
