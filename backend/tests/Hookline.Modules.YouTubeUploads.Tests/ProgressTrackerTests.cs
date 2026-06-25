using Hookline.Modules.YouTubeUploads.Domain;
using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

public sealed class ProgressTrackerTests
{
    private readonly ProgressTracker _tracker = new();

    [Fact]
    public void Get_returns_null_for_unknown_job()
    {
        Assert.Null(_tracker.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Set_then_Get_returns_progress()
    {
        var id = Guid.NewGuid();
        var progress = new JobProgress(JobState.Downloading, 50, 100, "phase1");

        _tracker.Set(id, progress);

        Assert.Equal(progress, _tracker.Get(id));
    }

    [Fact]
    public void Set_overwrites_previous()
    {
        var id = Guid.NewGuid();
        _tracker.Set(id, new JobProgress(JobState.Downloading, 10, 100, null));
        var updated = new JobProgress(JobState.Uploading, 80, 100, "phase2");

        _tracker.Set(id, updated);

        Assert.Equal(updated, _tracker.Get(id));
    }

    [Fact]
    public void Remove_clears_entry()
    {
        var id = Guid.NewGuid();
        _tracker.Set(id, new JobProgress(JobState.Downloading, 50, 100, null));

        _tracker.Remove(id);

        Assert.Null(_tracker.Get(id));
    }

    [Fact]
    public void Remove_is_idempotent()
    {
        _tracker.Remove(Guid.NewGuid()); // no throw
    }

    [Theory]
    [InlineData(50, 100, 50)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 100, 100)]
    [InlineData(200, 100, 100)] // clamped to 100
    [InlineData(0, 0, 0)]       // BytesTotal = 0 → 0
    public void Percent_calculates_correctly(long transferred, long total, int expected)
    {
        var progress = new JobProgress(JobState.Uploading, transferred, total, null);

        Assert.Equal(expected, progress.Percent);
    }

    [Fact]
    public void Percent_never_goes_negative()
    {
        var progress = new JobProgress(JobState.Uploading, -10, 100, null);

        Assert.Equal(0, progress.Percent);
    }
}
