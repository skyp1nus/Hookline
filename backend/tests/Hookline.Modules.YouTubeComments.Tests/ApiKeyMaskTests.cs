using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

/// <summary>The stored key is never exposed — only a masked hint (first 4 + ellipsis + last 4).</summary>
public class ApiKeyMaskTests
{
    [Fact]
    public void Mask_shows_first_and_last_four() =>
        Assert.Equal("AIza…cdef", ApiKeyService.Mask("AIzaSyABCDEFGHIJKLMNOPcdef"));

    [Theory]
    [InlineData("short", "•••• (5)")]
    [InlineData("eightchr", "•••• (8)")]
    public void Mask_hides_short_keys(string key, string expected) =>
        Assert.Equal(expected, ApiKeyService.Mask(key));

    [Fact]
    public void Mask_empty_is_empty() => Assert.Equal(string.Empty, ApiKeyService.Mask(""));
}
