using System.Diagnostics.CodeAnalysis;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>
/// The one table of the inline-image abbreviations of ISO 32000-2 §8.9.7: the keys of Table 91 and the colour space and filter
/// names of Table 92, with the full names they stand for. The image model, the stream decoder and the content reader all read it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.7, Tables 91 and 92. Lookups over raw name bytes accept either spelling (the abbreviation or the full name) and
/// return the shared full <see cref="CosName"/>, so the content reader can classify an inline image dictionary without allocating.
/// </remarks>
internal static class InlineImageAbbreviations
{
    /// <summary>Table 91: the abbreviated keys and the image XObject keys they stand for.</summary>
    private static readonly (CosName Abbreviation, CosName Full)[] KeyTable =
    [
        (ImageNames.Bpc, ImageNames.BitsPerComponent),
        (ImageNames.CS, ImageNames.ColorSpace),
        (ImageNames.D, ImageNames.Decode),
        (ImageNames.DP, ImageNames.DecodeParms),
        (ImageNames.F, ImageNames.Filter),
        (ImageNames.H, ImageNames.Height),
        (ImageNames.IM, ImageNames.ImageMask),
        (ImageNames.I, ImageNames.Interpolate),
        (ImageNames.L, ImageNames.Length),
        (ImageNames.W, ImageNames.Width),
    ];

    /// <summary>Table 92: the abbreviated colour space names (<c>I</c> only as an Indexed array's first element).</summary>
    private static readonly (CosName Abbreviation, CosName Full)[] ColorSpaceTable =
    [
        (new CosName("G"), ImageNames.DeviceGray),
        (new CosName("RGB"), ImageNames.DeviceRgb),
        (new CosName("CMYK"), ImageNames.DeviceCmyk),
        (ImageNames.I, ImageNames.Indexed),
    ];

    /// <summary>Table 92: the abbreviated filter names, valid only in inline images.</summary>
    private static readonly (CosName Abbreviation, CosName Full)[] FilterTable =
    [
        (new CosName("AHx"), FilterNames.AsciiHexDecode),
        (new CosName("A85"), FilterNames.Ascii85Decode),
        (new CosName("LZW"), FilterNames.LzwDecode),
        (new CosName("Fl"), FilterNames.FlateDecode),
        (new CosName("RL"), FilterNames.RunLengthDecode),
        (new CosName("CCF"), FilterNames.CcittFaxDecode),
        (new CosName("DCT"), FilterNames.DctDecode),
    ];

    /// <summary>Returns the image XObject key an inline image key stands for: the full key of an abbreviation, or the key itself when it is already a full key of Table 91.</summary>
    /// <param name="name">The key's bytes.</param>
    /// <returns>The full key, or <see langword="null"/> when the name is neither spelling of a Table 91 key.</returns>
    public static CosName? Key(ReadOnlySpan<byte> name) => Find(KeyTable, name);

    /// <summary>Returns the device or Indexed colour space name an inline colour space name stands for, in either spelling.</summary>
    /// <param name="name">The name's bytes.</param>
    /// <returns>The full name, or <see langword="null"/> when the name is neither spelling of a Table 92 colour space.</returns>
    public static CosName? ColorSpace(ReadOnlySpan<byte> name) => Find(ColorSpaceTable, name);

    /// <summary>Returns the filter name an inline filter name stands for, in either spelling.</summary>
    /// <param name="name">The name's bytes.</param>
    /// <returns>The full name, or <see langword="null"/> when the name is neither spelling of a Table 92 filter.</returns>
    public static CosName? Filter(ReadOnlySpan<byte> name) => Find(FilterTable, name);

    /// <summary>Returns the abbreviation of a full Table 91 key.</summary>
    /// <param name="full">The full key.</param>
    /// <returns>The abbreviation, or <see langword="null"/> when the key has none.</returns>
    public static CosName? AbbreviationOfKey(CosName full)
    {
        foreach ((CosName abbreviation, CosName name) in KeyTable)
        {
            if (full.Equals(name))
            {
                return abbreviation;
            }
        }

        return null;
    }

    /// <summary>Returns the full filter name a Table 92 filter abbreviation stands for; full names are not abbreviations.</summary>
    /// <param name="name">A filter name.</param>
    /// <param name="fullName">The full name, when <paramref name="name"/> is an abbreviation.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is an abbreviation.</returns>
    public static bool TryExpandFilter(CosName name, [NotNullWhen(true)] out CosName? fullName) => TryExpand(FilterTable, name, out fullName);

    /// <summary>Returns the full colour space name a Table 92 colour space abbreviation stands for, or the name itself.</summary>
    /// <param name="name">A colour space name.</param>
    /// <returns>The full name.</returns>
    public static CosName ExpandColorSpace(CosName name) => TryExpand(ColorSpaceTable, name, out CosName? full) ? full : name;

    /// <summary>Returns the full key a Table 91 key abbreviation stands for, or the key itself.</summary>
    /// <param name="name">A key.</param>
    /// <returns>The full key.</returns>
    public static CosName ExpandKey(CosName name) => TryExpand(KeyTable, name, out CosName? full) ? full : name;

    private static bool TryExpand((CosName Abbreviation, CosName Full)[] table, CosName name, [NotNullWhen(true)] out CosName? fullName)
    {
        foreach ((CosName abbreviation, CosName full) in table)
        {
            if (name.Equals(abbreviation))
            {
                fullName = full;
                return true;
            }
        }

        fullName = null;
        return false;
    }

    private static CosName? Find((CosName Abbreviation, CosName Full)[] table, ReadOnlySpan<byte> name)
    {
        foreach ((CosName abbreviation, CosName full) in table)
        {
            if (name.SequenceEqual(abbreviation.Bytes) || name.SequenceEqual(full.Bytes))
            {
                return full;
            }
        }

        return null;
    }
}
