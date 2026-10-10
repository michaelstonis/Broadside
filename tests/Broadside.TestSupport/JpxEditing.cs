using System.Buffers.Binary;

namespace Broadside.TestSupport;

/// <summary>
/// Builds JPEG 2000 test inputs from the vectors of <see cref="JpxSamples"/> by rearranging bytes the standard lets an encoder place
/// elsewhere, so the expected samples stay the vector's: JP2 wrappers with chosen boxes (ITU-T T.800 Annex I), packet headers moved
/// into PPT or PPM marker segments (A.7.4, A.7.5), tile-parts reordered or with a broken length (A.4.2).
/// </summary>
public static class JpxEditing
{
    /// <summary>A box: LBox, TBox, contents (T.800 I.4).</summary>
    public static byte[] Box(string type, params byte[][] contents)
    {
        int length = 8 + contents.Sum(c => c.Length);
        byte[] box = new byte[length];
        BinaryPrimitives.WriteInt32BigEndian(box, length);
        for (int i = 0; i < 4; i++)
        {
            box[4 + i] = (byte)type[i];
        }

        int offset = 8;
        foreach (byte[] content in contents)
        {
            content.CopyTo(box, offset);
            offset += content.Length;
        }

        return box;
    }

    /// <summary>A JP2 file: signature, file type, a JP2 header box holding an image header and <paramref name="headerBoxes"/>, then the codestream (I.5).</summary>
    public static byte[] Jp2(byte[] codestream, params byte[][] headerBoxes)
    {
        byte[] siz = codestream.AsSpan(4).ToArray();
        int components = BinaryPrimitives.ReadUInt16BigEndian(siz.AsSpan(2 + 34));
        byte[] ihdr = new byte[14];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, BinaryPrimitives.ReadUInt32BigEndian(siz.AsSpan(2 + 6)));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), BinaryPrimitives.ReadUInt32BigEndian(siz.AsSpan(2 + 2)));
        BinaryPrimitives.WriteUInt16BigEndian(ihdr.AsSpan(8), (ushort)components);
        ihdr[10] = siz[2 + 36];
        ihdr[11] = 7;
        return
        [
            .. Box("jP  ", [0x0D, 0x0A, 0x87, 0x0A]),
            .. Box("ftyp", [(byte)'j', (byte)'p', (byte)'2', (byte)' ', 0, 0, 0, 0, (byte)'j', (byte)'p', (byte)'2', (byte)' ']),
            .. Box("jp2h", [Box("ihdr", ihdr), .. headerBoxes]),
            .. Box("jp2c", codestream),
        ];
    }

    /// <summary>An enumerated Colour Specification box (I.5.3.3): METH 1, PREC, APPROX, EnumCS.</summary>
    public static byte[] Color(int enumerated, int precedence = 0, int approximation = 0)
    {
        byte[] body = [1, (byte)precedence, (byte)approximation, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(3), enumerated);
        return Box("colr", body);
    }

    /// <summary>A restricted ICC Colour Specification box (I.5.3.3): METH 2, PREC, APPROX, the profile.</summary>
    public static byte[] Color(byte[] profile, int precedence = 0, int approximation = 0) => Box("colr", [2, (byte)precedence, (byte)approximation], profile);

    /// <summary>A Palette box (I.5.3.4) of 8-bit columns: <paramref name="entries"/>[i][j] is column j of entry i.</summary>
    public static byte[] Palette(int[][] entries)
    {
        int columns = entries[0].Length;
        var body = new List<byte> { (byte)(entries.Length >> 8), (byte)entries.Length, (byte)columns };
        body.AddRange(Enumerable.Repeat((byte)7, columns));
        foreach (int[] entry in entries)
        {
            body.AddRange(entry.Select(v => (byte)v));
        }

        return Box("pclr", [.. body]);
    }

    /// <summary>A Component Mapping box (I.5.3.5) of (component, MTYP, PCOL) entries.</summary>
    public static byte[] Mapping(params (int Component, int Type, int Column)[] channels) =>
        Box("cmap", [.. channels.SelectMany(c => new[] { (byte)(c.Component >> 8), (byte)c.Component, (byte)c.Type, (byte)c.Column })]);

    /// <summary>A Channel Definition box (I.5.3.6) of (Cn, Typ, Asoc) entries.</summary>
    public static byte[] Definitions(params (int Channel, int Type, int Association)[] channels)
    {
        var body = new List<byte> { (byte)(channels.Length >> 8), (byte)channels.Length };
        foreach ((int channel, int type, int association) in channels)
        {
            body.AddRange([(byte)(channel >> 8), (byte)channel, (byte)(type >> 8), (byte)type, (byte)(association >> 8), (byte)association]);
        }

        return Box("cdef", [.. body]);
    }

    /// <summary>
    /// A 128-byte ICC profile header (ICC.1 7.2) for a display profile of <paramref name="dataColorSpace"/> ("GRAY", "RGB ", "CMYK")
    /// with a PCS of XYZ, version 4.3: enough for a reader that only reads the header.
    /// </summary>
    public static byte[] IccHeader(string dataColorSpace)
    {
        byte[] profile = new byte[128];
        BinaryPrimitives.WriteInt32BigEndian(profile, profile.Length);
        profile[8] = 4;
        profile[9] = 0x30;
        Ascii(profile, 12, "mntr");
        Ascii(profile, 16, dataColorSpace);
        Ascii(profile, 20, "XYZ ");
        Ascii(profile, 36, "acsp");
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(68), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(72), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(76), 0x0000D32D);
        return profile;

        static void Ascii(byte[] target, int offset, string text)
        {
            for (int i = 0; i < 4; i++)
            {
                target[offset + i] = (byte)text[i];
            }
        }
    }

    /// <summary>
    /// Moves the packet headers of a one-tile codestream written with SOP and EPH markers into a PPT marker segment of its tile-part
    /// header (A.7.5): each header runs from after its SOP segment to its EPH marker inclusive; the bodies stay, each after its SOP.
    /// </summary>
    public static byte[] WithTilePacketHeaders(byte[] codestream)
    {
        (byte[] main, byte[] tileHeader, byte[] headers, byte[] bodies) = SplitPackets(codestream);
        byte[] ppt = Segment(0xFF61, [0, .. headers]);
        return Assemble(main, [.. tileHeader, .. ppt], bodies);
    }

    /// <summary>As <see cref="WithTilePacketHeaders"/>, with the headers in a PPM marker segment of the main header (A.7.4): Zppm, then Nppm and the headers.</summary>
    public static byte[] WithMainPacketHeaders(byte[] codestream)
    {
        (byte[] main, byte[] tileHeader, byte[] headers, byte[] bodies) = SplitPackets(codestream);
        byte[] count = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(count, headers.Length);
        byte[] ppm = Segment(0xFF60, [0, .. count, .. headers]);
        return Assemble([.. main, .. ppm], tileHeader, bodies);
    }

    /// <summary>The tile-parts of a codestream as (start, length) of each SOT ... data range with their Isot and TPsot, in codestream order.</summary>
    public static List<(int Start, int Length, int Tile, int Part)> TileParts(byte[] codestream)
    {
        var parts = new List<(int, int, int, int)>();
        int position = FirstSot(codestream);
        while (position + 12 <= codestream.Length && BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position)) == 0xFF90)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(codestream.AsSpan(position + 6));
            parts.Add((position, length, BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position + 4)), codestream[position + 10]));
            position += length;
        }

        return parts;
    }

    /// <summary>The codestream with its tile-parts in the order <paramref name="order"/> (indices into <see cref="TileParts"/>).</summary>
    public static byte[] ReorderTileParts(byte[] codestream, params int[] order)
    {
        List<(int Start, int Length, int Tile, int Part)> parts = TileParts(codestream);
        int first = parts[0].Start;
        (int lastStart, int lastLength, _, _) = parts[^1];
        return
        [
            .. codestream.AsSpan(0, first),
            .. order.SelectMany(i => codestream.AsSpan(parts[i].Start, parts[i].Length).ToArray()),
            .. codestream.AsSpan(lastStart + lastLength),
        ];
    }

    /// <summary>The codestream with tile-part <paramref name="index"/>'s Psot set to <paramref name="psot"/>.</summary>
    public static byte[] WithPsot(byte[] codestream, int index, uint psot)
    {
        byte[] copy = [.. codestream];
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(TileParts(codestream)[index].Start + 6), psot);
        return copy;
    }

    private static int FirstSot(byte[] codestream)
    {
        int position = 2;
        while (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position)) != 0xFF90)
        {
            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(position + 2));
        }

        return position;
    }

    private static (byte[] Main, byte[] TileHeader, byte[] Headers, byte[] Bodies) SplitPackets(byte[] codestream)
    {
        int sot = FirstSot(codestream);
        int sod = sot + 12;
        while (BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(sod)) != 0xFF93)
        {
            sod += 2 + BinaryPrimitives.ReadUInt16BigEndian(codestream.AsSpan(sod + 2));
        }

        int end = codestream.Length - 2;
        var headers = new List<byte>();
        var bodies = new List<byte>();
        int position = sod + 2;
        while (position < end)
        {
            if (codestream[position] != 0xFF || codestream[position + 1] != 0x91)
            {
                throw new ArgumentException("Every packet must start with an SOP marker segment.", nameof(codestream));
            }

            bodies.AddRange(codestream.AsSpan(position, 6).ToArray());
            int eph = codestream.AsSpan(position + 6).IndexOf((ReadOnlySpan<byte>)[0xFF, 0x92]) + position + 6;
            headers.AddRange(codestream.AsSpan(position + 6, eph + 2 - position - 6).ToArray());
            int next = codestream.AsSpan(eph + 2, end - eph - 2).IndexOf((ReadOnlySpan<byte>)[0xFF, 0x91]);
            int bodyEnd = next < 0 ? end : eph + 2 + next;
            bodies.AddRange(codestream.AsSpan(eph + 2, bodyEnd - eph - 2).ToArray());
            position = bodyEnd;
        }

        return (codestream[..sot], codestream[(sot + 12)..sod], [.. headers], [.. bodies]);
    }

    private static byte[] Assemble(byte[] main, byte[] tileHeader, byte[] bodies)
    {
        byte[] sot = [0xFF, 0x90, 0, 10, 0, 0, 0, 0, 0, 0, 0, 1];
        BinaryPrimitives.WriteInt32BigEndian(sot.AsSpan(6), 12 + tileHeader.Length + 2 + bodies.Length);
        return [.. main, .. sot, .. tileHeader, 0xFF, 0x93, .. bodies, 0xFF, 0xD9];
    }

    private static byte[] Segment(ushort marker, byte[] body)
    {
        byte[] segment = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(segment, marker);
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(body.Length + 2));
        body.CopyTo(segment, 4);
        return segment;
    }
}
