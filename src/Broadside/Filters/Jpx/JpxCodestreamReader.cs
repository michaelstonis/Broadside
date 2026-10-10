using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>
/// Finds the codestream in a JP2/JPX file or takes a raw codestream, and parses its main header and tile-part headers.
/// </summary>
/// <remarks>
/// ITU-T T.800 Annex A (codestream syntax) and Annex I (JP2 boxes: I.4 box header, I.5.1 signature, I.5.4 contiguous codestream).
/// Lenient: unknown boxes and marker segments are skipped, reserved markers 0xFF30 to 0xFF3F are bare (A.1.3), deviations recorded.
/// </remarks>
internal static class JpxCodestreamReader
{
    private const ushort Soc = 0xFF4F;
    private const ushort Siz = 0xFF51;
    private const ushort Cod = 0xFF52;
    private const ushort Coc = 0xFF53;
    private const ushort Qcd = 0xFF5C;
    private const ushort Qcc = 0xFF5D;
    private const ushort Rgn = 0xFF5E;
    private const ushort Poc = 0xFF5F;
    private const ushort Ppm = 0xFF60;
    private const ushort Ppt = 0xFF61;
    private const ushort Sot = 0xFF90;
    private const ushort Sod = 0xFF93;
    private const ushort Eoc = 0xFFD9;

    private const uint ContiguousCodestreamBox = 0x6A703263; // 'jp2c'
    private const int MaxTiles = 65535;

    private static ReadOnlySpan<byte> Signature => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>Returns the range of the codestream inside <paramref name="data"/>: the data itself, or the first <c>jp2c</c> box.</summary>
    /// <returns>(offset, length), or <see langword="null"/> when no codestream is found.</returns>
    /// <remarks>ISO 32000-2 §7.4.9 expects a JPX file; raw codestreams occur in practice and are read without a diagnostic.</remarks>
    public static (int Offset, int Length)? Locate(ReadOnlySpan<byte> data, JpxReporter reporter)
    {
        if (data.Length >= 4 && BinaryPrimitives.ReadUInt16BigEndian(data) == Soc && BinaryPrimitives.ReadUInt16BigEndian(data[2..]) == Siz)
        {
            return (0, data.Length);
        }

        if (data.StartsWith(Signature))
        {
            if (FindBox(data, ContiguousCodestreamBox, reporter) is { } box)
            {
                return box;
            }

            reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Error, "The JPEG 2000 file has no contiguous codestream box (jp2c).");
            return Scan(data);
        }

