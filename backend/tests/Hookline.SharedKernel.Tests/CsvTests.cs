using Hookline.SharedKernel.Common;

namespace Hookline.SharedKernel.Tests;

public sealed class CsvTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Field_returns_value_unchanged_when_no_special_chars(string? input, string expected)
    {
        Assert.Equal(expected, Csv.Field(input));
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\none", "\"line\none\"")]
    [InlineData("line\rone", "\"line\rone\"")]
    public void Field_quotes_special_characters(string input, string expected)
    {
        Assert.Equal(expected, Csv.Field(input));
    }

    [Fact]
    public void Row_joins_fields_with_commas()
    {
        Assert.Equal("a,b,c", Csv.Row("a", "b", "c"));
    }

    [Fact]
    public void Row_quotes_fields_that_need_it()
    {
        Assert.Equal("a,\"b,c\",d", Csv.Row("a", "b,c", "d"));
    }

    [Fact]
    public void Row_empty_returns_empty_string()
    {
        Assert.Equal("", Csv.Row());
    }

    [Fact]
    public void Document_produces_crlf_terminated_output()
    {
        var doc = Csv.Document(
            ["Name", "Age"],
            [["Alice", "30"], ["Bob", "25"]]);

        Assert.Equal("Name,Age\r\nAlice,30\r\nBob,25\r\n", doc);
    }

    [Fact]
    public void Document_with_no_data_rows_has_header_only()
    {
        var doc = Csv.Document(["H1"], []);

        Assert.Equal("H1\r\n", doc);
    }
}
