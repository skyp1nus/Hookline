using Hookline.Infrastructure.Persistence;

using Hookline.SharedKernel.Settings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hookline.Infrastructure.Settings;

/// <summary>Settings resolution: database override → environment/config → caller default.</summary>
public sealed class SettingsStore(SharedDbContext db, IConfiguration config) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var dbValue = await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return dbValue ?? config[key.Replace(':', '_')] ?? config[key];
    }

    public async Task<string> GetAsync(string key, string fallback, CancellationToken ct = default) =>
        await GetAsync(key, ct) ?? fallback;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
