namespace Hookline.Modules.YouTubeUploads.Infrastructure;

/// <summary>
/// YouTube's <c>snippet.tags</c> field has a 500-character limit measured on the SERIALISED form, not on
/// the bare tag text: tags are joined by commas and any tag containing whitespace is wrapped in double
/// quotes, and BOTH the separating commas AND those quotes count toward the 500. So one tag costs its own
/// length, plus 2 if it holds whitespace, plus 1 for the comma joining it to the previous tag.
///
/// Exceeding the limit makes <c>videos.insert</c> fail with <c>invalidTags</c> ("The request metadata
/// specifies invalid video keywords") and rejects the WHOLE upload — no tag survives. An earlier
/// length-only budget (it summed tag lengths + quotes but ignored the commas) under-counted by one char
/// per extra tag, so a set the app believed fit was rejected by YouTube and the upload died. This is the
/// single source of truth for that accounting; both the parser and the upload service trim through it.
///
/// Trimming always keeps the in-order prefix and drops ONLY from the end: once a tag would overflow, it
/// and every later tag are dropped. It never reorders or skips a middle tag.
/// </summary>
internal static class YouTubeTagBudget
{
    /// <summary>YouTube's documented limit on the serialised tag list.</summary>
    internal const int MaxTotalChars = 500;

    /// <summary>Serialised cost of one tag. The <paramref name="first"/> tag pays no leading comma;
    /// every later tag adds 1 for the comma that joins it to the previous one.</summary>
    internal static int Cost(string tag, bool first) =>
        tag.Length + (tag.Any(char.IsWhiteSpace) ? 2 : 0) + (first ? 0 : 1);

    /// <summary>Total serialised length of an ordered tag list (for diagnostics / warning text).</summary>
    internal static int TotalCost(IReadOnlyList<string> tags)
    {
        var sum = 0;
        for (var i = 0; i < tags.Count; i++) sum += Cost(tags[i], i == 0);
        return sum;
    }

    /// <summary>The longest in-order prefix whose serialised length stays within <see cref="MaxTotalChars"/>.
    /// Drops only from the end. Assumes the caller already trimmed/deduped individual tags.</summary>
    internal static List<string> Fit(IReadOnlyList<string> tags)
    {
        var kept = new List<string>(tags.Count);
        var run = 0;
        foreach (var t in tags)
        {
            var cost = Cost(t, first: kept.Count == 0);
            if (run + cost > MaxTotalChars) break;
            run += cost;
            kept.Add(t);
        }
        return kept;
    }
}
