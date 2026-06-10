using System.Linq.Expressions;

using Hookline.Modules.YouTubeComments.Infrastructure;
using Hookline.SharedKernel.Audit;
using Hookline.SharedKernel.Common;
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

/// <summary>In-memory <see cref="ISlackConnections"/>; only the bits the module services read are real.</summary>
internal sealed class FakeSlackConnections : ISlackConnections
{
    private readonly List<SlackWorkspaceSummary> _workspaces = new();

    public Guid Seed(string teamName, bool active = true)
    {
        var id = Guid.NewGuid();
        _workspaces.Add(new SlackWorkspaceSummary(id, $"T-{id:N}", teamName, active));
        return id;
    }

    public Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SlackWorkspaceSummary>>(_workspaces.ToList());

    public Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult<string?>(_workspaces.Any(w => w.Id == workspaceId) ? "xoxb-test" : null);

    public Task<string?> GetBotTokenForTeamAsync(string teamId, CancellationToken ct = default) =>
        Task.FromResult<string?>("xoxb-test");

    public Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, CancellationToken ct = default) =>
        Task.FromResult(_workspaces.FirstOrDefault(w => w.TeamId == teamId));

    public Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default) =>
        Task.FromResult(Seed(write.TeamName));

    public Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult(_workspaces.RemoveAll(w => w.Id == workspaceId) > 0);
}

/// <summary>Records every entry the module writes through <see cref="ICommentsAudit"/> for assertions.</summary>
internal sealed class RecordingCommentsAudit : ICommentsAudit
{
    public readonly record struct Entry(
        string Level, string Category, string Message, string? EntityType, string? EntityId, string? Details);

    public List<Entry> Entries { get; } = new();

    public Task LogAsync(
        string level, string category, string message,
        string? entityType = null, string? entityId = null, string? actor = null, string? details = null,
        CancellationToken ct = default)
    {
        Entries.Add(new Entry(level, category, message, entityType, entityId, details));
        return Task.CompletedTask;
    }
}

/// <summary>Records what the shared <see cref="IAuditLog"/> would persist (so the folded-level prefix is visible).</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public readonly record struct Row(string Action, string? Module, string? EntityType, string? EntityId, string? Detail);

    public List<Row> Rows { get; } = new();

    public Task WriteAsync(
        string action, string? module = null, string? entityType = null, string? entityId = null,
        string? detail = null, CancellationToken ct = default)
    {
        Rows.Add(new Row(action, module, entityType, entityId, detail));
        return Task.CompletedTask;
    }
}

/// <summary>Stub <see cref="IAuditLogReader"/> returning a fixed error count; the dashboard quota math doesn't read audit.</summary>
internal sealed class StubAuditLogReader(int errorCount = 0) : IAuditLogReader
{
    public Task<PagedResult<AuditLogRecord>> ListAsync(string? module, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<AuditLogRecord>.Empty(page, pageSize));

    public Task<int> CountSinceAsync(string? module, DateTimeOffset since, string? detailPrefix = null, CancellationToken ct = default) =>
        Task.FromResult(errorCount);
}
