namespace Broadside.Annotations;

/// <summary>Where the caption of a line annotation is drawn, its <c>CP</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.5.6.7, Table 178 (PDF 1.7). Default <see cref="Inline"/>; other names read as the default.</remarks>
public enum PdfLineCaptionPosition
{
    /// <summary><c>Inline</c>: centred inside the line (the default).</summary>
    Inline,

    /// <summary><c>Top</c>: on top of the line.</summary>
    Top,
}
