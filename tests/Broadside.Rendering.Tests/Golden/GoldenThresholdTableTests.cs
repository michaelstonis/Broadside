using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Golden;

/// <summary>The one reviewed table of golden-image thresholds (issue #120): every row is well-formed and the most specific row wins.</summary>
public sealed class GoldenThresholdTableTests
{
    [Fact]
    public void Every_row_names_an_existing_corpus_page_gives_a_reason_and_is_unique()
    {
        var seen = new HashSet<(string, string?, int?)>();
        foreach (GoldenThresholdRule rule in GoldenThresholds.Rules)
        {
            Assert.Matches("^[a-z0-9-]+$", rule.Backend);
            Assert.False(string.IsNullOrWhiteSpace(rule.Reason), $"{rule} needs a reason.");
            Assert.True(seen.Add((rule.Backend, rule.FileName, rule.Page)), $"{rule} repeats an earlier row.");
            if (rule.FileName is null)
            {
                Assert.Null(rule.Page);
                continue;
            }

            Assert.Contains(rule.FileName, Corpus.AllFileNames);
            if (rule.Page is int page)
            {
                using PdfDocument document = GoldenCorpus.Open(rule.FileName);
                Assert.InRange(page, 0, document.Pages.Count - 1);
            }
        }
    }

    [Fact]
    public void A_page_without_a_row_compares_under_the_default()
    {
        Assert.Equal(GoldenThreshold.Default, GoldenThresholds.For("no-such-backend", "paths-fill-rules.pdf", 0));
    }

    [Fact]
    public void The_software_rasterizer_is_held_to_its_own_renders_more_tightly_than_other_backends()
    {
        GoldenThreshold own = GoldenThresholds.For(GoldenThresholds.SoftwareRasterizer, "paths-fill-rules.pdf", 0);

        Assert.True(own.EdgeTolerance < GoldenThreshold.Default.EdgeTolerance);
        Assert.True(own.FlatTolerance <= GoldenThreshold.Default.FlatTolerance);
        Assert.Equal(0, own.MaxDifferingPixels);
    }
}
