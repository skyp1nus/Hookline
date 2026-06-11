using System.Linq.Expressions;

using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Caching;
using Hookline.SharedKernel.Jobs;
using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>Shared in-memory fakes for the P0 toggle + Danger-Zone maintenance tests.</summary>
internal static class TestDb
{
    public static YouTubeUploadsDbContext Create() =>
        new(new DbContextOptionsBuilder<YouTubeUploadsDbContext>()
                .UseInMemoryDatabase($"uploads-{Guid.NewGuid()}")
                .Options,
            new PassthroughProtector());
}

internal sealed class PassthroughProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
    public bool TryUnprotect(string ciphertext, out string plaintext) { plaintext = ciphertext; return true; }
}

/// <summary>Records every entry written through the shared <see cref="IAuditLog"/>.</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public readonly record struct Row(string Action, string? Module, string? EntityType, string? EntityId, string? Detail);

    public List<Row> Rows { get; } = new();

    public Task WriteAsync(string action, string? module = null, string? entityType = null,
        string? entityId = null, string? detail = null, CancellationToken ct = default)
    {
        Rows.Add(new Row(action, module, entityType, entityId, detail));
        return Task.CompletedTask;
    }
}

/// <summary>Records the prefixes a reset asked to purge from Redis.</summary>
internal sealed class RecordingCachePurge : ICachePurge
{
    public List<string> Prefixes { get; } = new();

    public Task<long> PurgeByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        Prefixes.Add(prefix);
        return Task.FromResult(0L);
    }
}

/// <summary>Spy <see cref="IJobScheduler"/> that counts one-off enqueues (the upload pipeline trigger).</summary>
internal sealed class SpyJobScheduler : IJobScheduler
{
    public int EnqueueCount { get; private set; }

    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> methodCall, string cron) where TJob : notnull { }
    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) where TJob : notnull { EnqueueCount++; return Guid.NewGuid().ToString(); }
    public void RemoveRecurring(string id) { }
    public IReadOnlyList<string> ListRecurring() => [];
}

/// <summary>Minimal <see cref="IJobService"/>: only the two pre-ingest dedup checks are real (both "not seen").</summary>
internal sealed class NotSeenJobService : IJobService
{
    public Task<bool> ExistsForEventAsync(string slackEventId, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> ExistsForChannelMessageAsync(string channelId, string ts, CancellationToken ct = default) => Task.FromResult(false);

    public Task<UploadJob> CreateAsync(NewJob input, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UploadJob?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task TransitionAsync(UploadJob job, JobState to, string? note = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveAsync(UploadJob job, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<StatusSnapshot> GetStatusSnapshotAsync(string slackChannelId, int recentCount = 5, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UploadJob>> GetHistoryAsync(int take = 50, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(IReadOnlyList<JobHistoryItem> Items, int Total)> GetHistoryPagedAsync(JobHistoryFilter filter, int page, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<JobFilterOptions> GetJobFilterOptionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(int UploadsToday, int UploadsLast24h, int ErrorsLast24h)> GetDashboardCountsAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
