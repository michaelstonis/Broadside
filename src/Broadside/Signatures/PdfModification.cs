using Broadside.Objects;

namespace Broadside.Signatures;

/// <summary>What an incremental update after a signature changed, as modification detection classifies it.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.2.2.2: the signed revision is compared with the later ones "to see whether there have been modifications to any
/// objects that are not permitted by the transform parameters". Which kinds each permission allows (Table 257; Table 236, <c>P</c>):
/// </para>
/// <list type="table">
/// <listheader><term>Kind</term><description>Permitted with</description></listheader>
/// <item><term><see cref="DocumentSecurityStore"/>, <see cref="DocumentTimestamp"/>, <see cref="DocumentInformation"/></term><description>Every permission (Table 257: DSS and document timestamp updates "shall not be considered as changes to the document"; §12.8.4.4)</description></item>
/// <item><term><see cref="Signing"/>, <see cref="FormFilling"/></term><description><see cref="PdfCertificationPermissions.FormFilling"/> and <see cref="PdfCertificationPermissions.FormFillingAndAnnotations"/>; a field a signature locks may not be filled or signed after it (§12.8.2.4)</description></item>
/// <item><term><see cref="Annotations"/></term><description><see cref="PdfCertificationPermissions.FormFillingAndAnnotations"/></description></item>
/// <item><term><see cref="FormStructure"/>, <see cref="Pages"/>, <see cref="Other"/></term><description>Never</description></item>
/// </list>
/// <para>
/// Without a certification signature the permissions in effect are those of <see cref="PdfCertificationPermissions.FormFillingAndAnnotations"/>
/// (Table 236), narrowed by each signed field lock's <c>P</c> for the updates after its signature.
/// </para>
/// </remarks>
public enum PdfModificationKind
{
    /// <summary>The document security store: the catalog's <c>DSS</c> entry or what it holds (§12.8.4.3).</summary>
    DocumentSecurityStore,

    /// <summary>A document timestamp: a signature field whose value is a <c>DocTimeStamp</c> signature dictionary, added or signed (§12.8.5).</summary>
    DocumentTimestamp,

    /// <summary>
    /// Bookkeeping a save writes: the document information dictionary, the catalog's metadata stream, and the catalog's
    /// <c>Version</c> and <c>Extensions</c> (§14.3, §7.7.2 Table 29, §7.12).
    /// </summary>
    DocumentInformation,

    /// <summary>
    /// A signature: a signature field signed or added signed, with its widget, the form's <c>SigFlags</c>, and locked fields marked
    /// read-only (§12.7.5.5, §12.8.1).
    /// </summary>
    Signing,

    /// <summary>
    /// A form field filled in: its value (<c>V</c>, <c>RV</c>, <c>I</c>), its widgets' appearances and states, and the form's
    /// default appearance, default resources and <c>NeedAppearances</c> (§12.7.4).
    /// </summary>
    FormFilling,

    /// <summary>An annotation other than a widget created, deleted or modified (§12.5).</summary>
    Annotations,

    /// <summary>A form field or widget added or removed, or a field's properties other than its value changed (§12.7.4).</summary>
    FormStructure,

    /// <summary>A page's content, resources or attributes changed, or pages added, removed or reordered (§7.7.3, §7.8).</summary>
    Pages,

    /// <summary>Anything else: another catalog entry, the trailer's root, an existing signature, or an object none of the above reach.</summary>
    Other,
}

/// <summary>One change an incremental update after a signature made, and whether that signature permits it.</summary>
/// <remarks>ISO 32000-2 §12.8.2.2.2 and §12.8.2.4.</remarks>
public sealed class PdfModification
{
    internal PdfModification(int revision, PdfModificationKind kind, CosReference? objectReference, string? fieldName, string description, bool isPermitted)
    {
        Revision = revision;
        Kind = kind;
        ObjectReference = objectReference;
        FieldName = fieldName;
        Description = description;
        IsPermitted = isPermitted;
    }

    /// <summary>Gets the index in <see cref="PdfDocument.Revisions"/> of the revision that made the change.</summary>
    /// <remarks>ISO 32000-2 §7.5.6.</remarks>
    public int Revision { get; }

    /// <summary>Gets what kind of change it is.</summary>
    /// <remarks>ISO 32000-2 §12.8.2.2, Table 257.</remarks>
    public PdfModificationKind Kind { get; }

    /// <summary>Gets the object that changed, or <see langword="null"/> when the change is to the trailer.</summary>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public CosReference? ObjectReference { get; }

    /// <summary>Gets the fully qualified name of the form field the change is to, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2.</remarks>
    public string? FieldName { get; }

    /// <summary>Gets what changed, for a person to read.</summary>
    /// <remarks>ISO 32000-2 §12.8.2.2.2.</remarks>
    public string Description { get; }

    /// <summary>Gets a value indicating whether the permissions in effect and the signature's field locks allow the change.</summary>
    /// <remarks>ISO 32000-2 §12.8.2.2, Table 257; §12.7.5.5, Table 236; §12.8.2.4.</remarks>
    public bool IsPermitted { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"Revision {Revision}: {Kind}{(FieldName is null ? string.Empty : $" '{FieldName}'")}{(ObjectReference is null ? string.Empty : $" ({ObjectReference})")}: {Description}{(IsPermitted ? string.Empty : " (not permitted)")}";
}
