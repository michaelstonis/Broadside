namespace Broadside.Annotations;

/// <summary>The style of an annotation's border, the <c>S</c> entry of a border style dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.4, Table 168. A name the table does not list reads as <see cref="Solid"/> ("a PDF processor shall tolerate
/// other border styles"), without a diagnostic.
/// </remarks>
public enum PdfBorderStyle
{
    /// <summary><c>S</c>: a solid rectangle surrounding the annotation (the default).</summary>
    Solid,

    /// <summary><c>D</c>: a dashed rectangle, with the dash pattern of <see cref="PdfAnnotationBorder.DashPattern"/>.</summary>
    Dashed,

    /// <summary><c>B</c>: a simulated embossed rectangle that appears raised above the page.</summary>
    Beveled,

    /// <summary><c>I</c>: a simulated engraved rectangle that appears recessed into the page.</summary>
    Inset,

    /// <summary><c>U</c>: a single line along the bottom of the annotation rectangle.</summary>
    Underline,
}
