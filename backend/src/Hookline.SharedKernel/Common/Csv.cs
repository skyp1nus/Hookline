using System.Text;

namespace Hookline.SharedKernel.Common;

/// <summary>
/// Minimal RFC 4180 CSV writer for the export endpoints (System → Logs, Uploads → History). A field that
/// contains a comma, double-quote, CR or LF is wrapped in double-quotes with inner quotes doubled;
/// everything else is emitted verbatim. Rows are CRLF-terminated (the line ending Excel expects).
/// </summary>
public static class Csv
{
    /// <summary>Quote a single field iff it contains a delimiter, quote or newline.</summary>
    public static string Field(string? value)
    {
        var s = value ?? string.Empty;
        if (s.IndexOfAny(['"', ',', '\n', '\r']) < 0)
        {
            return s;
        }

        return $"\"{s.Replace("\"", "\"\"")}\"";
    }

    /// <summary>Join one row's fields (each quoted as needed) with commas.</summary>
    public static string Row(params string?[] fields)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Field(fields[i]));
        }

        return sb.ToString();
    }

    /// <summary>Build a full CSV document: a header row followed by data rows, each CRLF-terminated.</summary>
    public static string Document(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(Row([.. header])).Append("\r\n");
        foreach (var row in rows)
        {
            sb.Append(Row([.. row])).Append("\r\n");
        }

        return sb.ToString();
    }
}
