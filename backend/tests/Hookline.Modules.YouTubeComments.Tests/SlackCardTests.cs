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

    [Fact]
    public async Task Reject_button_added_with_confirm_when_mapping_id_supplied_and_can_moderate()
    {
        var h = new StubHandler();
        var mappingId = Guid.NewGuid();

        // canModerate: true ⇒ the owning Google account holds the force-ssl scope, so the active Reject renders.
        await Client(h).PostCommentAsync("xoxb-token", "C123", TopLevel(), mappingId: mappingId, canModerate: true);

        using var doc = JsonDocument.Parse(h.LastBody!);
        var elements = doc.RootElement.GetProperty("blocks")[2].GetProperty("elements");
        Assert.Equal(2, elements.GetArrayLength());
        Assert.Equal("open_comment", elements[0].GetProperty("action_id").GetString());

        var reject = elements[1];
        Assert.Equal("reject_comment", reject.GetProperty("action_id").GetString());
        Assert.Equal("danger", reject.GetProperty("style").GetString());
        Assert.Equal($"{mappingId}:CMT1", reject.GetProperty("value").GetString());
        // Irreversible-from-Slack ⇒ a native confirm dialog is attached, and the label is honest ("Reject").
        Assert.True(reject.TryGetProperty("confirm", out var confirm));
        Assert.Contains("Reject", reject.GetProperty("text").GetProperty("text").GetString());
        Assert.Contains("hides", confirm.GetProperty("text").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Reconsent_link_replaces_reject_when_account_cannot_moderate()
    {
        var h = new StubHandler();
        var mappingId = Guid.NewGuid();

        // mappingId present but the owning account lacks force-ssl (canModerate: false) ⇒ a proactive
        // "Re-consent to enable removal" LINK to Connections → Google, NOT an active Reject button that
        // would only fail on click.
        await Client(h).PostCommentAsync("xoxb-token", "C123", TopLevel(), mappingId: mappingId, canModerate: false);

        using var doc = JsonDocument.Parse(h.LastBody!);
        var elements = doc.RootElement.GetProperty("blocks")[2].GetProperty("elements");
        Assert.Equal(2, elements.GetArrayLength());
        Assert.Equal("open_comment", elements[0].GetProperty("action_id").GetString());

        var gated = elements[1];
        Assert.Equal("reconsent_google", gated.GetProperty("action_id").GetString());
        Assert.EndsWith("/connections/google", gated.GetProperty("url").GetString());
        // It is a pure URL link: no reject value, no confirm dialog, no server action.
        Assert.False(gated.TryGetProperty("value", out _));
        Assert.False(gated.TryGetProperty("confirm", out _));
        // And NO active reject button is present anywhere on the card.
        foreach (var el in elements.EnumerateArray())
            Assert.NotEqual("reject_comment", el.GetProperty("action_id").GetString());
    }

    [Fact]
    public async Task No_reject_button_without_mapping_id()
    {
        var h = new StubHandler();

        await Client(h).PostCommentAsync("xoxb-token", "C123", TopLevel());

        using var doc = JsonDocument.Parse(h.LastBody!);
        var elements = doc.RootElement.GetProperty("blocks")[2].GetProperty("elements");
        Assert.Equal(1, elements.GetArrayLength());
        Assert.Equal("open_comment", elements[0].GetProperty("action_id").GetString());
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
