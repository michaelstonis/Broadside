namespace Broadside.Fonts;

/// <summary>Where a font program comes from: the font descriptor entry that holds it.</summary>
/// <remarks>ISO 32000-2 §9.8.1, Table 120, and §9.9, Table 124.</remarks>
public enum FontProgramSource
{
    /// <summary>Not from a font descriptor: a program given to a parser directly.</summary>
    Unspecified,

    /// <summary>The <c>FontFile</c> entry: a Type 1 program.</summary>
    FontFile,

    /// <summary>The <c>FontFile2</c> entry (PDF 1.1): a TrueType program.</summary>
    FontFile2,

    /// <summary>The <c>FontFile3</c> entry (PDF 1.2): a program whose format the stream's <c>Subtype</c> names.</summary>
    FontFile3,
}
