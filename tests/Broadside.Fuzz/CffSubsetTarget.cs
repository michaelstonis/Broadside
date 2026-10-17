using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>cff-subset</c> target (issue #151): the input is a bare CFF program or an OpenType program with a <c>CFF </c> table (a
/// whole file is read from its first stream that starts with a CFF header or <c>OTTO</c>, so <c>text-cff-embedded.pdf</c> and
/// <c>text-opentype-cff-embedded.pdf</c> seed the target). Invariants: subsetting never throws; a subset re-parses through the CFF
/// parser with exactly the glyphs it says it kept, <c>.notdef</c> first; every kept glyph that draws in the original draws the same
/// outline and advance in the subset (no subroutine call dangles); and embedding the program, showing the printable ASCII
/// characters it has glyphs for and saving never throws, and the saved page extracts one glyph per character shown.
/// </summary>
/// <remarks>ISO 32000-2 §9.7.4.2, §9.9 (Table 124) and §9.9.2; Adobe Technical Notes #5176 and #5177.</remarks>
internal static class CffSubsetTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            data = FirstProgram(data);
        }

        byte[] bytes = data.ToArray();
        if (bytes.Length < 4)
        {
            return;
        }

        ReadOnlyMemory<byte> cff = bytes;
        if (bytes.AsSpan().StartsWith("OTTO"u8))
        {
            if (SfntFile.Open(bytes, new FontProgramContext()) is not { } sfnt || !sfnt.TryGetTable(SfntFile.Tag("CFF "), out cff))
            {
                return;
            }
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

        if (CffSubsetter.Subset(cff, glyphs) is { } subset)
        {
            Check(cff, subset, glyphs);
        }

        Embed(bytes);
    }

    private static void Check(ReadOnlyMemory<byte> cff, SubsetProgram subset, List<int> glyphs)
    {
        if (subset.Glyphs.Length == 0 || subset.Glyphs[0] != 0)
        {
            throw new InvalidOperationException("The subset does not start with .notdef.");
        }

        var parser = new CffFontProgramParser();
        FontProgram original = parser.Parse(cff, new FontProgramContext())!;
        if (parser.Parse(subset.Program, new FontProgramContext()) is not { } program || program.GlyphCount != subset.Glyphs.Length)
        {
            throw new InvalidOperationException($"The subset of {subset.Glyphs.Length} glyphs does not re-parse with that many glyphs.");
        }

        var before = new GlyphOutline();
        var after = new GlyphOutline();
        foreach (int originalGlyph in glyphs.Append(0).Where(glyph => (uint)glyph < (uint)original.GlyphCount).Distinct())
        {
            // Every glyph asked for is kept (an accented name-keyed font keeps the glyph ids and empties the others).
            int glyph = subset.NewId(originalGlyph);
            if (subset.Glyphs[glyph] != originalGlyph)
            {
                throw new InvalidOperationException($"Glyph {originalGlyph} was asked for but is not in the subset.");
            }

            before.Clear();
            after.Clear();
            GlyphOutlineStatus status = original.GetOutline(originalGlyph, before);
            if (status is not (GlyphOutlineStatus.Complete or GlyphOutlineStatus.Empty))
            {
                continue;
            }

            GlyphOutlineStatus subsetStatus = program.GetOutline(glyph, after);
            if (subsetStatus != status || !Same(before, original.FontMatrix, after, program.FontMatrix)
                || Math.Abs((original.GetMetrics(originalGlyph).AdvanceWidth * original.FontMatrix.A) - (program.GetMetrics(glyph).AdvanceWidth * program.FontMatrix.A)) > 1e-9)
            {
                throw new InvalidOperationException($"Glyph {originalGlyph} ({status}) draws differently in the subset ({subsetStatus}) as glyph {glyph}.");
            }
        }
    }

    /// <summary>Two outlines with the same verbs and the same points in text space (each through its program's matrix).</summary>
    private static bool Same(GlyphOutline left, Matrix leftMatrix, GlyphOutline right, Matrix rightMatrix)
    {
        PathView a = left.Path;
        PathView b = right.Path;
        if (!a.Verbs.SequenceEqual(b.Verbs) || a.Points.Length != b.Points.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Points.Length; index++)
        {
            PathPoint p = a.Points[index];
            PathPoint q = b.Points[index];
            double ax = (p.X * leftMatrix.A) + (p.Y * leftMatrix.C);
            double ay = (p.X * leftMatrix.B) + (p.Y * leftMatrix.D);
            double bx = (q.X * rightMatrix.A) + (q.Y * rightMatrix.C);
            double by = (q.X * rightMatrix.B) + (q.Y * rightMatrix.D);
            if (Math.Abs(ax - bx) > 1e-9 * Math.Max(1, Math.Abs(ax)) || Math.Abs(ay - by) > 1e-9 * Math.Max(1, Math.Abs(ay)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first stream of a file that holds a CFF or OpenType program, else the whole input.</summary>
    private static ReadOnlySpan<byte> FirstProgram(ReadOnlySpan<byte> data)
    {
        for (int at = data.IndexOf("stream"u8); at >= 0;)
        {
            ReadOnlySpan<byte> rest = data[(at + 6)..];
            int start = rest.StartsWith("\r\n"u8) ? 2 : rest.StartsWith("\n"u8) ? 1 : -1;
            if (start > 0 && (CffFont.IsHeader(rest[start..]) || rest[start..].StartsWith("OTTO"u8)))
            {
                return rest[start..];
            }

            int next = rest.IndexOf("stream"u8);
            at = next < 0 ? -1 : at + 6 + next;
        }

        return data;
    }

    internal static void Embed(byte[] bytes)
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseSystemFontResolver(null));
        var resolution = new FontResolution(bytes, "fuzz", FontMatchKind.Exact);
        if (EmbeddedCanvasFont.Get(document, resolution, [], out _, out _, cff: true) is not { } font)
        {
            return;
        }

        var text = new System.Text.StringBuilder();
        for (int scalar = 0x21; scalar < 0x7F; scalar++)
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
