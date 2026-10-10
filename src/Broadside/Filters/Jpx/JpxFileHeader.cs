using Broadside.Filters.Codecs;
using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>One Colour Specification box.</summary>
/// <remarks>ITU-T T.800 I.5.3.3, Tables I.9 and I.10; ISO/IEC 15444-2 adds METH 3 and 4 and more enumerated spaces.</remarks>
internal sealed class JpxColorSpecification
{
    /// <summary>The method METH: 1 enumerated, 2 restricted ICC (Part 2: 3 any ICC, 4 vendor).</summary>
    public int Method { get; init; }

    /// <summary>The precedence PREC (signed).</summary>
    public int Precedence { get; init; }

    /// <summary>The approximation APPROX (Part 2: 1 accurate to 4 poor, 0 unknown).</summary>
    public int Approximation { get; init; }

    /// <summary>The enumerated colour space EnumCS (METH 1), else -1.</summary>
    public long Enumerated { get; init; } = -1;

    /// <summary>The parameters that follow EnumCS (CIELab: RL, OL, RA, OA, RB, OB, IL), or empty.</summary>
    public uint[] Parameters { get; init; } = [];

    /// <summary>The ICC profile (METH 2 or 3), or <see langword="null"/>.</summary>
    public byte[]? Profile { get; init; }
}

/// <summary>The Palette box: NE entries of NPC columns.</summary>
/// <remarks>ITU-T T.800 I.5.3.4, Table I.13.</remarks>
internal sealed class JpxPalette
{
    public int Entries { get; init; }

    /// <summary>The precision of each column, 1 to 38.</summary>
    public required int[] Depths { get; init; }

    /// <summary>Whether each column's values are signed.</summary>
    public required bool[] Signed { get; init; }

    /// <summary>The values, entry-major: entry * columns + column.</summary>
    public required long[] Values { get; init; }

    public int Columns => Depths.Length;
}

/// <summary>One channel of the Component Mapping box.</summary>
/// <param name="Component">CMP, the codestream component.</param>
/// <param name="Type">MTYP: 0 direct use, 1 palette mapping.</param>
/// <param name="Column">PCOL, the palette column for MTYP 1.</param>
/// <remarks>ITU-T T.800 I.5.3.5, Tables I.14 and I.15.</remarks>
internal readonly record struct JpxComponentMapping(int Component, int Type, int Column);

/// <summary>One entry of the Channel Definition box.</summary>
/// <param name="Channel">Cn, the channel.</param>
/// <param name="Type">Typ: 0 colour, 1 opacity, 2 premultiplied opacity, 65535 unspecified.</param>
/// <param name="Association">Asoc: 0 the whole image, 1 to n the colour, 65535 none.</param>
/// <remarks>ITU-T T.800 I.5.3.6, Tables I.16 to I.18.</remarks>
internal readonly record struct JpxChannelDefinition(int Channel, int Type, int Association);

/// <summary>The JP2 Header box's colour and channel boxes, as a PDF reader needs them.</summary>
/// <remarks>
/// ITU-T T.800 I.5.3 (Image Header, Bits Per Component, Colour Specification, Palette, Component Mapping, Channel Definition);
/// other boxes (resolution, XML, UUID, unknown) are skipped (I.8). The Image Header is not needed: SIZ wins (I.5.3.1).
/// </remarks>
internal sealed class JpxFileHeader
{
    private const uint HeaderBox = 0x6A703268; // 'jp2h'
    private const uint ColorBox = 0x636F6C72; // 'colr'
    private const uint PaletteBox = 0x70636C72; // 'pclr'
    private const uint MappingBox = 0x636D6170; // 'cmap'
    private const uint DefinitionBox = 0x63646566; // 'cdef'

    public static JpxFileHeader Empty { get; } = new();

    public List<JpxColorSpecification> Colors { get; } = [];

    public JpxPalette? Palette { get; private set; }

    public JpxComponentMapping[]? Mapping { get; private set; }

    public JpxChannelDefinition[]? Channels { get; private set; }

