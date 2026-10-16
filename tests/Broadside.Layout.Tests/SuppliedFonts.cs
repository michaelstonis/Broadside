using System.Buffers.Binary;
using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Objects;

namespace Broadside.Layout.Tests;

/// <summary>
/// What a caller supplies so an auto-tagged layout corpus file can conform to PDF/UA-1 beyond alternate text (issue #206): the
/// corpus files show text in the non-embedded Standard 14 fonts Helvetica and Helvetica-Bold (ISO 14289-1 §7.21.4.1 wants every font
/// embedded), and <c>layout-figures.pdf</c> has a Type 3 font whose two glyphs, a square and a triangle, map to no Unicode
/// (§7.21.7). Each Helvetica font dictionary becomes Liberation Sans, metric-compatible with it, embedded whole as a non-symbolic
/// TrueType font with <c>WinAnsiEncoding</c> and the program's own widths (§7.21.5, §7.21.6); the Type 3 font gets a <c>ToUnicode</c>
/// CMap. Converting fonts is not auto-tagging's job: <see cref="PdfDocument.ConvertToPdfA"/> (#229) embeds them in the library.
/// </summary>
internal static class SuppliedFonts
{
    private static readonly CosName Font = new("Font");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName BaseFont = new("BaseFont");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName ToUnicode = new("ToUnicode");

