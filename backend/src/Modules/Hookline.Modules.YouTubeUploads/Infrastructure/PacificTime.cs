using Hookline.SharedKernel.Common;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// Module-local facade over <see cref="SharedKernel.Common.PacificTime"/> — preserves the existing
/// call-sites while the implementation lives in the shared kernel.
/// </summary>
internal static class PacificTime
{
    public static string TodayKey() => SharedKernel.Common.PacificTime.TodayKey();
    public static TimeSpan UntilMidnight() => SharedKernel.Common.PacificTime.UntilMidnight();
}
