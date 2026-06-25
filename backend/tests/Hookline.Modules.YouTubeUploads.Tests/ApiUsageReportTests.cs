using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

public sealed class ApiUsageReportTests
{
    private static readonly Guid Client1 = Guid.NewGuid();
    private static readonly Guid Client2 = Guid.NewGuid();

    [Fact]
    public void Build_returns_three_groups()
    {
        var report = ApiUsageReport.Build("2025-01-01", [], [], 100);

        Assert.Equal("2025-01-01", report.Date);
        Assert.Equal(3, report.Groups.Count);
        Assert.Equal("YouTube", report.Groups[0].Group);
        Assert.Equal("Drive", report.Groups[1].Group);
        Assert.Equal("Slack", report.Groups[2].Group);
    }

    [Fact]
    public void YouTube_metrics_reflect_client_quotas()
    {
        var clients = new[]
        {
            new ApiUsageReport.ClientQuota(Client1, "Project A",
                new QuotaStatus(UsedUploads: 3, UploadLimit: 10, UsedUnits: 50, CapUnits: 1000)),
        };

        var report = ApiUsageReport.Build("2025-01-01", clients, [], 100);
        var yt = report.Groups[0];

        Assert.Equal(2, yt.Metrics.Count);
        var uploads = yt.Metrics[0];
        Assert.Equal(ApiMetrics.YouTubeUpload, uploads.Key);
        Assert.Equal(3, uploads.Used);
        Assert.Equal(10, uploads.Limit);

        var units = yt.Metrics[1];
        Assert.Equal(ApiMetrics.YouTubeUnits, units.Key);
        Assert.Equal(50, units.Used);
        Assert.Equal(1000, units.Limit);
    }

    [Fact]
    public void Drive_metrics_reflect_usage_entries()
    {
        var usage = new[]
        {
            new UsageEntry(Client1.ToString(), ApiMetrics.DriveQueries, 42),
            new UsageEntry(Client1.ToString(), ApiMetrics.DriveBytes, 1024),
        };

        var report = ApiUsageReport.Build("2025-01-01", [], usage, 500);
        var drive = report.Groups[1];

        Assert.Equal(2, drive.Metrics.Count);
        var queries = drive.Metrics[0];
        Assert.Equal(42, queries.Used);
        Assert.Equal(500, queries.Limit);

        var bytes = drive.Metrics[1];
        Assert.Equal(1024, bytes.Used);
        Assert.Null(bytes.Limit); // bytes have no limit
    }

    [Fact]
    public void Slack_metrics_sorted_descending_by_value()
    {
        var usage = new[]
        {
            new UsageEntry(ApiMetrics.SlackScope, "slack.chat.postMessage", 10),
            new UsageEntry(ApiMetrics.SlackScope, "slack.conversations.history", 50),
        };

        var report = ApiUsageReport.Build("2025-01-01", [], usage, 100);
        var slack = report.Groups[2];

        Assert.Equal(2, slack.Metrics.Count);
        Assert.Equal("slack.conversations.history", slack.Metrics[0].Key);
        Assert.Equal(50, slack.Metrics[0].Used);
        Assert.Equal("slack.chat.postMessage", slack.Metrics[1].Key);
        Assert.Equal(10, slack.Metrics[1].Used);
    }

    [Fact]
    public void Slack_label_strips_prefix()
    {
        var usage = new[]
        {
            new UsageEntry(ApiMetrics.SlackScope, "slack.chat.update", 5),
        };

        var report = ApiUsageReport.Build("2025-01-01", [], usage, 100);
        var metric = report.Groups[2].Metrics[0];

        Assert.Equal("chat.update", metric.Label);
    }

    [Fact]
    public void Multiple_clients_have_summed_totals()
    {
        var clients = new[]
        {
            new ApiUsageReport.ClientQuota(Client1, "A",
                new QuotaStatus(UsedUploads: 2, UploadLimit: 5, UsedUnits: 100, CapUnits: 500)),
            new ApiUsageReport.ClientQuota(Client2, "B",
                new QuotaStatus(UsedUploads: 3, UploadLimit: 10, UsedUnits: 200, CapUnits: 1000)),
        };

        var report = ApiUsageReport.Build("2025-01-01", clients, [], 100);
        var yt = report.Groups[0];

        Assert.Equal(5, yt.Metrics[0].Used); // 2+3 uploads
        Assert.Equal(15, yt.Metrics[0].Limit); // 5+10 upload limit
        Assert.Equal(300, yt.Metrics[1].Used); // 100+200 units
        Assert.Equal(1500, yt.Metrics[1].Limit); // 500+1000 cap
    }

    [Fact]
    public void Client_labels_are_used_for_scope_names()
    {
        var clients = new[]
        {
            new ApiUsageReport.ClientQuota(Client1, "My Project",
                new QuotaStatus(UsedUploads: 1, UploadLimit: 5, UsedUnits: 10, CapUnits: 100)),
        };

        var usage = new[]
        {
            new UsageEntry(Client1.ToString(), ApiMetrics.DriveQueries, 7),
        };

        var report = ApiUsageReport.Build("2025-01-01", clients, usage, 100);
        var driveQueries = report.Groups[1].Metrics[0];

        Assert.Equal("My Project", driveQueries.PerScope[0].Scope);
    }
}
