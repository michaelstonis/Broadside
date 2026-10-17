using System.Buffers.Binary;
using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;

namespace Broadside.TestSupport;

/// <summary>
/// Subroutine-heavy CFF test fonts made from a TrueType program's outlines (issue #151): every glyph has eight stem hints and a
/// <c>hintmask</c>, each contour is a local subroutine that starts with its own <c>hintmask</c> (whose mask byte is the
/// <c>callsubr</c> opcode, so a walk that miscounts stems misreads it) and calls global subroutines of up to three segments each;
/// identical contours and segment runs share their subroutine. Widths use a nominalWidthX of 1000. The glyph ids are the TrueType
/// program's.
/// </summary>
/// <remarks>Adobe Technical Notes #5176 (CFF) and #5177 (Type 2 charstrings: §4.3 hintmask, §4.7 subroutines, Appendix C seac).</remarks>
public static class CffTestFonts
{
    /// <summary>The StandardEncoding codes of <c>A</c> and <c>acute</c>, the components of the <c>Aacute</c> accent (5176 Appendix B).</summary>
    private const int CodeA = 65;
    private const int CodeAcute = 194;

    /// <summary>
    /// An OpenType program with CFF outlines (and the TrueType program's <c>cmap</c>, <c>head</c>, <c>hhea</c>, <c>hmtx</c>,
    /// <c>OS/2</c>, <c>name</c> and <c>post</c>).
    /// </summary>
    /// <param name="trueType">The TrueType program.</param>
    /// <param name="cidKeyed">Whether the CFF font is CID-keyed, with two Font DICTs (each with its own local subroutines) chosen by a format 3 FDSelect.</param>
    /// <returns>The program.</returns>
    public static byte[] OpenType(ReadOnlyMemory<byte> trueType, bool cidKeyed = false)
    {
        byte[] cff = Cff(trueType, cidKeyed, accent: false);
        var tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string tag in (string[])["cmap", "head", "hhea", "hmtx", "OS/2", "name", "post"])
        {
            tables[tag] = Table(trueType.Span, tag);
        }

