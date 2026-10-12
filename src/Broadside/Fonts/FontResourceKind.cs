namespace Broadside.Fonts;

/// <summary>The kinds of named resource a <see cref="IFontResolver"/> can supply besides font programs.</summary>
/// <remarks>
/// ISO 32000-2 §9.7.5.2 (Table 116) and §9.10.2. Both are CMap files in the text syntax of Adobe Technical Note #5014, which the
/// core parses; a resolver returns the original file's bytes.
/// </remarks>
public enum FontResourceKind
{
    /// <summary>A predefined CMap, named as in Table 116 (such as <c>90ms-RKSJ-H</c> or <c>UniJIS-UTF16-H</c>), including the CMaps they use.</summary>
    CMap = 0,

    /// <summary>
    /// A CMap from the CIDs of a character collection to Unicode, named <c>{Registry}-{Ordering}-UCS2</c> as Adobe names them (such
    /// as <c>Adobe-Japan1-UCS2</c>).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10.2, the method for a font whose CIDSystemInfo names an Adobe character collection.</remarks>
    CidToUnicode = 1,
}
