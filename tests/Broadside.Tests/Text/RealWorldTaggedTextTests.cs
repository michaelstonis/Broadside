using System.Globalization;
using System.Text;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// The tagged PDF 2.0 files of the fetched corpora (the PDF Association's <c>pdf20examples</c>, the passing files of
/// <c>verapdf-corpus</c> and pdf.js's test files, among them LaTeX's two-column tagging example) extracted in structure order and
/// snapshotted: per page whether the structure tree ordered it, whether that order differs from content order, how many glyphs are
/// artifacts, and the plain text. ISO 32000-2 §14.7, §14.8.2.2, §14.8.2.5 and §14.9. Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldTaggedTextTests
{
    private const int MaxText = 240;

    /// <summary>The corpora searched: the PDF 2.0 examples, veraPDF's passing test files and pdf.js's tagged test files.</summary>
    private static readonly string[] CorpusIds = ["pdf20examples", "verapdf-corpus", "pdfjs"];

    [Fact]
    public async Task Tagged_pdf_2_0_files_extract_in_structure_order_matching_the_snapshot()
    {
        if (CorpusIds.Any(static id => RealWorldCorpus.Directory(id) is null))
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        IEnumerable<string> candidates = CorpusIds.SelectMany(RealWorldCorpus.Files)
            .Where(static path => !path.StartsWith("verapdf-corpus/", StringComparison.Ordinal) || path.Contains("-pass-", StringComparison.Ordinal));
        var output = new StringBuilder();
        int files = 0;
        int reordered = 0;
        foreach (string path in candidates)
        {
            byte[] bytes = await File.ReadAllBytesAsync(RealWorldCorpus.PathOf(path), TestContext.Current.CancellationToken);
            using PdfDocument? document = TryOpen(bytes);
            if (document?.StructureTree is null || document.Version.Major < 2)
            {
                continue;
            }

            files++;
            output.Append(CultureInfo.InvariantCulture, $"{path}\n");
            for (int index = 0; index < document.Pages.Count; index++)
            {
                PdfPage page = document.Pages[index];
                TextPage structure = page.ExtractText();
                TextPage everything = page.ExtractText(new TextExtractionOptions().WithArtifacts());
                TextPage content = page.ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content).WithArtifacts());
                bool tagged = structure.Glyphs.Any(glyph => glyph.StructureElement is not null);
                bool differs = tagged && !structure.Glyphs.Select(glyph => glyph.Origin).SequenceEqual(content.Glyphs.Where(glyph => !IsArtifact(everything, glyph)).Select(glyph => glyph.Origin));
                reordered += differs ? 1 : 0;
                output.Append(CultureInfo.InvariantCulture, $"  page {index + 1}: {(tagged ? "structure" : "content")} order{(differs ? ", differs from content order" : string.Empty)}")
                    .Append(CultureInfo.InvariantCulture, $", glyphs {structure.Glyphs.Count}, artifacts {everything.Glyphs.Count(glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact))}")
                    .Append(CultureInfo.InvariantCulture, $": {Escape(structure.PlainText)}\n");
            }
        }

        output.Insert(0, string.Create(CultureInfo.InvariantCulture, $"{files} tagged PDF 2.0 files, {reordered} pages in an order other than content order\n"));
        await Verify(output.ToString());
    }

    /// <summary>Opens a file of the corpus, or returns <see langword="null"/> for one too damaged to open (the corpus gates cover those).</summary>
    private static PdfDocument? TryOpen(byte[] bytes)
    {
        try
        {
            return PdfDocument.Open(bytes);
        }
        catch (Exception exception) when (exception is Diagnostics.DiagnosticException or Broadside.Security.PdfPasswordException)
        {
            return null;
        }
    }

    /// <summary>Whether the glyph at <paramref name="glyph"/>'s origin is an artifact of the page read with artifacts.</summary>
    private static bool IsArtifact(TextPage everything, TextGlyph glyph) =>
        everything.Glyphs.Any(other => other.Origin == glyph.Origin && other.CharacterCode == glyph.CharacterCode && other.Flags.HasFlag(TextGlyphFlags.Artifact));

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(Math.Min(text.Length, MaxText) + 8);
        foreach (char unit in text.Length > MaxText ? text[..MaxText] : text)
        {
            escaped.Append(unit switch
            {
                '\n' => " / ",
                _ when char.IsControl(unit) || char.IsSurrogate(unit) => $"\\u{(int)unit:X4}",
                _ => unit.ToString(),
            });
        }

        return text.Length > MaxText ? escaped.Append(" ...").ToString() : escaped.ToString();
    }
}
