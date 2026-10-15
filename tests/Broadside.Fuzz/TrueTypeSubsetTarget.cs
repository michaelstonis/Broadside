using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>truetype-subset</c> target (issue #150): the input is a TrueType program (a whole file is read from its first
/// <c>00 01 00 00</c>, so <c>text-truetype-embedded.pdf</c> seeds the target). Invariants: subsetting never throws; a subset
/// re-parses through the TrueType parser with exactly the glyphs it says it kept, <c>.notdef</c> first; and embedding the program in a
/// document, showing the printable ASCII and Cyrillic characters it has glyphs for and saving never throws, and the saved page
/// extracts one glyph per character shown.
/// </summary>
/// <remarks>ISO 32000-2 §9.7.4, §9.9 (Table 124), §9.9.2 and §9.10.3.</remarks>
internal static class TrueTypeSubsetTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            int start = data.IndexOf((ReadOnlySpan<byte>)[0, 1, 0, 0]);
            if (start < 0)
            {
                return;
            }

            data = data[start..];
        }

        byte[] bytes = data.ToArray();
        if (SfntFile.Open(bytes, new FontProgramContext()) is not { } sfnt)
        {
            return;
        }

        // Every third glyph id below 300, and ids from the input's first bytes (some past the glyph count).
        var glyphs = new List<int>();
        for (int glyph = 1; glyph < 300; glyph += 3)
        {
            glyphs.Add(glyph);
        }

        for (int index = 0; index + 1 < Math.Min(bytes.Length, 32); index += 2)
        {
            glyphs.Add((bytes[index] << 8) | bytes[index + 1]);
        }

        if (TrueTypeSubsetter.Subset(sfnt, glyphs) is { } subset)
        {
            if (subset.Glyphs.Length == 0 || subset.Glyphs[0] != 0)
            {
                throw new InvalidOperationException("The subset does not start with .notdef.");
            }

            // The subset copies head, hhea, maxp and OS/2: it re-parses whenever the program it was made from does.
            var parser = new TrueTypeFontProgramParser();
            FontProgram? program = parser.Parse(subset.Program, new FontProgramContext());
            if (parser.Parse(bytes, new FontProgramContext()) is not null && (program is null || program.GlyphCount != subset.Glyphs.Length))
            {
                throw new InvalidOperationException($"The subset of {subset.Glyphs.Length} glyphs of a program that parses does not re-parse with that many glyphs.");
            }

            if (program is null)
            {
                return;
            }

            var outline = new GlyphOutline();
            for (int glyph = 0; glyph < program.GlyphCount; glyph++)
            {
                _ = program.GetOutline(glyph, outline);
            }
        }

        Embed(bytes);
    }

    private static void Embed(byte[] bytes)
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseSystemFontResolver(null));
        var resolution = new FontResolution(bytes, "fuzz", FontMatchKind.Exact);
        if (EmbeddedCanvasFont.Get(document, resolution, [], out _, out _) is not { } font)
        {
            return;
        }

        var text = new System.Text.StringBuilder();
        for (int scalar = 0x21; scalar < 0x7F; scalar++)
        {
            text.Append(font.CanEncode([(char)scalar]) ? (char)scalar : string.Empty);
        }

        for (int scalar = 0x410; scalar < 0x450; scalar++)
        {
            text.Append(font.CanEncode([(char)scalar]) ? (char)scalar : string.Empty);
        }

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 12);
            canvas.MoveText(10, 400);
            canvas.ShowText(text.ToString());
            canvas.EndText();
        }

        using var output = new MemoryStream();
        document.Save(output);
        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        int extracted = reopened.Pages[0].ExtractText().Glyphs.Count;
        if (extracted != text.Length)
        {
            throw new InvalidOperationException($"{text.Length} characters shown, {extracted} glyphs extracted.");
        }
    }
}
