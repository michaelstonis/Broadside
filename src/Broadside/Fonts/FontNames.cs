using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>Names the font model looks up in font dictionaries, font descriptors and encoding dictionaries, created once.</summary>
internal static class FontNames
{
    /// <summary><c>/Font</c>, the type of a font dictionary and the resource category of fonts (§9.6.2.1 Table 109, §7.8.3).</summary>
    public static readonly CosName Font = new("Font");

    /// <summary><c>/Subtype</c> (§9.5 Table 108).</summary>
    public static readonly CosName Subtype = new("Subtype");

    /// <summary><c>/BaseFont</c> (§9.6.2.1 Table 109).</summary>
    public static readonly CosName BaseFont = new("BaseFont");

    /// <summary><c>/FirstChar</c> (§9.6.2.1 Table 109).</summary>
    public static readonly CosName FirstChar = new("FirstChar");

    /// <summary><c>/LastChar</c> (§9.6.2.1 Table 109).</summary>
    public static readonly CosName LastChar = new("LastChar");

    /// <summary><c>/Widths</c> (§9.6.2.1 Table 109).</summary>
    public static readonly CosName Widths = new("Widths");

    /// <summary><c>/FontDescriptor</c> (§9.6.2.1 Table 109), also the type of a font descriptor (§9.8.1 Table 120).</summary>
    public static readonly CosName FontDescriptor = new("FontDescriptor");

    /// <summary><c>/Encoding</c> (§9.6.2.1 Table 109), also the type of an encoding dictionary (§9.6.5.1 Table 112).</summary>
    public static readonly CosName Encoding = new("Encoding");

    /// <summary><c>/BaseEncoding</c> (§9.6.5.1 Table 112).</summary>
    public static readonly CosName BaseEncoding = new("BaseEncoding");

    /// <summary><c>/Differences</c> (§9.6.5.1 Table 112).</summary>
    public static readonly CosName Differences = new("Differences");

    /// <summary><c>/Type1</c> (§9.5 Table 108).</summary>
    public static readonly CosName Type1 = new("Type1");

    /// <summary><c>/MMType1</c> (§9.5 Table 108).</summary>
    public static readonly CosName MMType1 = new("MMType1");

    /// <summary><c>/TrueType</c> (§9.5 Table 108).</summary>
    public static readonly CosName TrueType = new("TrueType");

    /// <summary><c>/Type3</c> (§9.5 Table 108).</summary>
    public static readonly CosName Type3 = new("Type3");

    /// <summary><c>/Type0</c> (§9.5 Table 108).</summary>
    public static readonly CosName Type0 = new("Type0");

    /// <summary><c>/WinAnsiEncoding</c> (Annex D.1 Table D.1).</summary>
    public static readonly CosName WinAnsiEncoding = new("WinAnsiEncoding");

    /// <summary><c>/MacRomanEncoding</c> (Annex D.1 Table D.1).</summary>
    public static readonly CosName MacRomanEncoding = new("MacRomanEncoding");

    /// <summary><c>/MacExpertEncoding</c> (Annex D.1 Table D.1).</summary>
    public static readonly CosName MacExpertEncoding = new("MacExpertEncoding");

    /// <summary><c>/StandardEncoding</c>: not a predefined PDF name (§9.6.5.1), but written by some producers.</summary>
    public static readonly CosName StandardEncoding = new("StandardEncoding");

    /// <summary><c>/FontName</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontName = new("FontName");

    /// <summary><c>/FontFamily</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontFamily = new("FontFamily");

    /// <summary><c>/FontStretch</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontStretch = new("FontStretch");

    /// <summary><c>/FontWeight</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontWeight = new("FontWeight");

    /// <summary><c>/Flags</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName Flags = new("Flags");

    /// <summary><c>/FontBBox</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontBBox = new("FontBBox");

    /// <summary><c>/ItalicAngle</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName ItalicAngle = new("ItalicAngle");

    /// <summary><c>/Ascent</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName Ascent = new("Ascent");

    /// <summary><c>/Descent</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName Descent = new("Descent");

    /// <summary><c>/Leading</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName Leading = new("Leading");

    /// <summary><c>/CapHeight</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName CapHeight = new("CapHeight");

    /// <summary><c>/XHeight</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName XHeight = new("XHeight");

    /// <summary><c>/StemV</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName StemV = new("StemV");

    /// <summary><c>/StemH</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName StemH = new("StemH");

    /// <summary><c>/AvgWidth</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName AvgWidth = new("AvgWidth");

    /// <summary><c>/MaxWidth</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName MaxWidth = new("MaxWidth");

    /// <summary><c>/MissingWidth</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName MissingWidth = new("MissingWidth");

    /// <summary><c>/FontFile</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontFile = new("FontFile");

    /// <summary><c>/FontFile2</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontFile2 = new("FontFile2");

    /// <summary><c>/FontFile3</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName FontFile3 = new("FontFile3");

    /// <summary><c>/Length1</c> of a font file stream (§9.9 Table 125).</summary>
    public static readonly CosName Length1 = new("Length1");

    /// <summary><c>/Length2</c> of a font file stream (§9.9 Table 125).</summary>
    public static readonly CosName Length2 = new("Length2");

    /// <summary><c>/Length3</c> of a font file stream (§9.9 Table 125).</summary>
    public static readonly CosName Length3 = new("Length3");

    /// <summary><c>/CharSet</c> (§9.8.1 Table 120).</summary>
    public static readonly CosName CharSet = new("CharSet");
}
