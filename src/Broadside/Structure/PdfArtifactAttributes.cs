using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>Artifact</c>, on an <c>Artifact</c> structure element (PDF 2.0).</summary>
/// <remarks>ISO 32000-2 §14.8.5.8, Table 385.</remarks>
public sealed class PdfArtifactAttributes : PdfAttributeObject
{
    internal PdfArtifactAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>Gets the artifact type (<c>Type</c>): Pagination, Layout, Page or Inline.</summary>
    /// <remarks>ISO 32000-2 Table 385.</remarks>
    public string? ArtifactType => NameValue(Objects.KnownNames.Type);

    /// <summary>Gets the artifact's bounding box (<c>BBox</c>).</summary>
    /// <remarks>ISO 32000-2 Table 385.</remarks>
    public PdfRectangle? BoundingBox => RectangleValue(StructureNames.BBox);

    /// <summary>Gets the artifact subtype (<c>Subtype</c>): Header, Footer, Watermark, PageNum, Bates, LineNum or Redaction.</summary>
    /// <remarks>ISO 32000-2 Table 385.</remarks>
    public string? ArtifactSubtype => NameValue(StructureNames.Subtype);
}
