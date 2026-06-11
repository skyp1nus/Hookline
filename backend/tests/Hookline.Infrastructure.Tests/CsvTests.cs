using Hookline.SharedKernel.Common;

namespace Hookline.Infrastructure.Tests;

/// <summary>RFC 4180 escaping for the CSV export endpoints (commas, quotes, newlines).</summary>
public sealed class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has \"quote\"", "\"has \"\"quote\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    public void Field_quotes_only_when_needed(string input, string expected) =>
        Assert.Equal(expected, Csv.Field(input));

    [Fact]
    public void Field_treats_null_as_empty() => Assert.Equal("", Csv.Field(null));

    [Fact]
    public void Row_joins_and_escapes_each_field()
    {
        Assert.Equal("a,\"b,c\",d", Csv.Row("a", "b,c", "d"));
    }

    [Fact]
    public void Document_emits_a_header_then_crlf_terminated_rows()
    {
        var csv = Csv.Document(
            ["name", "note"],
            [["alice", "ok"], ["bob", "has,comma"]]);

        Assert.Equal(
            "name,note\r\nalice,ok\r\nbob,\"has,comma\"\r\n",
            csv);
    }
}
