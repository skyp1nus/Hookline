using Hookline.SharedKernel.Connections;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>Read model for a stored YouTube API key. Never exposes the secret value, only <see cref="KeyHint"/>.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string KeyHint,
    int DailyQuotaLimit,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int TodayUnitsUsed,
    int RemainingQuota);

/// <summary>Request payload to register a new API key.</summary>
public sealed record CreateApiKeyRequest(string Name, string ApiKey);

/// <summary>Thrown when an API key request is invalid or YouTube rejects the key (-> 400).</summary>
public sealed class ApiKeyValidationException(string message) : Exception(message);

/// <summary>
/// CRUD + validation for YouTube API keys. The key identity/secret lives in the shared Connections
/// store (<see cref="IYouTubeApiKeyConnections"/>); this decorates each key with today's
/// Pacific-Time quota usage (from the module-local <c>quota_usage</c>) so the UI can show consumption.
/// A uniform daily unit limit comes from config (the shared key record has no per-key limit).
/// </summary>
public sealed class ApiKeyService(
    YouTubeCommentsDbContext db,
    IYouTubeClient youtube,
    IYouTubeApiKeyConnections keys,
    ICommentsAudit audit,
    IOptions<YouTubeCommentsOptions> options)
{
    private readonly int _dailyLimit = options.Value.DailyQuotaUnits;

    /// <summary>Lists every stored key with today's quota usage and remaining quota.</summary>
    public async Task<ApiKeyDto[]> ListAsync(CancellationToken ct = default)
    {
        var today = PacificTime.Today();
        var all = await keys.ListAsync(ct);
        var ids = all.Select(k => k.Id).ToArray();

        var usageToday = await db.QuotaUsages
            .AsNoTracking()
            .Where(q => q.UsageDate == today && ids.Contains(q.ApiKeyId))
            .ToDictionaryAsync(q => q.ApiKeyId, q => q.UnitsUsed, ct);

        return all.Select(k => ToDto(k, usageToday.GetValueOrDefault(k.Id))).ToArray();
    }

    /// <summary>
    /// Validates the request and the key against the YouTube API, then persists it to the shared store.
    /// Throws <see cref="ApiKeyValidationException"/> for empty fields or a key YouTube rejects.
    /// </summary>
    public async Task<ApiKeyDto> CreateAsync(CreateApiKeyRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        var apiKey = request.ApiKey?.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ApiKeyValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApiKeyValidationException("API key is required.");

        var (ok, error) = await youtube.ValidateKeyAsync(apiKey, ct);
        if (!ok)
            throw new ApiKeyValidationException(error ?? "The API key could not be validated.");

        var hint = Mask(apiKey);
        var id = await keys.CreateAsync(name, apiKey, hint, ct);

        await audit.LogAsync("Information", "ApiKey",
            $"Added YouTube API key '{name}' ({hint})", "YouTubeApiKey", id.ToString(), ct: ct);

        // A freshly created key has no usage yet for today.
        return new ApiKeyDto(id, name, hint, _dailyLimit, IsActive: true, DateTimeOffset.UtcNow, TodayUnitsUsed: 0, RemainingQuota: _dailyLimit);
    }

    /// <summary>Deletes the key if present (also prunes its quota rows via the disconnect event). Returns <c>false</c> when no row matched.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var summary = (await keys.ListAsync(ct)).FirstOrDefault(k => k.Id == id);
        if (summary is null)
            return false;

        if (!await keys.DeleteAsync(id, ct))
            return false;

        await audit.LogAsync("Information", "ApiKey",
            $"Deleted YouTube API key '{summary.Name}'", "YouTubeApiKey", id.ToString(), ct: ct);
        return true;
    }

    /// <summary>Flips the key's active flag. Returns the updated DTO or <c>null</c> when not found.</summary>
    public async Task<ApiKeyDto?> ToggleAsync(Guid id, CancellationToken ct = default)
    {
        var summary = (await keys.ListAsync(ct)).FirstOrDefault(k => k.Id == id);
        if (summary is null)
            return null;

        var newActive = !summary.IsActive;
        await keys.ToggleAsync(id, newActive, ct);

        await audit.LogAsync("Information", "ApiKey",
            $"{(newActive ? "Enabled" : "Disabled")} YouTube API key '{summary.Name}'",
            "YouTubeApiKey", id.ToString(), ct: ct);

        var today = PacificTime.Today();
        var unitsUsed = await db.QuotaUsages
            .AsNoTracking()
            .Where(q => q.ApiKeyId == id && q.UsageDate == today)
            .Select(q => (int?)q.UnitsUsed)
            .FirstOrDefaultAsync(ct) ?? 0;

        return ToDto(summary with { IsActive = newActive }, unitsUsed);
    }

    private ApiKeyDto ToDto(YouTubeApiKeySummary k, int todayUnitsUsed) =>
        new(k.Id, k.Name, k.KeyHint, _dailyLimit, k.IsActive, k.CreatedAt, todayUnitsUsed, Math.Max(0, _dailyLimit - todayUnitsUsed));

    /// <summary>
    /// Masks a secret for display: first 4 + ellipsis + last 4. For keys of length &lt;= 8 the value
    /// is fully hidden and only the length is revealed (e.g. <c>•••• (6)</c>).
    /// </summary>
    internal static string Mask(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return string.Empty;

        if (apiKey.Length <= 8)
            return $"•••• ({apiKey.Length})";

        return $"{apiKey[..4]}…{apiKey[^4..]}";
    }
}
