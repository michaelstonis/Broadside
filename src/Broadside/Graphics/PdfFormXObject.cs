using Broadside.Annotations;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>A form XObject: a self-contained content stream with its own bounding box, matrix and resources. A live view over the stream.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.10.1, Table 93. Annotation appearances (§12.5.5) are form XObjects. Every property reads the stream dictionary on
/// every call and returns the default the table gives when an entry is absent or malformed (the identity matrix, no bounding box),
/// without a diagnostic: the object that uses the form (an annotation's appearance, the content interpreter's <c>Do</c>) reports
/// what it cannot use.
/// </para>
/// <para>The content is never interpreted here; <see cref="Decode"/> returns its bytes through the document's filters.</para>
/// </remarks>
public sealed class PdfFormXObject
{
    private readonly PdfDocument _document;

    internal PdfFormXObject(PdfDocument document, CosStream stream, CosReference? reference)
    {
        _document = document;
        Stream = stream;
        Reference = reference;
    }

    /// <summary>Gets the form's stream.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93.</remarks>
    public CosStream Stream { get; }

    /// <summary>Gets the stream's dictionary.</summary>
    public CosDictionary Dictionary => Stream.Dictionary;

    /// <summary>Gets the indirect reference to the stream, or <see langword="null"/> when it was reached directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets a value indicating whether the stream's <c>Subtype</c> is <c>Form</c>, as Table 93 requires.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>Subtype</c>, required).</remarks>
    public bool IsForm => _document.Resolve(Get(AnnotationNames.Subtype)) is CosName { Value: "Form" };

    /// <summary>Gets the form's bounding box in form space, its clip; <see langword="null"/> when <c>BBox</c> is absent or not four numbers.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>BBox</c>, required).</remarks>
    public PdfRectangle? BoundingBox => AnnotationValues.ReadRectangle(_document, Get(AnnotationNames.BBox));

    /// <summary>Gets the form matrix, which maps form space to the user space of the content that paints it; the identity when absent or not six numbers.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>Matrix</c>, default <c>[1 0 0 1 0 0]</c>).</remarks>
    public Matrix Matrix => AnnotationValues.ReadMatrix(_document, Get(AnnotationNames.Matrix)) ?? Matrix.Identity;

    /// <summary>Gets the resources the form's content uses, or <see langword="null"/> when the form has none of its own.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>Resources</c>), and §7.8.3.</remarks>
    public CosDictionary? Resources => _document.Resolve(Get(AnnotationNames.Resources)) as CosDictionary;

    /// <summary>Gets the group attributes dictionary that makes the form a transparency group, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.10.3, Table 94, and §11.6.6 (<c>Group</c>, PDF 1.4).</remarks>
    public CosDictionary? Group => _document.Resolve(Get(AnnotationNames.Group)) as CosDictionary;

    /// <summary>Gets the optional content group or membership dictionary of the form, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>OC</c>, PDF 1.5), and §8.11.3.3.</remarks>
    public CosDictionary? OptionalContent => _document.Resolve(Get(AnnotationNames.OC)) as CosDictionary;

    /// <summary>Gets the form's key in the structural parent tree when it is one content item, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>StructParent</c>, PDF 1.3), and §14.7.5.4.</remarks>
    public int? StructParent => AnnotationValues.ReadInteger(_document, Get(AnnotationNames.StructParent));

    /// <summary>Gets the form's key in the structural parent tree for the marked content in it, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (<c>StructParents</c>, PDF 1.3), and §14.7.5.4.</remarks>
    public int? StructParents => AnnotationValues.ReadInteger(_document, Get(AnnotationNames.StructParents));

    /// <summary>Returns the form's content decoded through the filters its <c>Filter</c> entry names.</summary>
    /// <returns>The content stream bytes.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.3.8.2 and §7.4; as <see cref="PdfDocument.DecodeStream(CosStream)"/>.</remarks>
    public ReadOnlyMemory<byte> Decode() => _document.DecodeStream(Stream);

    /// <summary>Returns the form view over <paramref name="value"/> when it is a stream, else <see langword="null"/>.</summary>
    internal static PdfFormXObject? Create(PdfDocument document, CosObject? value) =>
        document.Resolve(value) is CosStream stream ? new PdfFormXObject(document, stream, value as CosReference) : null;

    private CosObject? Get(CosName key) => Stream.Dictionary.TryGetValue(key, out CosObject? value) ? value : null;
}
