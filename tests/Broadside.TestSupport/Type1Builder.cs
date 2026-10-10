using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Builds Type 1 font programs in memory for tests, fuzz seeds and benchmarks: clear text, eexec-encrypted private dictionary with
/// encrypted charstrings, and the fixed portion (Adobe Type 1 Font Format chapters 2, 6 and 7). Charstrings are written as text
/// (<c>"50 800 hsbw 0 0 rmoveto … endchar"</c>) and encoded with the number and command encodings of §6.2-6.5.
/// </summary>
public sealed class Type1Builder
{
    private static readonly Dictionary<string, byte[]> Commands = new(StringComparer.Ordinal)
    {
        ["hstem"] = [1],
        ["vstem"] = [3],
        ["vmoveto"] = [4],
        ["rlineto"] = [5],
        ["hlineto"] = [6],
        ["vlineto"] = [7],
        ["rrcurveto"] = [8],
        ["closepath"] = [9],
        ["callsubr"] = [10],
        ["return"] = [11],
        ["hsbw"] = [13],
        ["endchar"] = [14],
        ["rmoveto"] = [21],
        ["hmoveto"] = [22],
        ["vhcurveto"] = [30],
        ["hvcurveto"] = [31],
        ["dotsection"] = [12, 0],
        ["vstem3"] = [12, 1],
        ["hstem3"] = [12, 2],
        ["seac"] = [12, 6],
        ["sbw"] = [12, 7],
        ["div"] = [12, 12],
        ["callothersubr"] = [12, 16],
        ["pop"] = [12, 17],
        ["setcurrentpoint"] = [12, 33],
    };

    private static readonly SearchValues<byte> HexDigits = SearchValues.Create("0123456789abcdefABCDEF"u8);

    /// <summary>Gets or sets the <c>/FontName</c>.</summary>
    public string FontName { get; set; } = "BroadsideTest";

    /// <summary>Gets or sets the value written after <c>/FontMatrix</c>.</summary>
    public string FontMatrix { get; set; } = "[0.001 0 0 0.001 0 0] readonly def";

    /// <summary>Gets or sets the value written after <c>/Encoding</c>.</summary>
    public string Encoding { get; set; } = "StandardEncoding def";

    /// <summary>Gets or sets text added to the clear text before <c>currentfile eexec</c>.</summary>
    public string ClearTextExtra { get; set; } = string.Empty;

    /// <summary>Gets or sets text added to the private dictionary before <c>/Subrs</c>.</summary>
    public string PrivateExtra { get; set; } = string.Empty;

    /// <summary>Gets or sets text added after the CharStrings dictionary.</summary>
    public string Trailer { get; set; } = string.Empty;

    /// <summary>Gets or sets the private dictionary's <c>lenIV</c>; <see langword="null"/> to leave it out (default 4); −1 for plaintext charstrings.</summary>
    public int? LenIV { get; set; } = 4;

    /// <summary>Gets or sets the <c>RD</c>/<c>ND</c> procedure names used in entries.</summary>
    public (string RD, string ND, string NP) Procedures { get; set; } = ("RD", "ND", "NP");

    /// <summary>Gets the subroutines (plaintext charstrings), by index; <see langword="null"/> leaves an index out.</summary>
    public List<byte[]?> Subrs { get; } = [];

    /// <summary>Gets the glyphs (name, plaintext charstring), in CharStrings order.</summary>
    public List<(string Name, byte[] Charstring)> Glyphs { get; } = [];

    /// <summary>Adds a glyph from charstring text.</summary>
    public Type1Builder Glyph(string name, string charstring) => Glyph(name, Charstring(charstring));

    /// <summary>Adds a glyph from plaintext charstring bytes.</summary>
    public Type1Builder Glyph(string name, byte[] charstring)
    {
        Glyphs.Add((name, charstring));
        return this;
    }

    /// <summary>Adds a subroutine from charstring text.</summary>
    public Type1Builder Subr(string charstring)
    {
        Subrs.Add(Charstring(charstring));
        return this;
    }

    /// <summary>Encodes charstring text: integers (§6.2) and command names (§6.4, Appendix 2); <c>raw:XX</c> adds a byte as is.</summary>
    public static byte[] Charstring(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new List<byte>();
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Commands.TryGetValue(token, out byte[]? command))
            {
                bytes.AddRange(command);
            }
            else if (token.StartsWith("raw:", StringComparison.Ordinal))
            {
                bytes.Add(byte.Parse(token.AsSpan(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else
            {
                bytes.AddRange(Number(int.Parse(token, CultureInfo.InvariantCulture)));
            }
        }

        return [.. bytes];
    }

    /// <summary>Type 1 Font Format §6.2: the charstring encoding of an integer.</summary>
    public static byte[] Number(int value)
    {
        if (value is >= -107 and <= 107)
        {
            return [(byte)(value + 139)];
        }

        if (value is >= 108 and <= 1131)
        {
            value -= 108;
            return [(byte)((value >> 8) + 247), (byte)value];
        }

        if (value is >= -1131 and <= -108)
        {
            value = -value - 108;
            return [(byte)((value >> 8) + 251), (byte)value];
        }

        byte[] bytes = new byte[5];
        bytes[0] = 255;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), value);
        return bytes;
    }

    /// <summary>Type 1 Font Format §7.1: encryption with initial key <paramref name="key"/> (55665 eexec, 4330 charstrings).</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plain, ushort key)
    {
        byte[] cipher = new byte[plain.Length];
        int r = key;
        for (int index = 0; index < plain.Length; index++)
        {
            byte c = (byte)(plain[index] ^ (r >> 8));
            cipher[index] = c;
            r = (((c + r) * 52845) + 22719) & 0xFFFF;
        }

        return cipher;
    }

