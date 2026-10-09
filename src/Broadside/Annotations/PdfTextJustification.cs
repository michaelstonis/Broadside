namespace Broadside.Annotations;

/// <summary>The justification of the text of a free text or redaction annotation, its <c>Q</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.5.6.6, Table 177 (PDF 1.4), and §12.5.6.23, Table 195. Default <see cref="Left"/>; other values read as the default.</remarks>
public enum PdfTextJustification
{
    /// <summary>0: left-justified (the default).</summary>
    Left = 0,

    /// <summary>1: centred.</summary>
    Centered = 1,

    /// <summary>2: right-justified.</summary>
    Right = 2,
}
