using System.Linq.Expressions;

using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Connections;
using Hookline.SharedKernel.Jobs;

using Microsoft.EntityFrameworkCore;

namespace Hookline.Modules.YouTubeComments.Tests;

internal static class TestDb
{
    public static YouTubeCommentsDbContext Create() =>
        new(new DbContextOptionsBuilder<YouTubeCommentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

/// <summary>In-memory <see cref="IJobScheduler"/> that records recurring registrations + removals.</summary>
internal sealed class FakeJobScheduler : IJobScheduler
{
    public Dictionary<string, string> Recurring { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> AddCalls { get; } = new();

    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> methodCall, string cron)
        where TJob : notnull
    {
        Recurring[id] = cron;
        AddCalls.Add(id);
    }

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) where TJob : notnull => Guid.NewGuid().ToString();

    public void RemoveRecurring(string id)
    {
        Recurring.Remove(id);
        Removed.Add(id);
    }

    public IReadOnlyList<string> ListRecurring() => Recurring.Keys.ToList();
}

/// <summary>In-memory <see cref="IYouTubeApiKeyConnections"/> backing the rotation provider tests.</summary>
internal sealed class FakeKeyConnections : IYouTubeApiKeyConnections
{
    private readonly List<(YouTubeApiKeySummary Summary, string Key)> _keys = new();

    public Guid Seed(string name, bool active, string key = "AIzaTESTKEYvalue9999")
    {
        var id = Guid.NewGuid();
        _keys.Add((new YouTubeApiKeySummary(id, name, "AIza…9999", active, DateTimeOffset.UtcNow), key));
        return id;
    }

    public Task<IReadOnlyList<YouTubeApiKeySummary>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<YouTubeApiKeySummary>>(_keys.Select(k => k.Summary).ToList());

    public Task<IReadOnlyList<YouTubeApiKeySummary>> ListActiveAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<YouTubeApiKeySummary>>(_keys.Where(k => k.Summary.IsActive).Select(k => k.Summary).ToList());

    public Task<string?> GetApiKeyAsync(Guid keyId, CancellationToken ct = default) =>
        Task.FromResult(_keys.Where(k => k.Summary.Id == keyId).Select(k => (string?)k.Key).FirstOrDefault());

    public Task<Guid> CreateAsync(string name, string apiKey, string keyHint, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _keys.Add((new YouTubeApiKeySummary(id, name, keyHint, true, DateTimeOffset.UtcNow), apiKey));
        return Task.FromResult(id);
    }

    public Task<bool> ToggleAsync(Guid keyId, bool isActive, CancellationToken ct = default)
    {
        var i = _keys.FindIndex(k => k.Summary.Id == keyId);
        if (i < 0) return Task.FromResult(false);
        _keys[i] = (_keys[i].Summary with { IsActive = isActive }, _keys[i].Key);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid keyId, CancellationToken ct = default) =>
        Task.FromResult(_keys.RemoveAll(k => k.Summary.Id == keyId) > 0);
}
