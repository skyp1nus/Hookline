using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

/// <summary>
/// Pins YouTube's serialised tag-length accounting (<see cref="YouTubeTagBudget"/>) and the two call sites
/// that trim through it (<see cref="SlackTemplateParser"/> at ingest, <see cref="YouTubeUploadService.NormalizeTags"/>
/// at upload). The regression these guard: an earlier budget summed tag lengths + quotes but ignored the
/// commas joining tags, so a 25-tag set the app believed fit (478) actually serialised to 502 and YouTube
/// rejected the WHOLE upload with invalidTags. The fix counts the commas and keeps only the in-order prefix.
/// </summary>
public sealed class YouTubeTagBudgetTests
{
    // The exact tag set from the 2026-06-17 prod failure (Unstract sponsored video) — 26 tags whose
    // length+quotes was 478 (under the old 480 cap) but whose real serialised length was 502 > 500.
    private static readonly string[] UnstractTags =
    [
        "unstract", "unstract ai", "unstractai", "ai unstract", "unstract app", "unstract platform",
        "unstract review", "unstract app review", "unstract platform review", "unstract document ai review",
        "unstract ocr review", "unstract unstructured data", "unstract document processing",
        "unstract data extraction", "unstract tool", "unstract tutorial", "unstract features",
        "unstract pricing", "unstract overview", "unstract guide", "unstract for business",
        "unstract pros cons", "how to use unstract", "what is unstract", "unstract demo", "unstract test",
    ];

    [Fact]
    public void Cost_counts_length_quotes_and_separator()
    {
        Assert.Equal(2, YouTubeTagBudget.Cost("ab", first: true));    // bare length
        Assert.Equal(3, YouTubeTagBudget.Cost("ab", first: false));   // + 1 leading comma
        Assert.Equal(5, YouTubeTagBudget.Cost("a b", first: true));   // + 2 quotes (whitespace)
        Assert.Equal(6, YouTubeTagBudget.Cost("a b", first: false));  // + quotes + comma
    }

    [Fact]
    public void TotalCost_includes_the_commas_between_tags()
    {
        // 3 bare tags of len 2 => 2 + (2+1) + (2+1) = 8, NOT 6.
        Assert.Equal(8, YouTubeTagBudget.TotalCost(new[] { "aa", "bb", "cc" }));
    }

    [Fact]
    public void Fit_drops_only_from_the_end()
    {
        // Two 250-char bare tags: first = 250, second = 250 + 1 comma = 501 > 500 → only the first survives.
        var a = new string('a', 250);
        var b = new string('b', 250);
        Assert.Equal(new[] { a }, YouTubeTagBudget.Fit([a, b]));
    }

    [Fact]
    public void Fit_result_always_serialises_within_the_limit()
    {
        var kept = YouTubeTagBudget.Fit(UnstractTags);
        Assert.True(YouTubeTagBudget.TotalCost(kept) <= YouTubeTagBudget.MaxTotalChars);
    }

    [Fact]
    public void Fit_keeps_the_in_order_prefix()
    {
        var kept = YouTubeTagBudget.Fit(UnstractTags);
        Assert.Equal(UnstractTags.Take(kept.Count), kept); // same order, no skipped/reordered tags
    }

    [Fact]
    public void NormalizeTags_real_prod_failure_now_fits_under_500()
    {
        var kept = YouTubeUploadService.NormalizeTags(UnstractTags);
        Assert.NotNull(kept);
        // The set that was rejected (502) now serialises within the limit...
        Assert.True(YouTubeTagBudget.TotalCost([.. kept]) <= 500);
        // ...by dropping only the last tags, not random middle ones.
        Assert.Equal(UnstractTags.Take(kept.Count), kept);
        // and it keeps almost all of them (24 of 26) — not over-trimming.
        Assert.Equal(24, kept.Count);
    }
}
