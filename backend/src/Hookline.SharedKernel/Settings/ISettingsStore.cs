namespace Hookline.SharedKernel.Settings;

/// <summary>
/// Hub-wide key/value settings. The Infrastructure implementation resolves in
/// order: database override → environment → default.
/// </summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string> GetAsync(string key, string fallback, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
}
