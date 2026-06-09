using System.Linq.Expressions;

namespace Hookline.SharedKernel.Jobs;

/// <summary>
/// A thin abstraction over the background-job engine (Hangfire in Infrastructure).
/// Modules schedule recurring work and enqueue one-off work without referencing
/// the engine directly, which keeps "extract this module later" realistic.
/// </summary>
public interface IJobScheduler
{
    /// <summary>Create or update a recurring job. The method call is resolved from DI at run time.</summary>
    void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> methodCall, string cron)
        where TJob : notnull;

    /// <summary>Enqueue a one-off job. Returns the engine's job id.</summary>
    string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall)
        where TJob : notnull;

    /// <summary>Remove a recurring job if it exists.</summary>
    void RemoveRecurring(string id);
}