    /// <summary>Reads the JP2 Header box of a JP2/JPX file, or returns <see cref="Empty"/> for a raw codestream or a file without one.</summary>
    public static JpxFileHeader Read(ReadOnlySpan<byte> data, CodecReporter reporter)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != 0x6A502020)
        {
            return Empty;
        }

        foreach ((uint type, int offset, int length) in Boxes(data, 0, data.Length, reporter))
        {
            if (type != HeaderBox)
            {
                continue;
            }

            var header = new JpxFileHeader();
            foreach ((uint inner, int start, int size) in Boxes(data, offset, offset + length, reporter))
            {
                ReadOnlySpan<byte> body = data.Slice(start, size);
                switch (inner)
                {
                    case ColorBox when ReadColor(body) is { } color:
                        header.Colors.Add(color);
                        break;
                    case PaletteBox:
                        header.Palette ??= ReadPalette(body, reporter);
                        break;
                    case MappingBox:
                        header.Mapping ??= ReadMapping(body);
                        break;
                    case DefinitionBox:
                        header.Channels ??= ReadDefinitions(body);
                        break;
                    case ColorBox:
                        reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "A JPEG 2000 colour specification box is too short; it is ignored.");
                        break;
                }
            }

            return header;
        }

        return Empty;
    }

    /// <summary>
    /// The boxes in [<paramref name="start"/>, <paramref name="end"/>) as (type, content offset, content length): LBox 1 reads XLBox,
    /// LBox 0 runs to the end of the enclosing box (I.4); a box overrunning its parent is cut there.
    /// </summary>
    public static List<(uint Type, int Offset, int Length)> Boxes(ReadOnlySpan<byte> data, int start, int end, CodecReporter reporter)
    {
        var boxes = new List<(uint, int, int)>();
        long position = start;
        while (position + 8 <= end)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(data[(int)position..]);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(data[((int)position + 4)..]);
            int header = 8;
            if (length == 1)
            {
                if (position + 16 > end)
                {
                    break;
                }

                ulong extended = BinaryPrimitives.ReadUInt64BigEndian(data[((int)position + 8)..]);
                length = extended > long.MaxValue ? long.MaxValue : (long)extended;
                header = 16;
            }
            else if (length == 0)
            {
                length = end - position;
            }

            if (length < header)
            {
                reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, $"A JPEG 2000 box has the impossible length {length}; the boxes after it are not read.");
                break;
            }

            long boxEnd = length > end - position ? end : position + length;
            if (length > end - position)
            {
                reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "A JPEG 2000 box runs past the end of the box or file holding it; it is cut there.");
            }

            boxes.Add((type, (int)position + header, (int)(boxEnd - position - header)));
            position = boxEnd;
        }

        return boxes;
    }

    private static JpxColorSpecification? ReadColor(ReadOnlySpan<byte> body)
    {
        if (body.Length < 3)
        {
            return null;
        }

        int method = body[0];
        int precedence = (sbyte)body[1];
        int approximation = body[2];
        if (method == 1)
        {
            if (body.Length < 7)
            {
                return null;
            }

            int count = (body.Length - 7) / 4;
            uint[] parameters = new uint[count];
            for (int i = 0; i < count; i++)
            {
                parameters[i] = BinaryPrimitives.ReadUInt32BigEndian(body[(7 + (4 * i))..]);
            }

            return new JpxColorSpecification
            {
                Method = method,
                Precedence = precedence,
                Approximation = approximation,
                Enumerated = BinaryPrimitives.ReadUInt32BigEndian(body[3..]),
                Parameters = parameters,
            };
        }

        return new JpxColorSpecification
        {
            Method = method,
            Precedence = precedence,
            Approximation = approximation,
            Profile = method is 2 or 3 ? body[3..].ToArray() : null,
        };
    }

    private static JpxPalette? ReadPalette(ReadOnlySpan<byte> body, CodecReporter reporter)
    {
        if (body.Length < 3)
        {
            return InvalidPalette(reporter);
        }

        int entries = BinaryPrimitives.ReadUInt16BigEndian(body);
        int columns = body[2];
        if (entries is < 1 or > 1024 || columns < 1 || body.Length < 3 + columns)
        {
            return InvalidPalette(reporter);
        }

        int[] depths = new int[columns];
        bool[] signed = new bool[columns];
        int rowBytes = 0;
        for (int j = 0; j < columns; j++)
        {
            depths[j] = (body[3 + j] & 0x7F) + 1;
            signed[j] = (body[3 + j] & 0x80) != 0;
            if (depths[j] > 38)
            {
                return InvalidPalette(reporter);
            }

            rowBytes += (depths[j] + 7) / 8;
        }

        ReadOnlySpan<byte> values = body[(3 + columns)..];
        int available = Math.Min(entries, values.Length / rowBytes);
        if (available < entries)
        {
            reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "The JPEG 2000 palette box holds fewer entries than it declares; the missing ones are zero.");
        }

        long[] table = new long[entries * columns];
        int position = 0;
        for (int i = 0; i < available; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                int bytes = (depths[j] + 7) / 8;
                long value = 0;
                for (int b = 0; b < bytes; b++)
                {
                    value = (value << 8) | values[position++];
                }

                value &= (1L << depths[j]) - 1;
                if (signed[j] && (value & (1L << (depths[j] - 1))) != 0)
                {
                    value -= 1L << depths[j];
                }

                table[(i * columns) + j] = value;
            }
        }

        return new JpxPalette { Entries = entries, Depths = depths, Signed = signed, Values = table };
    }

    private static JpxPalette? InvalidPalette(CodecReporter reporter)
    {
        reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "The JPEG 2000 palette box is outside Table I.13's ranges; it is ignored.");
        return null;
    }

    private static JpxComponentMapping[] ReadMapping(ReadOnlySpan<byte> body)
    {
        var mapping = new JpxComponentMapping[body.Length / 4];
        for (int i = 0; i < mapping.Length; i++)
        {
            mapping[i] = new JpxComponentMapping(BinaryPrimitives.ReadUInt16BigEndian(body[(4 * i)..]), body[(4 * i) + 2], body[(4 * i) + 3]);
        }

        return mapping;
    }

    private static JpxChannelDefinition[] ReadDefinitions(ReadOnlySpan<byte> body)
    {
        int count = body.Length >= 2 ? Math.Min(BinaryPrimitives.ReadUInt16BigEndian(body), (body.Length - 2) / 6) : 0;
        var channels = new JpxChannelDefinition[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = body[(2 + (6 * i))..];
            channels[i] = new JpxChannelDefinition(
                BinaryPrimitives.ReadUInt16BigEndian(entry),
                BinaryPrimitives.ReadUInt16BigEndian(entry[2..]),
                BinaryPrimitives.ReadUInt16BigEndian(entry[4..]));
        }

        return channels;
    }
}
