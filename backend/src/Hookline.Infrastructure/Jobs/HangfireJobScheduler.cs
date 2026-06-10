using System.Linq.Expressions;

using Hangfire;
using Hangfire.Storage;

using Hookline.SharedKernel.Jobs;

namespace Hookline.Infrastructure.Jobs;

/// <summary>Hangfire-backed <see cref="IJobScheduler"/>. Jobs are resolved from DI by type.</summary>
public sealed class HangfireJobScheduler : IJobScheduler
{
    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> methodCall, string cron)
        where TJob : notnull =>
        RecurringJob.AddOrUpdate(id, methodCall, cron);

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall)
        where TJob : notnull =>
        BackgroundJob.Enqueue(methodCall);

    public void RemoveRecurring(string id) => RecurringJob.RemoveIfExists(id);

    public IReadOnlyList<string> ListRecurring()
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs().Select(j => j.Id).ToList();
    }
}
