using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Builds CFF programs (and OpenType wrappers around them) in memory for tests of the font program contract: glyphs, subroutines,
/// charset, encoding and dictionaries are given explicitly so a test can make any of them malformed. Every offset in the Top DICT
/// is written in the 5-byte form so the layout is computed in one pass. <see cref="T2"/> assembles Type 2 charstrings.
/// </summary>
/// <remarks>Adobe Technical Note #5176 (CFF) and #5177 (Type 2 charstrings); OpenType "Organization of an OpenType font".</remarks>
public sealed class CffBuilder
{
    private static readonly Dictionary<string, byte[]> Operators = new(StringComparer.Ordinal)
    {
        ["hstem"] = [1],
        ["vstem"] = [3],
        ["vmoveto"] = [4],
        ["rlineto"] = [5],
        ["hlineto"] = [6],
        ["vlineto"] = [7],
        ["rrcurveto"] = [8],
        ["callsubr"] = [10],
        ["return"] = [11],
        ["endchar"] = [14],
        ["hstemhm"] = [18],
        ["hintmask"] = [19],
        ["cntrmask"] = [20],
        ["rmoveto"] = [21],
        ["hmoveto"] = [22],
        ["vstemhm"] = [23],
        ["rcurveline"] = [24],
        ["rlinecurve"] = [25],
        ["vvcurveto"] = [26],
        ["hhcurveto"] = [27],
        ["callgsubr"] = [29],
        ["vhcurveto"] = [30],
        ["hvcurveto"] = [31],
        ["dotsection"] = [12, 0],
        ["and"] = [12, 3],
        ["or"] = [12, 4],
        ["not"] = [12, 5],
        ["abs"] = [12, 9],
        ["add"] = [12, 10],
        ["sub"] = [12, 11],
        ["div"] = [12, 12],
        ["neg"] = [12, 14],
        ["eq"] = [12, 15],
        ["drop"] = [12, 18],
        ["put"] = [12, 20],
        ["get"] = [12, 21],
        ["ifelse"] = [12, 22],
        ["random"] = [12, 23],
        ["mul"] = [12, 24],
        ["sqrt"] = [12, 26],
        ["dup"] = [12, 27],
        ["exch"] = [12, 28],
        ["index"] = [12, 29],
        ["roll"] = [12, 30],
        ["hflex"] = [12, 34],
        ["flex"] = [12, 35],
        ["hflex1"] = [12, 36],
        ["flex1"] = [12, 37],
    };

    /// <summary>Gets or sets the font name (the Name INDEX entry).</summary>
    public string Name { get; set; } = "BroadsideTest";

    /// <summary>Gets the glyphs in GID order (name and charstring); add <c>.notdef</c> first. Default: <c>.notdef</c> as <c>endchar</c>.</summary>
    public List<(string Name, byte[] CharString)> Glyphs { get; } = [];

    /// <summary>Gets the global subroutines.</summary>
    public List<byte[]> GlobalSubrs { get; } = [];

    /// <summary>Gets the local subroutines (written only when there is at least one).</summary>
    public List<byte[]> LocalSubrs { get; } = [];

    /// <summary>Gets or sets the Private DICT's defaultWidthX (written when not 0).</summary>
    public int DefaultWidthX { get; set; }

    /// <summary>Gets or sets the Private DICT's nominalWidthX (written when not 0).</summary>
    public int NominalWidthX { get; set; }

    /// <summary>Gets or sets the Top DICT FontMatrix, or <see langword="null"/> to leave it out.</summary>
    public double[]? FontMatrix { get; set; }

    /// <summary>Gets or sets the Top DICT Encoding operand when predefined (0 Standard, 1 Expert); <see langword="null"/> for <see cref="Encoding"/>.</summary>
    public int? PredefinedEncoding { get; set; } = 0;

    /// <summary>Gets or sets a custom encoding's bytes (format byte first); used when <see cref="PredefinedEncoding"/> is <see langword="null"/>.</summary>
    public byte[]? Encoding { get; set; }

    /// <summary>Gets or sets the Top DICT charset operand when predefined (0, 1, 2); <see langword="null"/> for a custom charset.</summary>
    public int? PredefinedCharset { get; set; }

    /// <summary>Gets or sets a custom charset's bytes; <see langword="null"/> writes format 0 from the glyph names.</summary>
    public byte[]? Charset { get; set; }

    /// <summary>Gets or sets extra Top DICT bytes, written before the offsets (such as a ROS or CharstringType).</summary>
    public byte[] TopDictPrefix { get; set; } = [];

