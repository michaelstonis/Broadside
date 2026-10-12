using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Every <c>text-*.pdf</c> corpus file's extracted glyphs, snapshotted: text, code, font, size, rendering mode, flags, origin,
/// advance and quad; then its lines with their words. ISO 32000-2 §9.4.4, §9.10 and §14.8.2.6.2. The text of each file is also asserted independently by
/// <c>UnicodeCorpusTests</c>; the snapshots pin positions.
/// </summary>
public sealed class TextCorpusTests
{
    /// <summary>The <c>text-*.pdf</c> files of the corpus.</summary>
    public static TheoryData<string> TextFiles => new(Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(TextFiles))]
    public Task Every_text_corpus_file_extracts_glyphs_matching_the_snapshot(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));
        var output = new StringBuilder();
        for (int index = 0; index < document.Pages.Count; index++)
        {
            TextPage page = document.Pages[index].ExtractText();
            output.Append(CultureInfo.InvariantCulture, $"page {index + 1}: {Escape(page.Text)}\n");
            foreach (TextGlyph glyph in page.Glyphs)
            {
                output.Append(CultureInfo.InvariantCulture, $"  {Escape(page.GetGlyphText(glyph).ToString())} code {glyph.CharacterCode:X}/{glyph.CodeLength} {glyph.UnicodeSource}")
                    .Append(CultureInfo.InvariantCulture, $" font {glyph.Font.BaseFont ?? glyph.Font.FontType.ToString()} {Number(glyph.FontSize)} {glyph.RenderingMode}")
                    .Append(glyph.IsVertical ? " vertical" : string.Empty)
                    .Append(glyph.Flags == TextGlyphFlags.None ? string.Empty : " " + glyph.Flags)
                    .Append(CultureInfo.InvariantCulture, $" origin {Point(glyph.Origin)} advance {Point(glyph.Advance)}")
                    .Append(CultureInfo.InvariantCulture, $" quad {Point(glyph.Quad.BottomLeft)} {Point(glyph.Quad.BottomRight)} {Point(glyph.Quad.TopRight)} {Point(glyph.Quad.TopLeft)}\n");
            }

            foreach (TextLine line in page.Lines)
            {
                IEnumerable<string> words = page.Words.Skip(line.WordStart).Take(line.WordCount)
                    .Select(word => string.Create(CultureInfo.InvariantCulture, $"{Escape(page.GetWordText(word).ToString())} glyphs {word.GlyphStart}+{word.GlyphCount}"));
                output.Append(CultureInfo.InvariantCulture, $"  line {Escape(page.GetLineText(line).ToString())}{(line.IsVertical ? " vertical" : string.Empty)} glyphs {line.GlyphStart}+{line.GlyphCount}")
                    .Append(CultureInfo.InvariantCulture, $" quad {Point(line.Quad.BottomLeft)} {Point(line.Quad.BottomRight)} {Point(line.Quad.TopRight)} {Point(line.Quad.TopLeft)}\n")
                    .Append(CultureInfo.InvariantCulture, $"    words {string.Join(" | ", words)}\n");
            }
        }

        return Verify(output.ToString()).UseParameters(file);
    }

    private static string Point(PathPoint point) => $"({Number(point.X)}, {Number(point.Y)})";

    private static string Number(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (char unit in text)
        {
            escaped.Append(char.IsControl(unit) || char.IsSurrogate(unit) ? $"\\u{(int)unit:X4}" : unit);
        }

        return escaped.ToString();
    }
}
