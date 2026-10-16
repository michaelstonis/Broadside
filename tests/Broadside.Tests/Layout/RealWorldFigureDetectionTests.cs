using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// Figure and caption detection (issue #190) measured on real-world untagged pages of the fetched corpora (the diagrams and charts
/// of a paper, an image in a book, a vector drawing over a caption) against the hand labels of <c>LayoutFigureLabels.txt</c>.
/// Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldFigureDetectionTests
{
    // Measured when detection landed (10 labelled figures on 8 pages, 2 table captions): every figure found (overlap 0.5 or more
    // with its labelled box), no other figure detected on those pages, and every caption on its figure or table. The floors
    // allow a miss in ten.
    private const double MinimumRecall = 0.9;
    private const double MinimumPrecision = 0.9;
    private const double MinimumCaptionAccuracy = 0.9;

    [Fact]
    public void Detected_figures_and_captions_match_the_labelled_ones()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int figures = 0;
        int found = 0;
        int detected = 0;
        int captions = 0;
        int captioned = 0;
        foreach ((string path, int pageNumber, List<Label> labels) in Sections())
        {
            using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(path));
            LayoutTree tree = document.Pages[pageNumber - 1].ExtractLayout();
            Assert.True(tree.Pages[0].IsDetected, $"{path} page {pageNumber} is detected");
            List<LayoutFigure> candidates = [.. All(tree.Root).OfType<LayoutFigure>()];
            detected += candidates.Count;
            var report = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{path} p{pageNumber}: {candidates.Count} detected"));
            foreach (Label label in labels)
            {
                captions++;
                if (label.Box is not { } box)
                {
                    // A table's caption.
                    bool has = All(tree.Root).OfType<LayoutTable>().Any(table => CaptionOf(table) is { } text && text.StartsWith(label.Caption, StringComparison.Ordinal));
                    captioned += has ? 1 : 0;
                    report.Append(CultureInfo.InvariantCulture, $"\n  table caption {(has ? "found" : "missed")}: {label.Caption}");
                    continue;
                }

                figures++;
                LayoutFigure? match = candidates.Where(figure => Overlap(box, figure.BoundingBox!.Value) >= 0.5)
                    .OrderByDescending(figure => Overlap(box, figure.BoundingBox!.Value)).FirstOrDefault();
                if (match is null)
                {
                    report.Append(CultureInfo.InvariantCulture, $"\n  missed {box}");
                    continue;
                }

                found++;
                candidates.Remove(match);
                string? caption = CaptionOf(match);
                bool right = label.Caption == "-" ? caption is null : caption?.StartsWith(label.Caption, StringComparison.Ordinal) == true;
                captioned += right ? 1 : 0;
                report.Append(CultureInfo.InvariantCulture, $"\n  found {match.BoundingBox} overlap {Overlap(box, match.BoundingBox!.Value):0.00}, caption {(right ? "right" : $"wrong: {caption}")}");
            }

            TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        }

        double recall = (double)found / figures;
        double precision = detected == 0 ? 0 : (double)found / detected;
        double accuracy = (double)captioned / captions;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{figures} figures: found {found} (recall {recall:0.00}), {detected} detected (precision {precision:0.00}); captions {captioned}/{captions} ({accuracy:0.00})"));
        Assert.True(recall >= MinimumRecall, $"recall {recall:0.00}");
        Assert.True(precision >= MinimumPrecision, $"precision {precision:0.00}");
        Assert.True(accuracy >= MinimumCaptionAccuracy, $"caption accuracy {accuracy:0.00}");
    }

    private static string? CaptionOf(LayoutNode node) =>
        node.Children.FirstOrDefault(static child => child.Kind == LayoutNodeKind.Caption) is { } caption ? Normalize(caption.Text) : null;

    /// <summary>Intersection over union.</summary>
    private static double Overlap(PdfRectangle a, PdfRectangle b)
    {
        double width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        double height = Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        double both = width * height;
        return both / ((a.Width * a.Height) + (b.Width * b.Height) - both);
    }

    private static IEnumerable<LayoutNode> All(LayoutNode node) => node.Children.SelectMany(All).Prepend(node);

    private static string Normalize(string text) =>
        string.Join(' ', text.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<(string Path, int Page, List<Label> Labels)> Sections()
    {
        (string Path, int Page, List<Label> Labels)? current = null;
        foreach (string line in File.ReadLines(Path.Combine(Path.GetDirectoryName(SourcePath())!, "LayoutFigureLabels.txt")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    yield return current.Value;
                }

                string[] parts = line[3..].Split(' ');
                current = (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), []);
                continue;
            }

            int bar = line.IndexOf(" | ", StringComparison.Ordinal);
            string caption = Normalize(line[(bar + 3)..]);
            string[] fields = line[..bar].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            PdfRectangle? box = fields[0] == "F"
                ? new PdfRectangle(Number(fields[1]), Number(fields[2]), Number(fields[3]), Number(fields[4]))
                : null;
            current!.Value.Labels.Add(new Label(box, caption));
        }

        if (current is not null)
        {
            yield return current.Value;
        }
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private readonly record struct Label(PdfRectangle? Box, string Caption);
}
