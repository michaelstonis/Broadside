using System.Buffers.Binary;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Builds small installed-font files for tests of the operating-system font resolver: a one-glyph TrueType program with the
/// "name", "OS/2" and "post" facts a font directory scan reads (family, style, weight, slope, fixed pitch).
/// </summary>
/// <remarks>OpenType specification: "name" (format 0, Windows Unicode BMP records), "OS/2" (version 4), "post" (isFixedPitch).</remarks>
public static class SystemFontBuilder
{
    /// <summary>Builds a font file.</summary>
    /// <param name="postScriptName">Name id 6.</param>
    /// <param name="family">Name id 1 (legacy family).</param>
    /// <param name="subfamily">Name id 2 (legacy subfamily), such as <c>Bold Italic</c>.</param>
    /// <param name="weight">usWeightClass.</param>
    /// <param name="italic">fsSelection bit 0.</param>
    /// <param name="typographicFamily">Name id 16, or <see langword="null"/>.</param>
    /// <param name="fixedPitch">post isFixedPitch.</param>
    /// <param name="widthClass">usWidthClass (5 normal, 3 condensed).</param>
    /// <param name="symbolCmap">Whether the "cmap" is a (3, 0) symbol subtable instead of (3, 1).</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Font(
        string postScriptName,
        string family,
        string subfamily = "Regular",
        int weight = 400,
        bool italic = false,
        string? typographicFamily = null,
        bool fixedPitch = false,
        int widthClass = 5,
        bool symbolCmap = false)
    {
        var builder = new TrueTypeBuilder
        {
            Name = postScriptName,
            Cmap = TrueTypeBuilder.CmapTable((3, symbolCmap ? 0 : 1, TrueTypeBuilder.Format4(symbolCmap ? (0xF041, 0xF041, 1 - 0xF041) : (0x41, 0x41, 1 - 0x41)))),
        };
        builder.Glyphs.Add([]);
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(50, 0, 450, 700));
        var names = new List<(int Id, string Text)> { (1, family), (2, subfamily), (4, family + " " + subfamily), (6, postScriptName) };
        if (typographicFamily is not null)
        {
            names.Add((16, typographicFamily));
            names.Add((17, subfamily));
        }

        builder.Overrides["name"] = NameTable(names);
        builder.Overrides["OS/2"] = Os2Table(weight, widthClass, (italic ? 1 : 0) | (weight >= 700 ? 1 << 5 : 0) | (!italic && weight < 700 ? 1 << 6 : 0));
        byte[] post = TrueTypeBuilder.PostHeader(0x00030000);
        BinaryPrimitives.WriteUInt32BigEndian(post.AsSpan(12), fixedPitch ? 1u : 0u);
        builder.Post = post;
        return builder.Build();
    }

    /// <summary>A "name" table, format 0, with Windows Unicode BMP (3, 1, 0x409) records in UTF-16BE.</summary>
    public static byte[] NameTable(IReadOnlyList<(int Id, string Text)> names)
    {
        var records = new List<byte>();
        var strings = new List<byte>();
        foreach ((int id, string text) in names.OrderBy(name => name.Id))
        {
            byte[] encoded = Encoding.BigEndianUnicode.GetBytes(text);
            U16(records, 3);
            U16(records, 1);
            U16(records, 0x409);
            U16(records, id);
            U16(records, encoded.Length);
            U16(records, strings.Count);
            strings.AddRange(encoded);
        }

        var table = new List<byte>();
        U16(table, 0);
        U16(table, names.Count);
        U16(table, 6 + records.Count);
        table.AddRange(records);
        table.AddRange(strings);
        return [.. table];
    }

    /// <summary>An "OS/2" table, version 4, 96 bytes.</summary>
    public static byte[] Os2Table(int weight, int widthClass, int fsSelection)
    {
        byte[] table = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(table, 4);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)weight);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)widthClass);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(62), (ushort)fsSelection);
        return table;
    }

    private static void U16(List<byte> target, int value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }
}
