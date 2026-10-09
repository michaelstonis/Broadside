using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>
/// A typed view of a marked-content sequence's tag and property list (the operands of <c>BDC</c> or <c>DP</c>): its MCID, whether it
/// is an artifact and of what kind, and the accessibility entries a <c>Span</c> may carry.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.6 (Table 352 and errata Table 352a), §14.6.2 (property lists), §14.7.5.2 (<c>MCID</c>), §14.8.2.2.2 (Table 363,
/// artifacts) and §14.9 (<c>Lang</c>, <c>Alt</c>, <c>ActualText</c>, <c>E</c>). The content interpreter resolves the property list
/// (an inline dictionary, or a name looked up in the resources' <c>Properties</c>) and hands the dictionary over; this view only reads
/// it. Look the MCID up with <see cref="PdfStructureTreeRoot.FindElement(PdfPage, int)"/>. Optional content (<c>/OC</c>) and associated
/// files (<c>/AF</c>) property lists are typed by the optional content and associated files views.
/// </para>
/// <para>The tag of a marked-content sequence is not role mapped (§14.6.1 NOTE 3).</para>
/// </remarks>
public sealed class PdfPropertyList
{
    private readonly PdfDocument? _document;

    /// <summary>Creates the view.</summary>
    /// <param name="tag">The sequence's tag, the name operand of <c>BMC</c>, <c>BDC</c>, <c>MP</c> or <c>DP</c>.</param>
    /// <param name="properties">The resolved property list, or <see langword="null"/> for <c>BMC</c> and <c>MP</c>.</param>
    /// <param name="document">The document, to resolve indirect references a named property list may hold; <see langword="null"/> for an inline dictionary.</param>
    public PdfPropertyList(CosName tag, CosDictionary? properties, PdfDocument? document)
    {
        ArgumentNullException.ThrowIfNull(tag);
        Tag = tag;
        Properties = properties;
        _document = document;
    }

    /// <summary>Gets the tag.</summary>
    /// <remarks>ISO 32000-2 §14.6.1, Table 352.</remarks>
    public CosName Tag { get; }

    /// <summary>Gets the property list, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.6.2.</remarks>
    public CosDictionary? Properties { get; }

    /// <summary>Gets the marked-content identifier (<c>MCID</c>), when the sequence is a content item of a structure element.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.2.</remarks>
    public int? Mcid => Properties is { } properties && StructureValues.Integer(_document, properties, StructureNames.MCID) is >= 0 and var mcid ? mcid : null;

    /// <summary>Gets a value indicating whether the sequence is an artifact (tag <c>Artifact</c>): content that is not part of the document's real content.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public bool IsArtifact => StructureNames.Artifact.Equals(Tag);

    /// <summary>Gets the artifact type (<c>Type</c>): Pagination, Layout, Page or Background; <see langword="null"/> when absent or not an artifact.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public string? ArtifactType => ArtifactName(KnownNames.Type);

    /// <summary>Gets the artifact subtype (<c>Subtype</c>, PDF 1.7): Header, Footer, Watermark, PageNum, Bates, LineNum or Redaction.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public string? ArtifactSubtype => ArtifactName(StructureNames.Subtype);

    /// <summary>Gets the artifact's bounding box (<c>BBox</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public PdfRectangle? ArtifactBoundingBox => IsArtifact && Properties is { } properties ? StructureValues.Rectangle(_document, properties, StructureNames.BBox) : null;

    /// <summary>Gets the page edges a pagination artifact is attached to (<c>Attached</c>): Top, Bottom, Left, Right.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public IReadOnlyList<string> ArtifactAttachments
    {
        get
        {
            if (!IsArtifact || Properties is not { } properties || StructureValues.Get(_document, properties, StructureNames.Attached) is not CosArray array)
            {
                return [];
            }

            var edges = new List<string>(array.Count);
            foreach (CosObject item in array)
            {
                if ((_document?.Resolve(item) ?? item) is CosName edge)
                {
                    edges.Add(edge.Value);
                }
            }

            return edges;
        }
    }

    /// <summary>Gets the natural language of the sequence's content (<c>Lang</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.9.2.2, Table 352a (<c>Span</c>).</remarks>
    public string? Language => TextEntry(StructureNames.Lang);

    /// <summary>Gets the alternate description (<c>Alt</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.9.3, Table 352a (<c>Span</c>, PDF 1.5).</remarks>
    public string? AlternateDescription => TextEntry(StructureNames.Alt);

    /// <summary>Gets the replacement text (<c>ActualText</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.9.4, Table 352a (<c>Span</c>, PDF 1.4).</remarks>
    public string? ActualText => TextEntry(StructureNames.ActualText);

    /// <summary>Gets the expansion of an abbreviation (<c>E</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.9.5, Table 352a (<c>Span</c>, PDF 1.5).</remarks>
    public string? Expansion => TextEntry(StructureNames.E);

    private string? TextEntry(CosName key) => Properties is { } properties ? StructureValues.Text(_document, properties, key) : null;

    private string? ArtifactName(CosName key) => IsArtifact && Properties is { } properties ? StructureValues.Name(_document, properties, key)?.Value : null;
}
