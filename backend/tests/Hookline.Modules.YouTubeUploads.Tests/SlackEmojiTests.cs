using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

public sealed class SlackEmojiTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Empty_or_null_returns_empty(string? input, string expected)
    {
        Assert.Equal(expected, SlackEmoji.ShortcodesToUnicode(input));
    }

    [Fact]
    public void Known_shortcode_is_replaced()
    {
        Assert.Equal("🔥", SlackEmoji.ShortcodesToUnicode(":fire:"));
    }

    [Theory]
    [InlineData(":+1:", "👍")]
    [InlineData(":rocket:", "🚀")]
    [InlineData(":tada:", "🎉")]
    [InlineData(":heart:", "❤️")]
    [InlineData(":smile:", "😄")]
    [InlineData(":100:", "💯")]
    public void Common_shortcodes_map_correctly(string input, string expected)
    {
        Assert.Equal(expected, SlackEmoji.ShortcodesToUnicode(input));
    }

    [Fact]
    public void Unknown_shortcode_passes_through()
    {
        Assert.Equal(":custom_emoji:", SlackEmoji.ShortcodesToUnicode(":custom_emoji:"));
    }

    [Fact]
    public void Skin_tone_modifier_is_stripped()
    {
        Assert.Equal("", SlackEmoji.ShortcodesToUnicode(":skin-tone-3:"));
    }

    [Fact]
    public void Text_without_colons_is_unchanged()
    {
        Assert.Equal("no emoji here", SlackEmoji.ShortcodesToUnicode("no emoji here"));
    }

    [Fact]
    public void Multiple_shortcodes_in_text()
    {
        Assert.Equal("I 🔥 and 🚀", SlackEmoji.ShortcodesToUnicode("I :fire: and :rocket:"));
    }

    [Fact]
    public void Colon_delimited_non_emoji_is_unchanged()
    {
        Assert.Equal("16:9 ratio", SlackEmoji.ShortcodesToUnicode("16:9 ratio"));
    }
}
