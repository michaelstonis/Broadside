using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The targets of issue #56: content with resources (fonts, a Type 3 font, nested and self-referencing forms, an image, graphics
/// state parameter dictionaries, property lists, optional content) and the inline image data finder.
/// </summary>
internal static class ContentObjectsTargets
{
    private static readonly Lazy<(PdfDocument Document, CosDictionary Resources)> ResourceDocument = new(BuildResourceDocument);

    /// <summary>
    /// Runs the input as content whose names resolve in a fixed resource dictionary: <c>/F1</c> Helvetica, <c>/F2</c> an Identity-H
    /// Type 0 font, <c>/F3</c> a Type 3 font, <c>/Fm</c> a form that paints <c>/Fn</c>, which paints <c>/Fm</c> again (a cycle),
    /// <c>/Im</c> an image, <c>/G</c> and <c>/H</c> graphics state parameter dictionaries (one with a soft mask), <c>/P</c> and
    /// <c>/OC</c> property lists (the latter an optional content group that is off). The checking processor of
    /// <c>content-interpreter</c> also checks that glyph events are finite and well placed in their string, that text objects, forms,
    /// Type 3 glyphs and marked-content sequences open and close in pairs, and that the run ends with nothing open.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.4.5, §8.8 to §8.10, §9.3, §9.4, §14.6.</remarks>
    public static void ContentWithResources(ReadOnlySpan<byte> data)
    {
        (PdfDocument document, CosDictionary resources) = ResourceDocument.Value;
        ContentInterpreter.RunBytes(data, document, new ObjectsCheckingProcessor(), ContentInterpreter.DefaultOptions, resources);
    }

    /// <summary>
    /// Wraps the input in an inline image and reads it with the content reader: byte 0 selects the dictionary (no filter with a
    /// computed length, an <c>L</c> taken from byte 1, ASCIIHex, ASCII85 or DCT), the rest is the data and whatever follows. Every
    /// operator must lie inside the content in order, and the inline image's data must lie between <c>ID</c> and the offset where
    /// reading resumes.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.7.</remarks>
    public static void InlineImageEnd(ReadOnlySpan<byte> data)
    {
        byte mode = data.IsEmpty ? (byte)0 : data[0];
        ReadOnlySpan<byte> rest = data.IsEmpty ? data : data[1..];
        string dictionary = (mode % 5) switch
        {
            0 => "/W 4 /H 2 /CS /G /BPC 8",
            1 => string.Create(CultureInfo.InvariantCulture, $"/W 4 /H 2 /CS /RGB /BPC 8 /F /Fl /L {(rest.IsEmpty ? 0 : rest[0])}"),
            2 => "/W 4 /H 2 /CS /G /BPC 8 /F [/AHx /Fl]",
            3 => "/W 4 /H 2 /CS /G /BPC 8 /F /A85",
            _ => "/W 4 /H 2 /CS /G /BPC 8 /F /DCT",
        };
        byte[] prefix = Encoding.ASCII.GetBytes("q BI " + dictionary + " ID ");
        byte[] content = new byte[prefix.Length + rest.Length];
        prefix.CopyTo(content, 0);
        rest.CopyTo(content.AsSpan(prefix.Length));

        var arena = new OperandArena();
        var reader = new ContentReader(content, arena);
        int previousEnd = 0;
        while (reader.Next(out ReadOperator op))
        {
            if (op.KeywordStart < previousEnd || op.End > content.Length || op.KeywordStart + op.KeywordLength > op.End)
            {
                throw new InvalidOperationException($"Operator {op} lies outside the content of {content.Length} bytes or before {previousEnd}.");
            }

            if (op.Code == ContentOperatorCode.BeginInlineImage && op.DataLength > 0
                && (op.DataStart < op.KeywordStart || op.DataStart + op.DataLength > op.End))
            {
                throw new InvalidOperationException($"Inline image data [{op.DataStart}, +{op.DataLength}) lies outside the image ending at {op.End}.");
            }

            arena.Clear();
            previousEnd = op.End;
        }

        ContentInterpreter.RunBytes(content, ResourceDocument.Value.Document, new ObjectsCheckingProcessor(), ContentInterpreter.DefaultOptions);
    }

