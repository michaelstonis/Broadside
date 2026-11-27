using Broadside.Annotations;
using Broadside.Diagnostics;

namespace Broadside.Forms;

/// <summary>
/// What <see cref="PdfDocument.ImportFormData"/> changed: the fields it set, the names it found no field for, the annotations it
/// added or replaced, and what it repaired or skipped.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.8 and §12.7.6.4 (import-data action); Adobe XFDF 3.0.</remarks>
public sealed class PdfFormDataImport
{
    internal PdfFormDataImport(
        IReadOnlyList<PdfField> fieldsSet,
        IReadOnlyList<string> fieldsNotFound,
        IReadOnlyList<PdfAnnotation> annotationsAdded,
        IReadOnlyList<PdfAnnotation> annotationsReplaced,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        FieldsSet = fieldsSet;
        FieldsNotFound = fieldsNotFound;
        AnnotationsAdded = annotationsAdded;
        AnnotationsReplaced = annotationsReplaced;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the fields whose value or flags the import set, in the order of the file; every field of a name several fields share.</summary>
    public IReadOnlyList<PdfField> FieldsSet { get; }

    /// <summary>Gets the fully qualified names in the file that name no field of the document.</summary>
    public IReadOnlyList<string> FieldsNotFound { get; }

    /// <summary>Gets the annotations the import added to pages.</summary>
    public IReadOnlyList<PdfAnnotation> AnnotationsAdded { get; }

    /// <summary>Gets the annotations the import replaced in place, because an annotation of the file has the same name (<c>NM</c>) on the same page.</summary>
    public IReadOnlyList<PdfAnnotation> AnnotationsReplaced { get; }

    /// <summary>Gets what the import repaired or skipped: deviations of the file from its format and values it could not apply.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