    /// <summary>The clear text, ending with <c>currentfile eexec</c> and a line feed.</summary>
    public byte[] ClearText() => Latin1(
        $"%!PS-AdobeFont-1.0: {FontName} 001.000\n" +
        "10 dict begin\n" +
        $"/FontName /{FontName} def\n" +
        "/PaintType 0 def\n/FontType 1 def\n" +
        $"/FontMatrix {FontMatrix}\n" +
        "/FontBBox {0 0 1000 1000} readonly def\n" +
        $"/Encoding {Encoding}\n" +
        ClearTextExtra +
        "currentfile eexec\n");

    /// <summary>The private dictionary and CharStrings, before eexec encryption.</summary>
    public byte[] PrivateText()
    {
        var text = new MemoryStream();
        (string rd, string nd, string np) = Procedures;
        Write(text, "dup /Private 8 dict dup begin\n");
        Write(text, $"/{rd}{{string currentfile exch readstring pop}}executeonly def\n/{nd}{{noaccess def}}executeonly def\n/{np}{{noaccess put}}executeonly def\n");
        if (LenIV is { } lenIV)
        {
            Write(text, string.Create(CultureInfo.InvariantCulture, $"/lenIV {lenIV} def\n"));
        }

        Write(text, PrivateExtra);
        Write(text, string.Create(CultureInfo.InvariantCulture, $"/Subrs {Subrs.Count} array\n"));
        for (int index = 0; index < Subrs.Count; index++)
        {
            if (Subrs[index] is { } subr)
            {
                byte[] data = EncryptCharstring(subr);
                Write(text, string.Create(CultureInfo.InvariantCulture, $"dup {index} {data.Length} {rd} "));
                text.Write(data);
                Write(text, $" {np}\n");
            }
        }

        Write(text, string.Create(CultureInfo.InvariantCulture, $"{nd}\n2 index /CharStrings {Glyphs.Count} dict dup begin\n"));
        foreach ((string name, byte[] charstring) in Glyphs)
        {
            byte[] data = EncryptCharstring(charstring);
            Write(text, string.Create(CultureInfo.InvariantCulture, $"/{name} {data.Length} {rd} "));
            text.Write(data);
            Write(text, $" {nd}\n");
        }

        Write(text, "end\nend\n" + Trailer + "readonly put\nnoaccess put\ndup/FontName get exch definefont pop\nmark currentfile closefile\n");
        return text.ToArray();
    }

    /// <summary>The eexec-encrypted private text in binary form, its four leading bytes chosen as §7.2 requires.</summary>
    public byte[] EncryptedPortion()
    {
        byte[] plain = PrivateText();
        for (int seed = 0; ; seed++)
        {
            byte[] prefixed = [(byte)(0x80 + seed), (byte)(seed * 7), (byte)(seed * 13), (byte)(seed * 31), .. plain];
            byte[] cipher = Encrypt(prefixed, 55665);
            bool white = cipher[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
            bool allHex = !cipher.AsSpan(0, 4).ContainsAnyExcept(HexDigits);
            if (!white && !allHex)
            {
                return cipher;
            }
        }
    }

    /// <summary>The fixed portion: 512 zeros in eight lines and <c>cleartomark</c>.</summary>
    public static byte[] FixedPortion() => Latin1(string.Concat(Enumerable.Repeat(new string('0', 64) + "\n", 8)) + "cleartomark\n");

    /// <summary>The program in the layout ISO 32000-2 §9.9 describes, and its three lengths (Table 125).</summary>
    public (byte[] Program, int Length1, int Length2, int Length3) Build()
    {
        byte[] clear = ClearText();
        byte[] cipher = EncryptedPortion();
        byte[] fixedPortion = FixedPortion();
        return ([.. clear, .. cipher, .. fixedPortion], clear.Length, cipher.Length, fixedPortion.Length);
    }

    /// <summary>The program as a PFB file (TN 5040 §3.3): ASCII, binary and ASCII segments and the end-of-file marker.</summary>
    public byte[] BuildPfb()
    {
        var pfb = new MemoryStream();
        Segment(pfb, 1, ClearText());
        Segment(pfb, 2, EncryptedPortion());
        Segment(pfb, 1, FixedPortion());
        pfb.Write([0x80, 3]);
        return pfb.ToArray();
    }

    /// <summary>The program with its eexec portion as hexadecimal text (a PFA file), 64 digits per line.</summary>
    public byte[] BuildPfa()
    {
        string hex = Convert.ToHexString(EncryptedPortion()).ToLowerInvariant();
        var lines = new StringBuilder();
        for (int index = 0; index < hex.Length; index += 64)
        {
            lines.Append(hex.AsSpan(index, Math.Min(64, hex.Length - index))).Append('\n');
        }

        return [.. ClearText(), .. Latin1(lines.ToString()), .. FixedPortion()];
    }

    private static void Segment(Stream stream, byte type, byte[] data)
    {
        Span<byte> header = [0x80, type, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], data.Length);
        stream.Write(header);
        stream.Write(data);
    }

    private byte[] EncryptCharstring(byte[] charstring) =>
        LenIV is -1 ? charstring : Encrypt([.. new byte[Math.Max(LenIV ?? 4, 0)], .. charstring], 4330);

    private static void Write(Stream stream, string text) => stream.Write(Latin1(text));

    private static byte[] Latin1(string text) => System.Text.Encoding.Latin1.GetBytes(text);
}
