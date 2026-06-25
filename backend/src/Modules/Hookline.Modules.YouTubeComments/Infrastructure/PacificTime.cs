using Hookline.SharedKernel.Common;

namespace Hookline.Modules.YouTubeComments.Infrastructure;

/// <summary>
/// Module-local facade over <see cref="SharedKernel.Common.PacificTime"/> — preserves the existing
/// call-sites while the implementation lives in the shared kernel.
/// </summary>
internal static class PacificTime
{
    public static DateOnly Today() => SharedKernel.Common.PacificTime.Today();
    public static string TodayKey() => SharedKernel.Common.PacificTime.TodayKey();
    public static DateTimeOffset StartOfToday() => SharedKernel.Common.PacificTime.StartOfToday();
    public static TimeSpan UntilMidnight() => SharedKernel.Common.PacificTime.UntilMidnight();
}