        reporter.Report(
            DiagnosticCodes.JpxSignatureInvalid,
            DiagnosticSeverity.Warning,
            "The JPXDecode data is neither a JPEG 2000 file nor a codestream; the first SOC and SIZ markers in it are used.");
        return Scan(data);
    }

    /// <summary>Reads the SIZ marker segment only.</summary>
    public static JpxImageSize? ReadSize(ReadOnlySpan<byte> codestream, JpxReporter reporter)
    {
        if (codestream.Length < 4 || BinaryPrimitives.ReadUInt16BigEndian(codestream) != Soc || BinaryPrimitives.ReadUInt16BigEndian(codestream[2..]) != Siz)
        {
            reporter.Report(DiagnosticCodes.JpxMarkerUnexpected, DiagnosticSeverity.Error, "The JPEG 2000 codestream does not start with the SOC and SIZ markers.");
            return null;
        }

        if (!TrySegment(codestream, 2, reporter, out ReadOnlySpan<byte> body))
        {
            return null;
        }

        return ParseSize(body, reporter);
    }

    /// <summary>Parses the main header and every tile-part header of <paramref name="codestream"/>.</summary>
    public static JpxCodestream? Read(ReadOnlySpan<byte> codestream, JpxReporter reporter)
    {
        if (ReadSize(codestream, reporter) is not { } size)
        {
            return null;
        }

        int components = size.Components.Length;
        var main = new JpxMarkerSet(components);
        int position = 4 + BinaryPrimitives.ReadUInt16BigEndian(codestream[4..]);
        bool ended = !ReadMarkers(codestream, ref position, main, size, inTile: false, reporter);
        if (main.Cod is null || main.Qcd is null)
        {
            reporter.Report(DiagnosticCodes.JpxMarkerSegmentInvalid, DiagnosticSeverity.Error, "The JPEG 2000 main header has no COD or no QCD marker segment.");
            return null;
        }

        int tileCount = size.TilesWide * size.TilesHigh;
        var tiles = new JpxTile?[tileCount];
        while (!ended && position + 2 <= codestream.Length)
        {
            ushort marker = BinaryPrimitives.ReadUInt16BigEndian(codestream[position..]);
            if (marker == Eoc)
            {
                break;
            }

            if (marker != Sot || position + 12 > codestream.Length)
            {
                if (marker == Sot)
                {
                    Truncated(reporter);
                }
                else
                {
                    reporter.Report(DiagnosticCodes.JpxMarkerUnexpected, DiagnosticSeverity.Warning, $"A JPEG 2000 tile-part is expected where the codestream holds 0x{marker:X4}; the rest is ignored.");
                }

                break;
            }

            int start = position;
            int index = BinaryPrimitives.ReadUInt16BigEndian(codestream[(position + 4)..]);
            long length = BinaryPrimitives.ReadUInt32BigEndian(codestream[(position + 6)..]);
            int part = codestream[position + 10];
            long end;
            if (length == 0)
            {
                end = codestream.Length;
                if (codestream.EndsWith((ReadOnlySpan<byte>)[0xFF, 0xD9]))
                {
                    end -= 2;
                }
            }
            else if (length < 14)
            {
                reporter.Report(DiagnosticCodes.JpxPsotInvalid, DiagnosticSeverity.Warning, $"A JPEG 2000 tile-part length Psot of {length} is impossible; the rest of the codestream is ignored.");
                break;
            }
            else
            {
                end = start + length;
                if (end > codestream.Length)
                {
                    Truncated(reporter);
                    end = codestream.Length;
                }
            }

            JpxTile? tile = null;
            if (index >= tileCount)
            {
                reporter.Report(DiagnosticCodes.JpxMarkerSegmentInvalid, DiagnosticSeverity.Warning, $"A JPEG 2000 tile-part names tile {index} of {tileCount}; it is ignored.");
            }
            else
            {
                tile = tiles[index] ??= new JpxTile(index, components);
            }

            position = start + 12;
            JpxMarkerSet markers = tile is not null && part == 0 && tile.Parts.Count == 0 ? tile.Markers : new JpxMarkerSet(components);
            int headerEnd = (int)end;
            if (!ReadMarkers(codestream[..headerEnd], ref position, markers, size, inTile: true, reporter))
            {
                Truncated(reporter);
                break;
            }

            tile?.Parts.Add((position, headerEnd - position));
            position = headerEnd;
        }

        var present = new List<JpxTile>();
        foreach (JpxTile? tile in tiles)
        {
            if (tile is not null)
            {
                present.Add(tile);
            }
        }

        if (present.Count < tileCount)
        {
            Truncated(reporter);
        }

        return new JpxCodestream { Size = size, Main = main, Tiles = present };
    }

    private static void Truncated(JpxReporter reporter) => reporter.Report(
        DiagnosticCodes.JpxCodestreamTruncated,
        DiagnosticSeverity.Warning,
        "The JPEG 2000 codestream ends early; what is present is decoded and the rest of the image is left empty.");

    /// <summary>
    /// Reads marker segments from <paramref name="position"/> up to the first SOT (main header) or past the SOD (tile-part header).
    /// </summary>
    /// <returns><see langword="false"/> when the header is cut short or broken.</returns>
    private static bool ReadMarkers(ReadOnlySpan<byte> data, ref int position, JpxMarkerSet markers, JpxImageSize size, bool inTile, JpxReporter reporter)
    {
        while (position + 2 <= data.Length)
        {
            ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (marker is >= 0xFF30 and <= 0xFF3F)
            {
                position += 2;
                continue;
            }

            if (inTile && marker == Sod)
            {
                position += 2;
                return true;
            }

            if (!inTile && marker is Sot or Eoc)
            {
                return true;
            }

            if (marker < 0xFF00 || !TrySegment(data, position, reporter, out ReadOnlySpan<byte> body))
            {
                if (marker < 0xFF00)
                {
                    reporter.Report(DiagnosticCodes.JpxMarkerUnexpected, DiagnosticSeverity.Warning, "A JPEG 2000 header holds bytes that are not a marker segment; the header ends there.");
                }

                return false;
            }

            switch (marker)
            {
                case Cod when ParseCodingStyle(body, reporter) is { } style:
                    markers.Cod = style;
                    break;
                case Coc when ParseComponentIndex(body, size, reporter, out int c, out int used) && ParseComponentStyle(body[(used + 1)..], (body[used] & 1) != 0, reporter) is { } component:
                    markers.Coc[c] = component;
                    break;
                case Qcd when ParseQuantization(body, reporter) is { } quantization:
                    markers.Qcd = quantization;
                    break;
                case Qcc when ParseComponentIndex(body, size, reporter, out int c, out int used) && ParseQuantization(body[used..], reporter) is { } quantization:
                    markers.Qcc[c] = quantization;
                    break;
                case Rgn:
                    reporter.ReportUnsupported("a region of interest (RGN)");
                    break;
                case Poc:
                    reporter.ReportUnsupported("progression order changes (POC)");
                    break;
                case Ppm or Ppt:
                    reporter.ReportUnsupported("packed packet headers (PPM, PPT)");
                    break;
            }

            position += 2 + body.Length + 2;
        }

        return false;
    }

    /// <summary>Returns the body of the marker segment at <paramref name="position"/> (after its length field).</summary>
    private static bool TrySegment(ReadOnlySpan<byte> data, int position, JpxReporter reporter, out ReadOnlySpan<byte> body)
    {
        body = default;
        if (position + 4 > data.Length)
        {
            Truncated(reporter);
            return false;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
        if (length < 2)
        {
            reporter.Report(DiagnosticCodes.JpxMarkerSegmentInvalid, DiagnosticSeverity.Warning, $"A JPEG 2000 marker segment has the impossible length {length}.");
            return false;
        }

        if (position + 2 + length > data.Length)
        {
            Truncated(reporter);
            return false;
        }

        body = data.Slice(position + 4, length - 2);
        return true;
    }

    private static JpxImageSize? ParseSize(ReadOnlySpan<byte> body, JpxReporter reporter)
    {
        if (body.Length < 36)
        {
            return InvalidSize(reporter, "it is too short");
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(body[34..]);
        if (count is < 1 or > 16384 || body.Length < 36 + (3 * count))
        {
            return InvalidSize(reporter, $"it declares {count} components");
        }

        var components = new JpxComponentInfo[count];
        for (int c = 0; c < count; c++)
        {
            byte precision = body[36 + (3 * c)];
            int dx = body[37 + (3 * c)];
            int dy = body[38 + (3 * c)];
            int depth = (precision & 0x7F) + 1;
            if (depth > 38 || dx == 0 || dy == 0)
            {
                return InvalidSize(reporter, $"component {c} has {depth} bits or a zero separation");
            }

            components[c] = new JpxComponentInfo(depth, (precision & 0x80) != 0, dx, dy);
        }

        var size = new JpxImageSize
        {
            Width = U32(body, 2),
            Height = U32(body, 6),
            OriginX = U32(body, 10),
            OriginY = U32(body, 14),
            TileWidth = U32(body, 18),
            TileHeight = U32(body, 22),
            TileOriginX = U32(body, 26),
            TileOriginY = U32(body, 30),
            Components = components,
        };

        if (size.Width <= size.OriginX || size.Height <= size.OriginY || size.TileWidth == 0 || size.TileHeight == 0
            || size.TileOriginX > size.OriginX || size.TileOriginY > size.OriginY
            || size.TileOriginX + size.TileWidth <= size.OriginX || size.TileOriginY + size.TileHeight <= size.OriginY)
        {
            return InvalidSize(reporter, "its image and tile areas are inconsistent (B-3, B-4)");
        }

        long wide = JpxMath.CeilDivide(size.Width - size.TileOriginX, size.TileWidth);
        long high = JpxMath.CeilDivide(size.Height - size.TileOriginY, size.TileHeight);
        if (wide > MaxTiles || high > MaxTiles || wide * high > MaxTiles)
        {
            reporter.Report(DiagnosticCodes.JpxLimitExceeded, DiagnosticSeverity.Error, $"The JPEG 2000 codestream declares more than {MaxTiles} tiles.");
            return null;
        }

        return size;
    }

    private static JpxImageSize? InvalidSize(JpxReporter reporter, string reason)
    {
        reporter.Report(DiagnosticCodes.JpxMarkerSegmentInvalid, DiagnosticSeverity.Error, $"The JPEG 2000 SIZ marker segment cannot be used: {reason}.");
        return null;
    }

    private static JpxCodingStyle? ParseCodingStyle(ReadOnlySpan<byte> body, JpxReporter reporter)
    {
        if (body.Length < 5)
        {
            Invalid(reporter, "COD");
            return null;
        }

        int layers = BinaryPrimitives.ReadUInt16BigEndian(body[2..]);
        if (layers == 0 || body[1] > 4 || ParseComponentStyle(body[5..], (body[0] & 1) != 0, reporter) is not { } component)
        {
            Invalid(reporter, "COD");
            return null;
        }

        return new JpxCodingStyle
        {
            MayUseSop = (body[0] & 2) != 0,
            UsesEph = (body[0] & 4) != 0,
            Progression = body[1],
            Layers = layers,
            Transform = body[4],
            Component = component,
        };
    }

    private static JpxComponentStyle? ParseComponentStyle(ReadOnlySpan<byte> body, bool precinctsDefined, JpxReporter reporter)
    {
        if (body.Length < 5)
        {
            Invalid(reporter, "COD or COC");
            return null;
        }

        int levels = body[0];
        int width = (body[1] & 0x0F) + 2;
        int height = (body[2] & 0x0F) + 2;
        if (levels > 32 || width > 10 || height > 10 || width + height > 12 || (precinctsDefined && body.Length < 5 + levels + 1))
        {
            Invalid(reporter, "COD or COC");
            return null;
        }

        byte[] precincts = new byte[levels + 1];
        for (int r = 0; r <= levels; r++)
        {
            precincts[r] = precinctsDefined ? body[5 + r] : (byte)0xFF;
            if (r > 0 && ((precincts[r] & 0x0F) == 0 || (precincts[r] >> 4) == 0))
            {
                // A.6.1, Table A.21: only resolution level 0 may have precincts of size 1 (exponent 0).
                Invalid(reporter, "COD or COC");
                precincts[r] = (byte)(precincts[r] | ((precincts[r] & 0x0F) == 0 ? 0x01 : 0) | ((precincts[r] >> 4) == 0 ? 0x10 : 0));
            }
        }

        return new JpxComponentStyle
        {
            Levels = levels,
            BlockWidthExponent = width,
            BlockHeightExponent = height,
            BlockStyle = body[3],
            Reversible = body[4] == 1,
            Precincts = precincts,
        };
    }

    private static bool ParseComponentIndex(ReadOnlySpan<byte> body, JpxImageSize size, JpxReporter reporter, out int component, out int used)
    {
        used = size.Components.Length < 257 ? 1 : 2;
        component = 0;
        if (body.Length <= used)
        {
            Invalid(reporter, "COC or QCC");
            return false;
        }

        component = used == 1 ? body[0] : BinaryPrimitives.ReadUInt16BigEndian(body);
        if (component >= size.Components.Length)
        {
            Invalid(reporter, "COC or QCC");
            return false;
        }

        return true;
    }

    private static JpxQuantization? ParseQuantization(ReadOnlySpan<byte> body, JpxReporter reporter)
    {
        if (body.Length < 2)
        {
            Invalid(reporter, "QCD or QCC");
            return null;
        }

        int style = body[0] & 0x1F;
        int guard = body[0] >> 5;
        ReadOnlySpan<byte> entries = body[1..];
        int count = style == 0 ? entries.Length : entries.Length / 2;
        if (style > 2 || count == 0)
        {
            Invalid(reporter, "QCD or QCC");
            return null;
        }

        int[] exponents = new int[count];
        int[] mantissas = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (style == 0)
            {
                exponents[i] = entries[i] >> 3;
            }
            else
            {
                int value = BinaryPrimitives.ReadUInt16BigEndian(entries[(2 * i)..]);
                exponents[i] = value >> 11;
                mantissas[i] = value & 0x7FF;
            }
        }

        return new JpxQuantization { Style = style, GuardBits = guard, Exponents = exponents, Mantissas = mantissas };
    }

    private static void Invalid(JpxReporter reporter, string segment) => reporter.Report(
        DiagnosticCodes.JpxMarkerSegmentInvalid,
        DiagnosticSeverity.Warning,
        $"A JPEG 2000 {segment} marker segment holds values outside Annex A's ranges; what can be used is kept.");

    private static long U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    /// <summary>Walks the boxes of a JP2 file (I.4) and returns the contents of the first box of <paramref name="type"/>.</summary>
    private static (int Offset, int Length)? FindBox(ReadOnlySpan<byte> data, uint type, JpxReporter reporter)
    {
        long position = 0;
        while (position + 8 <= data.Length)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(data[(int)position..]);
            uint boxType = BinaryPrimitives.ReadUInt32BigEndian(data[((int)position + 4)..]);
            int header = 8;
            if (length == 1)
            {
                if (position + 16 > data.Length)
                {
                    break;
                }

                ulong extended = BinaryPrimitives.ReadUInt64BigEndian(data[((int)position + 8)..]);
                length = extended > long.MaxValue ? long.MaxValue : (long)extended;
                header = 16;
            }
            else if (length == 0)
            {
                length = data.Length - position;
            }

            if (length < header)
            {
                reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, $"A JPEG 2000 box has the impossible length {length}; the boxes after it are not read.");
                break;
            }

            long end = length > data.Length - position ? data.Length + 1L : position + length;
            if (end > data.Length)
            {
                if (boxType != type)
                {
                    break;
                }

                end = data.Length;
            }

            if (boxType == type)
            {
                return ((int)position + header, (int)(end - position - header));
            }

            position = end;
        }

        return null;
    }

    /// <summary>Finds the first SOC followed by SIZ.</summary>
    private static (int Offset, int Length)? Scan(ReadOnlySpan<byte> data)
    {
        int found = data.IndexOf((ReadOnlySpan<byte>)[0xFF, 0x4F, 0xFF, 0x51]);
        return found < 0 ? null : (found, data.Length - found);
    }
}
