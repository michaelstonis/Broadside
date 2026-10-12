namespace Broadside.Fonts;

/// <summary>The formats a font program can have in a PDF file, as a font file stream declares them or its bytes show them.</summary>
/// <remarks>
/// ISO 32000-2 §9.9, Table 124: <c>FontFile</c> holds a Type 1 program, <c>FontFile2</c> a TrueType program, <c>FontFile3</c> a
/// program whose format its <c>Subtype</c> names (<c>Type1C</c> and <c>CIDFontType0C</c> for CFF, <c>OpenType</c>).
/// </remarks>
public enum FontProgramFormat
{
    /// <summary>A TrueType program: an sfnt container with "glyf" outlines (Apple TrueType Reference Manual, OpenType "glyf"), or a TrueType collection.</summary>
    TrueType,

    /// <summary>An OpenType program (ISO/IEC 14496-22): an sfnt container whose outlines are "glyf" or "CFF " data.</summary>
    OpenType,

    /// <summary>A bare Compact Font Format program (Adobe TN 5176), name-keyed or CID-keyed, with Type 2 charstrings.</summary>
    Cff,

    /// <summary>A Type 1 program (Adobe Type 1 Font Format), in PFA or PFB form.</summary>
    Type1,
}
