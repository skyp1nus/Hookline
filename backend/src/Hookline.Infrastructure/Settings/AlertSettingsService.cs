using Hookline.SharedKernel.Settings;

namespace Hookline.Infrastructure.Settings;

/// <summary>Persisted Settings → Alerts preferences.</summary>
public sealed record AlertSettings(bool UploadFailures, bool QuotaWarnings, bool OauthExpiry, bool WeeklyDigest);

/// <summary>
/// Reads/writes the Alerts toggles through the shared key/value <see cref="ISettingsStore"/> under the
/// <c>system:alerts:*</c> namespace (no dedicated table — matches how the upload settings are stored).
/// Defaults: upload-failures / quota-warnings / oauth-expiry on, weekly-digest off.
/// <para><b>Scope:</b> this persists the user PREFERENCE only. Alert <i>delivery</i> (emailing on an upload
/// failure, etc.) is a separate feature that is not built yet — saving a toggle here does not start sending
/// alerts; it records the choice so the UI reflects saved state and a future delivery worker can honour it.</para>
/// </summary>
public sealed class AlertSettingsService(ISettingsStore settings)
{
    private const string KeyUploadFailures = "system:alerts:uploadFailures";
    private const string KeyQuotaWarnings = "system:alerts:quotaWarnings";
    private const string KeyOauthExpiry = "system:alerts:oauthExpiry";
    private const string KeyWeeklyDigest = "system:alerts:weeklyDigest";

    public async Task<AlertSettings> GetAsync(CancellationToken ct = default) => new(
        await GetBoolAsync(KeyUploadFailures, true, ct),
        await GetBoolAsync(KeyQuotaWarnings, true, ct),
        await GetBoolAsync(KeyOauthExpiry, true, ct),
        await GetBoolAsync(KeyWeeklyDigest, false, ct));

    /// <summary>Applies any non-null field, then returns the full saved state.</summary>
    public async Task<AlertSettings> UpdateAsync(
        bool? uploadFailures, bool? quotaWarnings, bool? oauthExpiry, bool? weeklyDigest,
        CancellationToken ct = default)
    {
        if (uploadFailures.HasValue)
        {
            await settings.SetAsync(KeyUploadFailures, Str(uploadFailures.Value), ct);
        }

        if (quotaWarnings.HasValue)
        {
            await settings.SetAsync(KeyQuotaWarnings, Str(quotaWarnings.Value), ct);
        }

        if (oauthExpiry.HasValue)
        {
            await settings.SetAsync(KeyOauthExpiry, Str(oauthExpiry.Value), ct);
        }

        if (weeklyDigest.HasValue)
        {
            await settings.SetAsync(KeyWeeklyDigest, Str(weeklyDigest.Value), ct);
        }

        return await GetAsync(ct);
    }

    private async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = await settings.GetAsync(key, ct);
        return raw is null ? fallback : raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string Str(bool value) => value ? "true" : "false";
}