        byte[] maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
        maxp[4] = Table(trueType.Span, "maxp")[4];
        maxp[5] = Table(trueType.Span, "maxp")[5];
        tables["maxp"] = maxp;
        return CffBuilder.OpenType(cff, tables);
    }

    /// <summary>
    /// A bare name-keyed CFF program: glyphs named <c>A</c>–<c>Z</c>, <c>a</c>–<c>z</c>, <c>acute</c> and <c>Aacute</c> where
    /// the TrueType cmap has those characters, <c>uniXXXX</c> otherwise.
    /// </summary>
    /// <param name="trueType">The TrueType program.</param>
    /// <param name="accent">Whether <c>Aacute</c> is drawn by an <c>endchar</c> accent of <c>A</c> and <c>acute</c> rather than by its outline.</param>
    /// <returns>The program.</returns>
    public static byte[] Cff(ReadOnlyMemory<byte> trueType, bool accent = false) => Cff(trueType, cidKeyed: false, accent);

    private static byte[] Cff(ReadOnlyMemory<byte> trueType, bool cidKeyed, bool accent)
    {
        FontProgram program = new TrueTypeFontProgramParser().Parse(trueType, new FontProgramContext())!;
        FontCharacterMap cmap = program.CharacterMaps.First(map => map is { PlatformId: 3, EncodingId: 1 });
        var characters = new Dictionary<int, int>();
        for (int scalar = 0x20; scalar < 0x10000; scalar++)
        {
            int glyph = cmap.GetGlyphId(scalar);
            if (glyph > 0)
            {
                _ = characters.TryAdd(glyph, scalar);
            }
        }

        int count = program.GlyphCount;
        int half = count / 2;
        var localBodies = new Dictionary<string, int>[] { new(StringComparer.Ordinal), new(StringComparer.Ordinal) };
        var localSubrs = new List<byte[]>[] { [], [] };
        var globalChunks = new Dictionary<string, int>(StringComparer.Ordinal);
        var globalSubrs = new List<byte[]>();
        var glyphParts = new List<(int Width, int[] Box, List<(byte[] MoveTo, int Subr)> Contours)>();
        var outline = new GlyphOutline();
        for (int glyph = 0; glyph < count; glyph++)
        {
            outline.Clear();
            _ = program.GetOutline(glyph, outline);
            int fd = cidKeyed && glyph >= half ? 1 : 0;
            var parts = new List<(byte[] MoveTo, int Subr)>();
            int x = 0;
            int y = 0;
            int[] box = [int.MaxValue, int.MaxValue, int.MinValue, int.MinValue];
            var segments = new List<byte[]>();
            byte[]? moveTo = null;
            int pointIndex = 0;
            ReadOnlySpan<PathPoint> points = outline.Path.Points;
            void Flush()
            {
                if (moveTo is null)
                {
                    return;
                }

                // The contour body (written below, once the biases are known): a hintmask, then calls of global subroutines of up to three segments each.
                var calls = new List<int>();
                for (int start = 0; start < segments.Count; start += 3)
                {
                    byte[] chunk = [.. segments.Skip(start).Take(3).SelectMany(segment => segment), 11];
                    string key = Convert.ToHexString(chunk);
                    if (!globalChunks.TryGetValue(key, out int index))
                    {
                        index = globalSubrs.Count;
                        globalChunks[key] = index;
                        globalSubrs.Add(chunk);
                    }

                    calls.Add(index);
                }

                string bodyKey = string.Join(",", calls);
                if (!localBodies[fd].TryGetValue(bodyKey, out int subr))
                {
                    subr = localSubrs[fd].Count;
                    localBodies[fd][bodyKey] = subr;
                    localSubrs[fd].Add(Encoding.ASCII.GetBytes(bodyKey));
                }

                parts.Add((moveTo, subr));
                moveTo = null;
                segments.Clear();
            }

            (int X, int Y) Point(PathPoint point)
            {
                int px = (int)Math.Round(point.X);
                int py = (int)Math.Round(point.Y);
                box[0] = Math.Min(box[0], px);
                box[1] = Math.Min(box[1], py);
                box[2] = Math.Max(box[2], px);
                box[3] = Math.Max(box[3], py);
                return (px, py);
            }

            foreach (PathVerb verb in outline.Path.Verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        Flush();
                        (int mx, int my) = Point(points[pointIndex++]);
                        moveTo = CffBuilder.T2(mx - x, my - y, "rmoveto");
                        (x, y) = (mx, my);
                        break;
                    case PathVerb.LineTo:
                        (int lx, int ly) = Point(points[pointIndex++]);
                        segments.Add(CffBuilder.T2(lx - x, ly - y, "rlineto"));
                        (x, y) = (lx, ly);
                        break;
                    case PathVerb.QuadTo:
                        {
                            PathPoint control = points[pointIndex++];
                            PathPoint end = points[pointIndex++];
                            (int c1x, int c1y) = Point(new PathPoint(x + (2.0 / 3 * (control.X - x)), y + (2.0 / 3 * (control.Y - y))));
                            (int ex, int ey) = Point(end);
                            (int c2x, int c2y) = Point(new PathPoint(ex + (2.0 / 3 * (control.X - ex)), ey + (2.0 / 3 * (control.Y - ey))));
                            segments.Add(CffBuilder.T2(c1x - x, c1y - y, c2x - c1x, c2y - c1y, ex - c2x, ey - c2y, "rrcurveto"));
                            (x, y) = (ex, ey);
                            break;
                        }

                    case PathVerb.CubicTo:
                        {
                            (int c1x, int c1y) = Point(points[pointIndex++]);
                            (int c2x, int c2y) = Point(points[pointIndex++]);
                            (int ex, int ey) = Point(points[pointIndex++]);
                            segments.Add(CffBuilder.T2(c1x - x, c1y - y, c2x - c1x, c2y - c1y, ex - c2x, ey - c2y, "rrcurveto"));
                            (x, y) = (ex, ey);
                            break;
                        }

                    default:
                        break;
                }
            }

            Flush();
            if (box[0] > box[2])
            {
                box = [0, 0, 0, 0];
            }

            glyphParts.Add(((int)Math.Round(program.GetMetrics(glyph).AdvanceWidth), box, parts));
        }

        int globalBias = Bias(globalSubrs.Count);
        int[] localBias = [Bias(localSubrs[0].Count), Bias(localSubrs[1].Count)];
        for (int fd = 0; fd < 2; fd++)
        {
            for (int index = 0; index < localSubrs[fd].Count; index++)
            {
                string key = Encoding.ASCII.GetString(localSubrs[fd][index]);
                var body = new List<byte>(CffBuilder.T2("hintmask", "\n"u8.ToArray()));
                foreach (string call in key.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    body.AddRange(CffBuilder.T2(int.Parse(call, System.Globalization.CultureInfo.InvariantCulture) - globalBias, "callgsubr"));
                }

                body.Add(11);
                localSubrs[fd][index] = [.. body];
            }
        }

        var glyphs = new List<(string Name, byte[] CharString)>();
        int? aacute = null;
        for (int glyph = 0; glyph < count; glyph++)
        {
            (int width, int[] box, List<(byte[] MoveTo, int Subr)> parts) = glyphParts[glyph];
            int fd = cidKeyed && glyph >= half ? 1 : 0;
            string name = glyph == 0 ? ".notdef" : Name(characters.TryGetValue(glyph, out int scalar) ? scalar : -glyph);
            var code = new List<byte>(CffBuilder.T2(width - 1000, box[1], 20, 20, 20, 20, 20, 20, 20, "hstemhm", box[0], 20, 20, 20, 20, 20, 20, 20, "hintmask", new byte[] { 0xFF }));
            foreach ((byte[] moveTo, int subr) in parts)
            {
                code.AddRange(moveTo);
                code.AddRange(CffBuilder.T2(subr - localBias[fd], "callsubr"));
            }

            code.AddRange(CffBuilder.T2("endchar"));
            if (accent && name == "Aacute")
            {
                aacute = glyph;
                code = [.. CffBuilder.T2(width - 1000, 120, 300, CodeA, CodeAcute, "endchar")];
            }

            glyphs.Add((name, [.. code]));
        }

        if (accent && aacute is null)
        {
            throw new InvalidOperationException("The program has no Á to draw as an accent.");
        }

        var builder = new CffBuilder { Name = "BroadsideSubrTest", NominalWidthX = 1000 };
        builder.Glyphs.AddRange(glyphs);
        builder.GlobalSubrs.AddRange(globalSubrs);
        double[] matrix = [1.0 / 2048, 0, 0, 1.0 / 2048, 0, 0];
        if (cidKeyed)
        {
            builder.Ros = ("Adobe", "Identity", 0);
            for (int fd = 0; fd < 2; fd++)
            {
                var dict = new CffFontDict { FontMatrix = matrix, NominalWidthX = 1000 };
                dict.LocalSubrs.AddRange(localSubrs[fd]);
                builder.FontDicts.Add(dict);
            }

            builder.Charset = CffBuilder.CharsetFormat0([.. Enumerable.Range(1, count - 1)]);
            builder.FdSelect = CffBuilder.FdSelectFormat3(count, (0, 0), (half, 1));
        }
        else
        {
            builder.FontMatrix = matrix;
            builder.LocalSubrs.AddRange(localSubrs[0]);
        }

        return builder.Build();
    }

    /// <summary>The glyph name: a standard name for ASCII letters, <c>acute</c> and <c>Aacute</c>, else <c>uniXXXX</c> (or <c>gN</c> for a glyph without a character).</summary>
    private static string Name(int scalar) => scalar switch
    {
        < 0 => $"g{-scalar}",
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' => ((char)scalar).ToString(),
        0xB4 => "acute",
        0xC1 => "Aacute",
        _ => $"uni{scalar:X4}",
    };

    private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    /// <summary>A table of an sfnt program by tag.</summary>
    private static byte[] Table(ReadOnlySpan<byte> sfnt, string tag)
    {
        int tables = BinaryPrimitives.ReadUInt16BigEndian(sfnt[4..]);
        uint wanted = BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(tag));
        for (int index = 0; index < tables; index++)
        {
            ReadOnlySpan<byte> record = sfnt[(12 + (16 * index))..];
            if (BinaryPrimitives.ReadUInt32BigEndian(record) == wanted)
            {
                return sfnt.Slice((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..])).ToArray();
            }
        }

        throw new ArgumentException($"The program has no {tag} table.", nameof(sfnt));
    }
}