    private static (PdfDocument Document, CosDictionary Resources) BuildResourceDocument()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [12 0 R] /D << /OFF [12 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources 4 0 R >>",
            "<< /Font << /F1 5 0 R /F2 6 0 R /F3 8 0 R >> /XObject << /Fm 10 0 R /Fn 11 0 R /Im 13 0 R >> "
                + "/ExtGState << /G 14 0 R /H 15 0 R >> /Properties << /P << /MCID 0 >> /OC 12 0 R >> >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type0 /BaseFont /C /Encoding /Identity-H /DescendantFonts [7 0 R] >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /C /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 500 >>",
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 9 0 R >> "
                + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [500] >>",
            Stream("500 0 d0 0 0 100 100 re f /Fm Do", string.Empty),
            Stream("q 0 0 1 1 re f /Fn Do Q BT /F1 5 Tf (x) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [2 0 0 2 1 1]"),
            Stream("/Fm Do /P BDC 1 1 m 2 2 l S", "/Type /XObject /Subtype /Form /BBox [0 0 5 5] /Group << /S /Transparency >>"),
            "<< /Type /OCG /Name (Off) >>",
            Stream("\u0080", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8"),
            "<< /LW 2 /D [[1 2] 0] /BM /Multiply /CA 0.5 /Font [5 0 R 9] /SMask << /S /Alpha /G 11 0 R >> >>",
            "<< /SMask /None /TR /Identity /OP true >>",
        ];
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));
        return (document, document.Pages[0].Resources!);

        static string Stream(string data, string entries) =>
            $"<< {entries} /Length {Encoding.Latin1.GetByteCount(data)} >>\nstream\n{data}\nendstream";
    }

    /// <summary>The checks of <see cref="CheckingProcessor"/> plus pairing and finiteness of the events issue #56 reports.</summary>
    private sealed class ObjectsCheckingProcessor : ContentProcessor
    {
        private readonly CheckingProcessor _paths = new();
        private int _text;
        private int _forms;
        private int _marked;
        private int _glyphs;

        public override void BeginRun(ContentContext context)
        {
            _paths.BeginRun(context);
            (_text, _forms, _marked, _glyphs) = (0, 0, 0, 0);
        }

        public override void EndRun(ContentContext context)
        {
            _paths.EndRun(context);
            if (_text != 0 || _forms != 0 || _marked != 0 || _glyphs != 0 || context.MarkedContentDepth != 0)
            {
                throw new InvalidOperationException($"Unpaired events at the end of the run: text {_text}, forms {_forms}, marked {_marked}, Type 3 glyphs {_glyphs}.");
            }
        }

        public override void SaveState(ContentContext context) => _paths.SaveState(context);

        public override void RestoreState(ContentContext context) => _paths.RestoreState(context);

        public override void PaintPath(in PathEvent path, ContentContext context) => _paths.PaintPath(path, context);

        public override void IntersectClip(in ClipEvent clip, ContentContext context)
        {
            if (clip.Kind != ClipKind.Text)
            {
                _paths.IntersectClip(clip, context);
            }
            else if (clip.Glyphs.IsEmpty || context.State.ClipHandle != clip.Handle)
            {
                throw new InvalidOperationException("A text clip has no glyphs or is not the state's clip.");
            }
        }

        public override void BeginText(ContentContext context) => Pair(ref _text, +1, "BT");

        public override void EndText(ContentContext context) => Pair(ref _text, -1, "ET");

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            if (glyph.CodeLength < 1 || glyph.SourceIndex < 0 || glyph.SourceIndex + glyph.CodeLength > glyph.SourceBytes.Length || glyph.Font is null
                || !double.IsFinite(glyph.HorizontalDisplacement) || !double.IsFinite(glyph.VerticalDisplacement))
            {
                throw new InvalidOperationException($"A glyph event is malformed: code length {glyph.CodeLength} at {glyph.SourceIndex} of {glyph.SourceBytes.Length}.");
            }
        }

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            Pair(ref _forms, +1, "BeginForm");
            return ContentVisit.Enter;
        }

        public override void EndForm(in FormEvent form, ContentContext context) => Pair(ref _forms, -1, "EndForm");

        public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context)
        {
            Pair(ref _glyphs, +1, "BeginType3Glyph");
            return ContentVisit.Enter;
        }

        public override void EndType3Glyph(in GlyphEvent glyph, ContentContext context) => Pair(ref _glyphs, -1, "EndType3Glyph");

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            Pair(ref _marked, +1, "BMC");
            if (mc.Depth != context.MarkedContentDepth)
            {
                throw new InvalidOperationException($"A sequence's depth {mc.Depth} is not the context's {context.MarkedContentDepth}.");
            }
        }

        public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context) => Pair(ref _marked, -1, "EMC");

        private static void Pair(ref int count, int step, string what)
        {
            count += step;
            if (count < 0)
            {
                throw new InvalidOperationException($"{what} without its opening event.");
            }
        }
    }
}