    /// <summary>Gets or sets extra Private DICT bytes, written first.</summary>
    public byte[] PrivateExtra { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the Top DICT has a Private entry.</summary>
    public bool WritePrivate { get; set; } = true;

    /// <summary>Gets the strings of the String INDEX, in SID order from 391.</summary>
    public List<string> Strings { get; } = [];

    /// <summary>Assembles a Type 2 charstring: integers and doubles are operands (doubles as 16.16 Fixed), strings operators, byte arrays raw.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The charstring.</returns>
    public static byte[] T2(params object[] tokens)
    {
        var bytes = new List<byte>();
        foreach (object token in tokens)
        {
            switch (token)
            {
                case int value:
                    T2Integer(bytes, value);
                    break;
                case double value:
                    bytes.Add(255);
                    var fixedValue = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(fixedValue, (int)Math.Round(value * 65536));
                    bytes.AddRange(fixedValue);
                    break;
                case string name:
                    bytes.AddRange(Operators[name]);
                    break;
                case byte[] raw:
                    bytes.AddRange(raw);
                    break;
                default:
                    throw new ArgumentException($"Unsupported token {token}.", nameof(tokens));
            }
        }

        return [.. bytes];
    }

    /// <summary>An INDEX (5176 §5): count, offSize, 1-based offsets, data.</summary>
    public static byte[] Index(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
        {
            return [0, 0];
        }

        int total = items.Sum(item => item.Length) + 1;
        int offSize = total < 0x100 ? 1 : total < 0x10000 ? 2 : total < 0x1000000 ? 3 : 4;
        var bytes = new List<byte> { (byte)(items.Count >> 8), (byte)items.Count, (byte)offSize };
        int offset = 1;
        for (int index = 0; index <= items.Count; index++)
        {
            for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
            {
                bytes.Add((byte)(offset >> shift));
            }

            if (index < items.Count)
            {
                offset += items[index].Length;
            }
        }

        foreach (byte[] item in items)
        {
            bytes.AddRange(item);
        }

        return [.. bytes];
    }

    /// <summary>A DICT integer operand in its shortest form (5176 Table 3).</summary>
    public static byte[] DictInteger(int value) => value switch
    {
        >= -107 and <= 107 => [(byte)(value + 139)],
        >= 108 and <= 1131 => [(byte)(((value - 108) >> 8) + 247), (byte)((value - 108) & 0xFF)],
        >= -1131 and <= -108 => [(byte)(((-value - 108) >> 8) + 251), (byte)((-value - 108) & 0xFF)],
        >= short.MinValue and <= short.MaxValue => [28, (byte)(value >> 8), (byte)value],
        _ => DictInteger32(value),
    };

    /// <summary>A DICT integer operand in the 5-byte form (operator 29).</summary>
    public static byte[] DictInteger32(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>A DICT real operand (5176 Table 5), from the number's invariant text.</summary>
    public static byte[] DictReal(double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture).Replace("E-", "c", StringComparison.Ordinal).Replace("E+", "b", StringComparison.Ordinal).Replace("E", "b", StringComparison.Ordinal);
        var nibbles = new List<int>();
        foreach (char c in text)
        {
            nibbles.Add(c switch { '.' => 0xA, 'b' => 0xB, 'c' => 0xC, '-' => 0xE, _ => c - '0' });
        }

        nibbles.Add(0xF);
        if (nibbles.Count % 2 == 1)
        {
            nibbles.Add(0xF);
        }

        var bytes = new List<byte> { 30 };
        for (int index = 0; index < nibbles.Count; index += 2)
        {
            bytes.Add((byte)((nibbles[index] << 4) | nibbles[index + 1]));
        }

        return [.. bytes];
    }

    /// <summary>Wraps a CFF program in an OpenType (<c>OTTO</c>) file with a "CFF " table and optional other tables.</summary>
    /// <param name="cff">The CFF program.</param>
    /// <param name="tables">Other tables by tag.</param>
    /// <returns>The OpenType program.</returns>
    public static byte[] OpenType(byte[] cff, IReadOnlyDictionary<string, byte[]>? tables = null)
    {
        var all = new SortedDictionary<string, byte[]>(StringComparer.Ordinal) { ["CFF "] = cff };
        foreach ((string tag, byte[] data) in tables ?? new Dictionary<string, byte[]>())
        {
            all[tag] = data;
        }

        var directory = new List<byte>();
        U32(directory, 0x4F54544F);
        U16(directory, all.Count);
        U16(directory, 0);
        U16(directory, 0);
        U16(directory, 0);
        int offset = 12 + (16 * all.Count);
        var body = new List<byte>();
        foreach ((string tag, byte[] data) in all)
        {
            directory.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
            U32(directory, 0);
            U32(directory, (uint)(offset + body.Count));
            U32(directory, (uint)data.Length);
            body.AddRange(data);
            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }
        }

        return [.. directory, .. body];
    }

