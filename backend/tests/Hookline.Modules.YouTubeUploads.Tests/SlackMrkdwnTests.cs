using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

public sealed class SlackMrkdwnTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Empty_or_null_returns_empty(string? input, string expected)
    {
        Assert.Equal(expected, SlackMrkdwn.ToPlainText(input));
    }

    [Fact]
    public void Plain_text_passes_through()
    {
        Assert.Equal("hello world", SlackMrkdwn.ToPlainText("hello world"));
    }

    [Fact]
    public void User_mention_with_label_expands()
    {
        Assert.Equal("@alice", SlackMrkdwn.ToPlainText("<@U123|alice>"));
    }

    [Fact]
    public void User_mention_without_label_keeps_at_target()
    {
        Assert.Equal("@U123", SlackMrkdwn.ToPlainText("<@U123>"));
    }

    [Fact]
    public void Channel_mention_with_label_expands()
    {
        Assert.Equal("#general", SlackMrkdwn.ToPlainText("<#C123|general>"));
    }

    [Fact]
    public void Special_command_here()
    {
        Assert.Equal("@here", SlackMrkdwn.ToPlainText("<!here>"));
    }

    [Fact]
    public void Special_command_channel()
    {
        Assert.Equal("@channel", SlackMrkdwn.ToPlainText("<!channel>"));
    }

    [Fact]
    public void Special_command_everyone()
    {
        Assert.Equal("@everyone", SlackMrkdwn.ToPlainText("<!everyone>"));
    }

    [Fact]
    public void Subteam_with_label_returns_label()
    {
        Assert.Equal("@eng", SlackMrkdwn.ToPlainText("<!subteam^S1|@eng>"));
    }

    [Fact]
    public void Plain_url_entity_becomes_text()
    {
        Assert.Equal("https://example.com", SlackMrkdwn.ToPlainText("<https://example.com>"));
    }

    [Fact]
    public void Url_with_different_label_includes_both()
    {
        Assert.Equal("Click here (https://example.com)",
            SlackMrkdwn.ToPlainText("<https://example.com|Click here>"));
    }

    [Fact]
    public void Mailto_strips_prefix()
    {
        Assert.Equal("user@example.com", SlackMrkdwn.ToPlainText("<mailto:user@example.com>"));
    }

    [Fact]
    public void Html_escapes_are_decoded()
    {
        Assert.Equal("a & b", SlackMrkdwn.ToPlainText("a &amp; b"));
    }

    [Fact]
    public void Literal_angle_brackets_from_escapes_are_stripped()
    {
        Assert.Equal("ab", SlackMrkdwn.ToPlainText("&lt;a&gt;b"));
    }

    [Fact]
    public void Emoji_shortcodes_are_converted()
    {
        Assert.Equal("🔥", SlackMrkdwn.ToPlainText(":fire:"));
    }
}
