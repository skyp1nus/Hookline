using Hookline.Modules.YouTubeComments;
using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// Severity is folded into the shared audit entry's Detail as a leading <c>[Level]</c> marker (there is no
/// separate Level column by design) and queried back via <c>CountSinceAsync(detailPrefix)</c>. These tests
/// pin that contract so the writer's marker and the dashboard's error-count query can't drift apart, and
/// lock the DailyQuotaUnits startup validation that protects the quota math.
/// </summary>
public class AuditAndOptionsTests
{
    [Theory]
    [InlineData("Error", "[Error]")]
    [InlineData("Warning", "[Warning]")]
    [InlineData("Information", "[Information]")]
    public void DetailPrefix_wraps_the_level_in_brackets(string level, string expected) =>
        Assert.Equal(expected, CommentsAudit.DetailPrefix(level));

    [Fact]
    public void AuditLevel_constants_are_the_expected_stable_values()
    {
        Assert.Equal("Information", AuditLevel.Information);
        Assert.Equal("Warning", AuditLevel.Warning);
        Assert.Equal("Error", AuditLevel.Error);
    }

    [Fact]
    public async Task LogAsync_folds_the_level_into_the_detail_prefix_and_tags_the_module()
    {
        var log = new RecordingAuditLog();
        var audit = new CommentsAudit(log);

        await audit.LogAsync(AuditLevel.Error, "Delivery", "Boom", "ChannelMapping", "abc");

        var row = Assert.Single(log.Rows);
        Assert.Equal("[Error] Boom", row.Detail);
        Assert.Equal(CommentsAudit.ModuleName, row.Module);
        Assert.Equal("Delivery", row.Action);          // category → action
        Assert.Equal("ChannelMapping", row.EntityType);
        Assert.Equal("abc", row.EntityId);
    }

    [Fact]
    public async Task LogAsync_appends_details_after_the_message()
    {
        var log = new RecordingAuditLog();
        await new CommentsAudit(log).LogAsync(AuditLevel.Warning, "Quota", "Exhausted", details: "{\"key\":1}");

        Assert.Equal("[Warning] Exhausted {\"key\":1}", Assert.Single(log.Rows).Detail);
    }

    [Fact]
    public async Task An_error_entry_matches_the_prefix_the_dashboard_counts_by()
    {
        // The dashboard counts errors with CommentsAudit.DetailPrefix(AuditLevel.Error); an Error entry's
        // Detail must StartsWith exactly that, which is how CountSinceAsync(detailPrefix) finds it.
        var log = new RecordingAuditLog();
        await new CommentsAudit(log).LogAsync(AuditLevel.Error, "Polling", "Poll failed");

        var detail = Assert.Single(log.Rows).Detail;
        Assert.StartsWith(CommentsAudit.DetailPrefix(AuditLevel.Error), detail);
    }

    // ── DailyQuotaUnits validation ──

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(YouTubeCommentsOptions.MaxDailyQuotaUnits + 1)]
    public void Validator_fails_an_out_of_range_daily_quota(int units)
    {
        var result = new YouTubeCommentsOptionsValidator()
            .Validate(null, new YouTubeCommentsOptions { DailyQuotaUnits = units });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(YouTubeCommentsOptions.MinDailyQuotaUnits)]
    [InlineData(10000)]
    [InlineData(YouTubeCommentsOptions.MaxDailyQuotaUnits)]
    public void Validator_accepts_an_in_range_daily_quota(int units)
    {
        var result = new YouTubeCommentsOptionsValidator()
            .Validate(null, new YouTubeCommentsOptions { DailyQuotaUnits = units });
        Assert.True(result.Succeeded);
    }
}