    public static void Supply(PdfDocument document)
    {
        var embedded = new Dictionary<string, (CosReference Descriptor, CosArray Widths)>();
        foreach (PdfPage page in document.Pages)
        {
            if (page.Resources is not { } resources || document.Resolve(resources.GetValueOrDefault(Font)) is not CosDictionary fonts)
            {
                continue;
            }

            foreach (KeyValuePair<CosName, CosObject> entry in fonts.ToArray())
            {
                if (document.Resolve(entry.Value) is not CosDictionary font)
                {
                    continue;
                }

                string? subtype = (document.Resolve(font.GetValueOrDefault(Subtype)) as CosName)?.Value;
                string? baseFont = (document.Resolve(font.GetValueOrDefault(BaseFont)) as CosName)?.Value;
                if (subtype == "Type1" && !font.ContainsKey(FontDescriptor) && baseFont is "Helvetica" or "Helvetica-Bold")
                {
                    if (!embedded.TryGetValue(baseFont, out var program))
                    {
                        embedded[baseFont] = program = Embed(document, baseFont == "Helvetica" ? Standard14Font.Helvetica : Standard14Font.HelveticaBold, baseFont == "Helvetica" ? "LiberationSans" : "LiberationSans-Bold");
                    }

                    font[Subtype] = new CosName("TrueType");
                    font[BaseFont] = new CosName(baseFont == "Helvetica" ? "LiberationSans" : "LiberationSans-Bold");
                    font[new CosName("FirstChar")] = new CosInteger(32);
                    font[new CosName("LastChar")] = new CosInteger(255);
                    font[new CosName("Widths")] = program.Widths;
                    font[new CosName("Encoding")] = new CosName("WinAnsiEncoding");
                    font[FontDescriptor] = program.Descriptor;
                }
                else if (subtype == "Type3" && !font.ContainsKey(ToUnicode))
                {
                    font[ToUnicode] = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(
                        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n"
                        + "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def /CMapName /Adobe-Identity-UCS def /CMapType 2 def\n"
                        + "1 begincodespacerange <00> <FF> endcodespacerange\n2 beginbfchar <01> <25A0> <02> <25B2> endbfchar\n"
                        + "endcmap CMapName currentdict /CMap defineresource pop end end\n")));
                }
            }
        }
    }

    private static (CosReference Descriptor, CosArray Widths) Embed(PdfDocument document, Standard14Font standard, string name)
    {
        ReadOnlyMemory<byte> program = Standard14FontResolver.GetProgram(standard);
        CosReference file = document.AddObject(new CosStream(new CosDictionary { [new CosName("Length1")] = new CosInteger(program.Length) }, program));
        CosReference descriptor = document.AddObject(new CosDictionary
        {
            [new CosName("Type")] = new CosName("FontDescriptor"),
            [new CosName("FontName")] = new CosName(name),
            [new CosName("Flags")] = new CosInteger(32),
            [new CosName("FontBBox")] = new CosArray { new CosInteger(-203), new CosInteger(-303), new CosInteger(1050), new CosInteger(910) },
            [new CosName("ItalicAngle")] = new CosInteger(0),
            [new CosName("Ascent")] = new CosInteger(905),
            [new CosName("Descent")] = new CosInteger(-212),
            [new CosName("CapHeight")] = new CosInteger(729),
            [new CosName("StemV")] = new CosInteger(80),
            [new CosName("FontFile2")] = file,
        });

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding winAnsi = Encoding.GetEncoding(1252);
        var metrics = new TrueTypeMetrics(program.Span);
        var widths = new CosArray();
        for (int code = 32; code <= 255; code++)
        {
            string text = winAnsi.GetString([(byte)code]);
            widths.Add(new CosInteger(metrics.Width(text[0])));
        }

        return (descriptor, widths);
    }

    /// <summary>Advance widths in thousandths of an em from a TrueType program's <c>head</c>, <c>hhea</c>, <c>hmtx</c> and (3,1) <c>cmap</c>.</summary>
    private sealed class TrueTypeMetrics
    {
        private readonly int _unitsPerEm;
        private readonly int[] _advances;
        private readonly Dictionary<char, int> _glyphs = [];

        public TrueTypeMetrics(ReadOnlySpan<byte> font)
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);
            int head = 0, hhea = 0, hmtx = 0, cmap = 0;
            for (int index = 0; index < tables; index++)
            {
                ReadOnlySpan<byte> record = font.Slice(12 + (16 * index), 16);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                switch (Encoding.ASCII.GetString(record[..4]))
                {
                    case "head":
                        head = offset;
                        break;
                    case "hhea":
                        hhea = offset;
                        break;
                    case "hmtx":
                        hmtx = offset;
                        break;
                    case "cmap":
                        cmap = offset;
                        break;
                }
            }

            _unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(font[(head + 18)..]);
            int metrics = BinaryPrimitives.ReadUInt16BigEndian(font[(hhea + 34)..]);
            _advances = new int[metrics];
            for (int index = 0; index < metrics; index++)
            {
                _advances[index] = BinaryPrimitives.ReadUInt16BigEndian(font[(hmtx + (4 * index))..]);
            }

            int subtables = BinaryPrimitives.ReadUInt16BigEndian(font[(cmap + 2)..]);
            for (int index = 0; index < subtables; index++)
            {
                ReadOnlySpan<byte> record = font[(cmap + 4 + (8 * index))..];
                if (BinaryPrimitives.ReadUInt16BigEndian(record) != 3 || BinaryPrimitives.ReadUInt16BigEndian(record[2..]) != 1)
                {
                    continue;
                }

                int table = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
                int segments = BinaryPrimitives.ReadUInt16BigEndian(font[(table + 6)..]) / 2;
                int ends = table + 14, starts = ends + (2 * segments) + 2, deltas = starts + (2 * segments), ranges = deltas + (2 * segments);
                for (int segment = 0; segment < segments; segment++)
                {
                    int end = BinaryPrimitives.ReadUInt16BigEndian(font[(ends + (2 * segment))..]);
                    int start = BinaryPrimitives.ReadUInt16BigEndian(font[(starts + (2 * segment))..]);
                    int delta = BinaryPrimitives.ReadInt16BigEndian(font[(deltas + (2 * segment))..]);
                    int range = BinaryPrimitives.ReadUInt16BigEndian(font[(ranges + (2 * segment))..]);
                    for (int code = start; code <= end && code <= 0xFFFF && code != 0xFFFF; code++)
                    {
                        int glyph = range == 0 ? (code + delta) & 0xFFFF
                            : BinaryPrimitives.ReadUInt16BigEndian(font[(ranges + (2 * segment) + range + (2 * (code - start)))..]) is var raw and not 0 ? (raw + delta) & 0xFFFF : 0;
                        _glyphs[(char)code] = glyph;
                    }
                }

                break;
            }
        }

        public int Width(char character)
        {
            int glyph = _glyphs.GetValueOrDefault(character);
            int advance = _advances[Math.Min(glyph, _advances.Length - 1)];
            return (int)Math.Round(advance * 1000.0 / _unitsPerEm, MidpointRounding.AwayFromZero);
        }
    }
}
