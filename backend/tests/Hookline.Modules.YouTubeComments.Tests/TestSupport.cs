using System.Linq.Expressions;
using System.Net;

using Google;
using Google.Apis.Requests;

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

/// <summary>In-memory <see cref="ISlackConnections"/>; only the bits the module services read are real.</summary>
internal sealed class FakeSlackConnections : ISlackConnections
{
    private readonly List<SlackWorkspaceSummary> _workspaces = new();

    public Guid Seed(string teamName, bool active = true, string app = "youtube-comments")
    {
        var id = Guid.NewGuid();
        _workspaces.Add(new SlackWorkspaceSummary(id, $"T-{id:N}", teamName, app, active));
        return id;
    }

    public Task<IReadOnlyList<SlackWorkspaceSummary>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SlackWorkspaceSummary>>(_workspaces.ToList());

    public Task<string?> GetBotTokenAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult<string?>(_workspaces.Any(w => w.Id == workspaceId) ? "xoxb-test" : null);

    public Task<string?> GetBotTokenForTeamAsync(string teamId, string app, CancellationToken ct = default) =>
        Task.FromResult<string?>("xoxb-test");

    public Task<SlackWorkspaceSummary?> GetByTeamAsync(string teamId, string app, CancellationToken ct = default) =>
        Task.FromResult(_workspaces.FirstOrDefault(w => w.TeamId == teamId && w.App == app));

    public Task<Guid> UpsertWorkspaceAsync(SlackWorkspaceWrite write, CancellationToken ct = default) =>
        Task.FromResult(Seed(write.TeamName, app: write.App));

    public Task<bool> DeactivateAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult(_workspaces.RemoveAll(w => w.Id == workspaceId) > 0);
}

/// <summary>Records every entry the module writes through <see cref="ICommentsAudit"/> for assertions.</summary>
internal sealed class RecordingCommentsAudit : ICommentsAudit
{
    public readonly record struct Entry(
        string Level, string Category, string Message, string? EntityType, string? EntityId, string? Actor, string? Details);

    public List<Entry> Entries { get; } = new();

    public Task LogAsync(
        string level, string category, string message,
        string? entityType = null, string? entityId = null, string? actor = null, string? details = null,
        CancellationToken ct = default)
    {
        Entries.Add(new Entry(level, category, message, entityType, entityId, actor, details));
        return Task.CompletedTask;
    }
}

/// <summary>Records what the shared <see cref="IAuditLog"/> would persist (so the folded-level prefix is visible).</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public readonly record struct Row(string Action, string? Module, string? EntityType, string? EntityId, string? Detail, string? Actor);

    public List<Row> Rows { get; } = new();

    public Task WriteAsync(
        string action, string? module = null, string? entityType = null, string? entityId = null,
        string? detail = null, string? actor = null, CancellationToken ct = default)
    {
        Rows.Add(new Row(action, module, entityType, entityId, detail, actor));
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

/// <summary>In-memory <see cref="IGoogleChannelCredentials"/> — seeds a force-ssl credential per channel
/// id; unseeded channels resolve to null (the honest "no connected account" path used by both monitoring
/// and moderation).</summary>
internal sealed class FakeGoogleChannelCredentials : IGoogleChannelCredentials
{
    private readonly Dictionary<string, GoogleChannelCredential> _byChannel = new(StringComparer.Ordinal);

    public void Seed(string youtubeChannelId, string accessToken = "ya29.test-access") =>
        _byChannel[youtubeChannelId] = new GoogleChannelCredential(
            Guid.NewGuid(), youtubeChannelId, accessToken, DateTimeOffset.UtcNow.AddHours(1),
            new[] { GoogleScopes.YouTubeForceSsl });

    public Task<GoogleChannelCredential?> GetChannelCredentialAsync(string youtubeChannelId, CancellationToken ct = default) =>
        Task.FromResult(_byChannel.GetValueOrDefault(youtubeChannelId));
}

/// <summary>In-memory <see cref="IYouTubeModerationClient"/>: records calls and can be told to throw a
/// specific <see cref="GoogleApiException"/> (already-gone / forbidden / quota).</summary>
internal sealed class FakeYouTubeModerationClient : IYouTubeModerationClient
{
    public int Calls { get; private set; }
    public string? LastAccessToken { get; private set; }
    public string? LastCommentId { get; private set; }
    public Func<Exception>? Throw { get; set; }

    public Task RejectAsync(string accessToken, string commentId, CancellationToken ct = default)
    {
        Calls++;
        LastAccessToken = accessToken;
        LastCommentId = commentId;
        if (Throw is { } make)
            throw make();
        return Task.CompletedTask;
    }

    /// <summary>Builds a YouTube <see cref="GoogleApiException"/> with a status + optional error reason.</summary>
    public static GoogleApiException Api(HttpStatusCode status, string? reason = null) =>
        new("youtube", reason ?? status.ToString())
        {
            HttpStatusCode = status,
            Error = new RequestError
            {
                Errors = reason is null ? new List<SingleError>() : new List<SingleError> { new() { Reason = reason } },
            },
        };
}

/// <summary>In-memory <see cref="IGoogleConnections"/> for moderation-capability tests (scope snapshot).</summary>
internal sealed class FakeGoogleConnections : IGoogleConnections
{
    private readonly List<GoogleAccountDetail> _accounts = new();

    public Guid Seed(string? channelId, string scopes, bool active = true)
    {
        var id = Guid.NewGuid();
        _accounts.Add(new GoogleAccountDetail(id, channelId, "Channel", null, null, scopes, active));
        return id;
    }

    public Task<IReadOnlyList<GoogleAccountSummary>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GoogleAccountSummary>>(
            _accounts.Select(a => new GoogleAccountSummary(a.Id, a.ChannelTitle, a.IsActive)).ToList());

    public Task<GoogleAccountDetail?> GetAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<GoogleAccountDetail?>(_accounts.FirstOrDefault(a => a.Id == accountId));

    public Task<GoogleAccessCredential?> GetCredentialAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<GoogleAccessCredential?>(null);

    public Task<string?> GetRefreshTokenAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<string?>("refresh-token");

    public Task<Guid> CreateAccountAsync(GoogleAccountWrite write, CancellationToken ct = default) =>
        Task.FromResult(Seed(write.ChannelId, write.Scopes));

    public Task<bool> UpdateConsentAsync(Guid accountId, string refreshToken, string scopes,
        string? channelTitle = null, string? accountEmail = null, string? avatarUrl = null, CancellationToken ct = default)
    {
        var i = _accounts.FindIndex(a => a.Id == accountId);
        if (i < 0) return Task.FromResult(false);
        _accounts[i] = _accounts[i] with { Scopes = scopes, IsActive = true };
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_accounts.RemoveAll(a => a.Id == accountId) > 0);
}
