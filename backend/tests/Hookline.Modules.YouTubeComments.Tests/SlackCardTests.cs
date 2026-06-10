using System.Net;
using System.Text.Json;

using Hookline.Modules.YouTubeComments.Infrastructure;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>
/// The Slack post is a Block Kit card (context header + block-quoted body + a primary button that
/// deep-links to the comment via YouTube's <c>lc</c> permalink — <c>commentId</c> for a top-level
/// comment, <c>parentId.replyId</c> for a reply). A permanently-gone channel is classified distinctly
/// from a transient failure so the pipeline can deactivate vs. retry.
/// </summary>
public class SlackCardTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastBody;
        public string Body = "{\"ok\":true,\"ts\":\"1700000000.000100\"}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
        }
    }

    private static SlackClient Client(StubHandler h) =>
        new(new HttpClient(h), Options.Create(new YouTubeCommentsOptions()), NullLogger<SlackClient>.Instance);

    private static CommentNotification TopLevel() =>
        new("Ada", "https://youtube.com/@ada", "https://img/avatar.png", "My Video", "VID123",
            "Hello & <there>", 5, DateTimeOffset.UtcNow, "CMT1");

    [Fact]
    public async Task Top_level_comment_builds_card_with_lc_deep_link()
    {
        var h = new StubHandler();
        var res = await Client(h).PostCommentAsync("xoxb-token", "C123", TopLevel());

        Assert.True(res.Ok);
        Assert.Equal("1700000000.000100", res.MessageTs);

        using var doc = JsonDocument.Parse(h.LastBody!);
        var root = doc.RootElement;
        Assert.Equal("C123", root.GetProperty("channel").GetString());
        Assert.False(root.GetProperty("unfurl_links").GetBoolean());
        Assert.False(root.GetProperty("unfurl_media").GetBoolean());

        var blocks = root.GetProperty("blocks");
        Assert.Equal("context", blocks[0].GetProperty("type").GetString());
        Assert.Equal("section", blocks[1].GetProperty("type").GetString());
        Assert.Equal("actions", blocks[2].GetProperty("type").GetString());

        var button = blocks[2].GetProperty("elements")[0];
        Assert.Equal("https://www.youtube.com/watch?v=VID123&lc=CMT1", button.GetProperty("url").GetString());
        Assert.Equal("open_comment", button.GetProperty("action_id").GetString());
    }

    [Fact]
    public async Task Reply_uses_parent_dot_reply_lc_form_and_threads()
    {
        var h = new StubHandler();
        var reply = new CommentNotification("Bob", null, null, "Title", "VID9", "a reply", 0,
            DateTimeOffset.UtcNow, "R1", IsReply: true, ParentCommentId: "P1");

        await Client(h).PostCommentAsync("t", "C", reply, threadTs: "1699999999.000200");

        using var doc = JsonDocument.Parse(h.LastBody!);
        var root = doc.RootElement;
        Assert.Equal("1699999999.000200", root.GetProperty("thread_ts").GetString());
        var url = root.GetProperty("blocks")[2].GetProperty("elements")[0].GetProperty("url").GetString();
        Assert.Equal("https://www.youtube.com/watch?v=VID9&lc=P1.R1", url);
    }

    [Theory]
    [InlineData("is_archived", SlackPostStatus.ChannelGone)]
    [InlineData("channel_not_found", SlackPostStatus.ChannelGone)]
    [InlineData("not_in_channel", SlackPostStatus.ChannelGone)]
    [InlineData("ratelimited", SlackPostStatus.RetryableFailure)]
    [InlineData("internal_error", SlackPostStatus.RetryableFailure)]
    public async Task Slack_error_codes_classify_into_gone_vs_retryable(string error, SlackPostStatus expected)
    {
        var h = new StubHandler { Body = $"{{\"ok\":false,\"error\":\"{error}\"}}" };
        var res = await Client(h).PostCommentAsync("t", "C", TopLevel());
        Assert.Equal(expected, res.Status);
        Assert.Null(res.MessageTs);
    }
}