    /// <summary>Builds the program.</summary>
    /// <returns>The CFF bytes.</returns>
    public byte[] Build()
    {
        List<(string Name, byte[] CharString)> glyphs = Glyphs.Count > 0 ? Glyphs : [(".notdef", T2("endchar"))];
        var strings = new List<string>(Strings);
        int Sid(string name)
        {
            int standard = Array.IndexOf(StandardStrings.Value, name);
            if (standard >= 0)
            {
                return standard;
            }

            int index = strings.IndexOf(name);
            if (index < 0)
            {
                strings.Add(name);
                index = strings.Count - 1;
            }

            return 391 + index;
        }

        byte[] charset = Charset ?? [0, .. glyphs.Skip(1).SelectMany(glyph => BigEndian16(Sid(glyph.Name)))];
        byte[] privateDict;
        byte[] localSubrs = LocalSubrs.Count > 0 ? Index(LocalSubrs) : [];
        byte[] charStrings = Index([.. glyphs.Select(glyph => glyph.CharString)]);
        byte[] header = [1, 0, 4, 4];
        byte[] names = Index([System.Text.Encoding.ASCII.GetBytes(Name)]);

        // The Top DICT's size does not depend on the offsets (5-byte form), so lay it out with zeros first.
        int topLength = BuildTop(0, 0, 0, 0, 0).Length;
        byte[] stringIndex = Index([.. strings.Select(s => System.Text.Encoding.ASCII.GetBytes(s))]);
        byte[] globalSubrs = Index(GlobalSubrs);
        int afterGlobals = header.Length + names.Length + Index([new byte[topLength]]).Length + stringIndex.Length + globalSubrs.Length;
        int charsetOffset = afterGlobals;
        int encodingOffset = charsetOffset + charset.Length;
        byte[] encoding = PredefinedEncoding is null ? Encoding ?? [0, 0] : [];
        int charStringsOffset = encodingOffset + encoding.Length;
        int privateOffset = charStringsOffset + charStrings.Length;
        privateDict = BuildPrivate(LocalSubrs.Count > 0 ? BuildPrivate(0).Length : 0);
        byte[] top = BuildTop(charsetOffset, encodingOffset, charStringsOffset, privateDict.Length, privateOffset);
        return [.. header, .. names, .. Index([top]), .. stringIndex, .. globalSubrs, .. charset, .. encoding, .. charStrings, .. privateDict, .. localSubrs];
    }

    /// <summary>The standard strings with SIDs 0 to 95 and a few accents (5176 Appendix A); other names go to the String INDEX.</summary>
    private static readonly Lazy<string[]> StandardStrings = new(() =>
    {
        string[] strings = new string[172];
        string[] ascii = (".notdef space exclam quotedbl numbersign dollar percent ampersand quoteright parenleft parenright asterisk plus "
            + "comma hyphen period slash zero one two three four five six seven eight nine colon semicolon less equal greater question at "
            + "A B C D E F G H I J K L M N O P Q R S T U V W X Y Z bracketleft backslash bracketright asciicircum underscore quoteleft "
            + "a b c d e f g h i j k l m n o p q r s t u v w x y z braceleft bar braceright asciitilde").Split(' ');
        ascii.CopyTo(strings, 0);
        strings[125] = "acute";
        strings[171] = "Aacute";
        return strings;
    });

    private byte[] BuildTop(int charset, int encoding, int charStrings, int privateSize, int privateOffset)
    {
        var top = new List<byte>(TopDictPrefix);
        if (FontMatrix is { } matrix)
        {
            foreach (double value in matrix)
            {
                top.AddRange(DictReal(value));
            }

            top.AddRange([12, 7]);
        }

        top.AddRange(PredefinedCharset is { } predefinedCharset ? DictInteger(predefinedCharset) : DictInteger32(charset));
        top.Add(15);
        if (PredefinedEncoding is { } predefined)
        {
            if (predefined != 0)
            {
                top.AddRange(DictInteger(predefined));
                top.Add(16);
            }
        }
        else
        {
            top.AddRange(DictInteger32(encoding));
            top.Add(16);
        }

        top.AddRange(DictInteger32(charStrings));
        top.Add(17);
        if (WritePrivate)
        {
            top.AddRange(DictInteger32(privateSize));
            top.AddRange(DictInteger32(privateOffset));
            top.Add(18);
        }

        return [.. top];
    }

    private byte[] BuildPrivate(int subrsOffset)
    {
        var bytes = new List<byte>(PrivateExtra);
        if (DefaultWidthX != 0)
        {
            bytes.AddRange(DictInteger(DefaultWidthX));
            bytes.Add(20);
        }

        if (NominalWidthX != 0)
        {
            bytes.AddRange(DictInteger(NominalWidthX));
            bytes.Add(21);
        }

        if (LocalSubrs.Count > 0)
        {
            bytes.AddRange(DictInteger32(subrsOffset));
            bytes.Add(19);
        }

        return [.. bytes];
    }

    private static void T2Integer(List<byte> bytes, int value)
    {
        switch (value)
        {
            case >= -107 and <= 107:
                bytes.Add((byte)(value + 139));
                break;
            case >= 108 and <= 1131:
                bytes.Add((byte)(((value - 108) >> 8) + 247));
                bytes.Add((byte)((value - 108) & 0xFF));
                break;
            case >= -1131 and <= -108:
                bytes.Add((byte)(((-value - 108) >> 8) + 251));
                bytes.Add((byte)((-value - 108) & 0xFF));
                break;
            default:
                bytes.Add(28);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)value);
                break;
        }
    }

    private static byte[] BigEndian16(int value) => [(byte)(value >> 8), (byte)value];

    private static void U16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void U32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }
}
