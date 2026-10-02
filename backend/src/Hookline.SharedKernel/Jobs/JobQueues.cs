namespace Hookline.SharedKernel.Jobs;

/// <summary>Background-job queue names. A module opts a job into a queue with Hangfire's <c>[Queue]</c>.</summary>
public static class JobQueues
{
    public const string Default = "default";

    /// <summary>Minutes-long transfers. Served by its own capped worker pool so they never starve short jobs.</summary>
    public const string LongRunning = "long-running";
}
