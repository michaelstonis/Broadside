using System.Diagnostics.CodeAnalysis;

namespace Broadside.Signatures;

/// <summary>
/// The changes a certification signature permits after it (the <c>P</c> entry of the DocMDP transform parameters), or a signature
/// field lock permits after its signature (the lock's <c>P</c>, PDF 2.0).
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.2.2, Table 257, and §12.7.5.5, Table 236. Whatever the value, incremental updates that only add a document
/// security store (§12.8.4.3) or a document timestamp (§12.8.5) are not changes to the document.
/// </remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The values are the numbers the P entry holds; 0 is not one of them.")]
public enum PdfCertificationPermissions
{
    /// <summary><c>1</c>: no changes; any change invalidates the signature.</summary>
    NoChanges = 1,

    /// <summary><c>2</c>: filling in forms, instantiating page templates and signing; other changes invalidate the signature. The default.</summary>
    FormFilling = 2,

    /// <summary><c>3</c>: what <see cref="FormFilling"/> permits, and creating, deleting and modifying annotations.</summary>
    FormFillingAndAnnotations = 3,
}
