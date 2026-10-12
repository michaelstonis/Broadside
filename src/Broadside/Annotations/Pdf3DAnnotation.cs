using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A 3D annotation: 3D artwork on the page.</summary>
/// <remarks>ISO 32000-2 §12.5.6.25 and §13.6.2, Table 309 (PDF 1.6). Parse-and-preserve (3D is out of scope): the entries are exposed as data, never rendered.</remarks>
public sealed class Pdf3DAnnotation : PdfAnnotation
{
    internal Pdf3DAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.ThreeD)
    {
    }

    /// <summary>Gets the 3D stream or 3D reference dictionary (<c>3DD</c>, required), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public CosObject? Artwork
    {
        get
        {
            CosObject? value = Get(AnnotationNames.ThreeDD);
            if (value is null)
            {
                ReportMissing(AnnotationNames.ThreeDD, "Table 309");
            }

            return value;
        }
    }

    /// <summary>Gets the default view (<c>3DV</c>): a view dictionary, an index, a name or a keyword, as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public CosObject? DefaultView => Get(AnnotationNames.ThreeDV);

    /// <summary>Gets the activation dictionary (<c>3DA</c>), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public CosDictionary? Activation => ReadDictionary(AnnotationNames.ThreeDA);

    /// <summary>Gets a value indicating whether the artwork is interactive (<c>3DI</c>); default <see langword="true"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public bool IsInteractive => ReadBoolean(AnnotationNames.ThreeDI, fallback: true);

    /// <summary>Gets the 3D view box in the annotation's target coordinate system (<c>3DB</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public PdfRectangle? ViewBox => AnnotationValues.ReadRectangle(Document, Get(AnnotationNames.ThreeDB));

    /// <summary>Gets the units dictionary (<c>3DU</c>, PDF 2.0), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309.</remarks>
    public CosDictionary? Units => ReadDictionary(AnnotationNames.ThreeDU);

    /// <summary>Gets the geospatial information (<c>GEO</c>, PDF 2.0), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.6.2, Table 309, and §12.10.</remarks>
    public CosDictionary? Geospatial => ReadDictionary(AnnotationNames.GEO);
}
