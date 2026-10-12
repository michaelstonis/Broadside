namespace Broadside.Annotations;

/// <summary>The annotation flags, the bits of an annotation dictionary's <c>F</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.3, Table 167. Bit n of the entry is <c>1 &lt;&lt; (n − 1)</c>. "All other bits of the integer shall be set to
/// 0"; a file that sets them keeps them (<see cref="PdfAnnotation.Flags"/> returns every bit as stored).
/// </remarks>
[Flags]
public enum PdfAnnotationFlags
{
    /// <summary>No flag is set (the default).</summary>
    None = 0,

    /// <summary>Bit 1 (PDF 1.0): for an annotation of a type the processor has no handler for, do not display or print it.</summary>
    Invisible = 1 << 0,

    /// <summary>Bit 2 (PDF 1.2): do not display or print the annotation nor let the user interact with it.</summary>
    Hidden = 1 << 1,

    /// <summary>Bit 3 (PDF 1.2): print the annotation when the page is printed (unless <see cref="Hidden"/> is set).</summary>
    Print = 1 << 2,

    /// <summary>Bit 4 (PDF 1.3): do not scale the appearance with the page magnification; the upper-left corner of the rectangle stays fixed.</summary>
    NoZoom = 1 << 3,

    /// <summary>Bit 5 (PDF 1.3): do not rotate the appearance with the page; the upper-left corner of the rectangle stays fixed.</summary>
    NoRotate = 1 << 4,

    /// <summary>Bit 6 (PDF 1.3): do not display the annotation on the screen nor let the user interact with it; it may still print.</summary>
    NoView = 1 << 5,

    /// <summary>Bit 7 (PDF 1.3): do not let the user interact with the annotation. Ignored for widget annotations.</summary>
    ReadOnly = 1 << 6,

    /// <summary>Bit 8 (PDF 1.4): do not let the user delete the annotation or change its properties.</summary>
    Locked = 1 << 7,

    /// <summary>Bit 9 (PDF 1.5): invert <see cref="NoView"/> for certain events, such as the mouse hovering over the annotation.</summary>
    ToggleNoView = 1 << 8,

    /// <summary>Bit 10 (PDF 1.7): do not let the user change the annotation's contents (its properties may still change).</summary>
    LockedContents = 1 << 9,
}
