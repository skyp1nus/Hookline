using Hookline.Modules.YouTubeUploads;
using Hookline.Modules.YouTubeUploads.Infrastructure;
using Hookline.Modules.YouTubeUploads.Jobs;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// Upload capacity is gated by the per-project <c>videos.insert</c> daily bucket (Google default
/// 100/project/day), keyed by PROJECT (Cloud project) — NOT by the separate ~10k unit pool used by
/// other endpoints. Upload rotation across a channel's projects picks the project with the most
/// remaining uploads first, skipping exhausted ones; when ALL are exhausted the job is blocked
/// (no project chosen) rather than uploaded — preserving the no-duplicate-upload guarantee.
/// </summary>
public class QuotaRotationTests
{
    [Fact]
    public void UploadCountKeyScopedByProjectAndDate()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        var keyA = RedisKeys.UploadCount(projectA, "2026-06-05");

        Assert.StartsWith("ytu:", keyA);                                         // YouTubeUploads prefix
        Assert.Contains(projectA.ToString(), keyA);
        Assert.Contains("2026-06-05", keyA);
        Assert.NotEqual(keyA, RedisKeys.UploadCount(projectB, "2026-06-05"));   // different project ⇒ different counter
        Assert.NotEqual(keyA, RedisKeys.UploadCount(projectA, "2026-06-06"));   // different PT day ⇒ different counter
        Assert.NotEqual(keyA, RedisKeys.Quota(projectA, "2026-06-05"));         // upload bucket ≠ unit pool key
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(95, 100, 5, 100)]
    public void QuotaStatusUploadMath(int usedUploads, int uploadLimit, int expectedRemaining, int expectedTotal)
    {
        var status = new QuotaStatus(usedUploads, uploadLimit, UsedUnits: 0, CapUnits: 10000);
        Assert.Equal(expectedRemaining, status.RemainingUploads);
        Assert.Equal(expectedTotal, status.TotalUploads);
        Assert.Equal(10000, status.RemainingUnits); // unit pool is independent of the upload bucket
    }

    [Fact]
    public void UnitMeterIsIndependentOfUploads()
    {
        var status = new QuotaStatus(UsedUploads: 3, UploadLimit: 100, UsedUnits: 2500, CapUnits: 10000);
        Assert.Equal(97, status.RemainingUploads);
        Assert.Equal(7500, status.RemainingUnits);
    }

    [Fact]
    public async Task GetStatusNullProjectReportsZeroCap()
    {
        // An unbound account (null project) must not look like it has a full daily quota available.
        var quota = new QuotaService(null!, Options.Create(new YouTubeUploadsOptions()));
        var status = await quota.GetStatusAsync(null);
        Assert.Equal(0, status.UploadLimit);
        Assert.Equal(0, status.CapUnits);
        Assert.Equal(0, status.RemainingUploads);
        Assert.Equal(0, status.TotalUploads);
    }

    [Fact]
    public async Task RotationPicksMostRemainingFirst()
    {
        var projectLow = Guid.NewGuid();   // less headroom
        var projectHigh = Guid.NewGuid();  // more headroom
        // projectLow appears FIRST in the list but has used more — selection must still prefer projectHigh.
        var candidates = new[] { Creds(projectLow), Creds(projectHigh) };
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [projectLow] = 99, [projectHigh] = 0 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.NotNull(chosen);
        Assert.Equal(projectHigh, chosen!.ProjectId);
        Assert.Equal(1, quota.Used(projectHigh));   // one upload reserved on the chosen project only
        Assert.Equal(99, quota.Used(projectLow));   // untouched
    }

    [Fact]
    public async Task RotationSkipsExhaustedProject()
    {
        var exhausted = Guid.NewGuid();
        var open = Guid.NewGuid();
        var candidates = new[] { Creds(exhausted), Creds(open) };
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [exhausted] = 100, [open] = 50 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.NotNull(chosen);
        Assert.Equal(open, chosen!.ProjectId);
    }

    [Fact]
    public async Task RotationNullWhenAllExhausted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var candidates = new[] { Creds(a), Creds(b) };
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [a] = 100, [b] = 100 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.Null(chosen); // → the handler marks the job Blocked, never uploads
    }

    [Fact]
    public async Task StatusAggregatesPoolQuotaAcrossProjects()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var quota = new FakeQuota(limit: 100,                       // 100 uploads per project ⇒ 200 total
            used: new Dictionary<Guid, int> { [projectA] = 96, [projectB] = 0 }); // A spent 96 ⇒ 4 left

        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(new[] { projectA, projectB }, quota);

        Assert.Equal(104, remaining); // 4 (A) + 100 (B)
        Assert.Equal(200, total);     // 100 + 100
    }

    [Fact]
    public async Task StatusAggregateEmptyPoolIsZero()
    {
        var quota = new FakeQuota(limit: 100);
        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(Array.Empty<Guid>(), quota);
        Assert.Equal(0, remaining);
        Assert.Equal(0, total);
    }

    private static GoogleUploadCreds Creds(Guid projectId) =>
        new(AccountId: Guid.NewGuid(), ProjectId: projectId, ClientId: $"cid-{projectId}", ClientSecret: "secret", RefreshToken: "rt");

    /// <summary>In-memory stand-in for the Redis-backed QuotaService (per-project daily upload-call counter).</summary>
    private sealed class FakeQuota : IQuotaService
    {
        private readonly Dictionary<Guid, int> _used; // videos.insert calls used per project today
        private readonly int _limit;

        public FakeQuota(int limit, Dictionary<Guid, int>? used = null)
        {
            _limit = limit;
            _used = used ?? new Dictionary<Guid, int>();
        }

        public int Used(Guid projectId) => _used.GetValueOrDefault(projectId);

        public Task<QuotaStatus> GetStatusAsync(Guid? projectId)
        {
            var used = projectId is null ? 0 : _used.GetValueOrDefault(projectId.Value);
            var limit = projectId is null ? 0 : _limit;
            return Task.FromResult(new QuotaStatus(used, limit, UsedUnits: 0, CapUnits: 0));
        }

        public Task<bool> TryReserveUploadAsync(Guid projectId)
        {
            var used = _used.GetValueOrDefault(projectId);
            if (used + 1 > _limit) return Task.FromResult(false);
            _used[projectId] = used + 1;
            return Task.FromResult(true);
        }

        public Task ReleaseUploadAsync(Guid projectId)
        {
            _used[projectId] = Math.Max(0, _used.GetValueOrDefault(projectId) - 1);
            return Task.CompletedTask;
        }

        public Task ChargeUnitsAsync(Guid projectId, int units) => Task.CompletedTask;
    }
}
