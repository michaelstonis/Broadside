using System.Text;
using Broadside.Content;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>Records the Unicode text and its source of every glyph a page shows, through <see cref="GlyphEvent.Unicode"/>.</summary>
internal sealed class UnicodeRecorder : ContentProcessor
{
    public List<(string Text, UnicodeSource Source, uint Code)> Glyphs { get; } = [];

    public override ContentEvents Events => ContentEvents.Glyphs;

    /// <summary>Gets the text of every glyph, concatenated.</summary>
    public string Text => string.Concat(Glyphs.Select(glyph => glyph.Text));

    /// <summary>The text of each page of a document, in order.</summary>
    public static string[] PagesOf(PdfDocument document) =>
        [.. document.Pages.Select(page =>
        {
            var recorder = new UnicodeRecorder();
            page.ProcessContent(recorder);
            return recorder.Text;
        })];

    /// <summary>The glyphs of the first page.</summary>
    public static UnicodeRecorder FirstPageOf(PdfDocument document)
    {
        var recorder = new UnicodeRecorder();
        document.Pages[0].ProcessContent(recorder);
        return recorder;
    }

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) =>
        Glyphs.Add((new string(glyph.Unicode), glyph.UnicodeSource, glyph.CharacterCode));

    /// <summary>Spells text as code points for readable assertion messages.</summary>
    public static string CodePoints(string text) =>
        string.Join(' ', text.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));

    /// <summary>A rune as a string.</summary>
    public static string Of(int scalar) => new Rune(scalar).ToString();
}
