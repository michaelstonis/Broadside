using Broadside.Objects;
using Broadside.Structure;
using Broadside.Writing;

namespace Broadside.Graphics;

public sealed partial class PdfCanvas
{
    private static readonly CosName ArtifactTag = new("Artifact");
    private static readonly CosName McidKey = new("MCID");
    private static readonly CosName AttachedKey = new("Attached");
    private static readonly CosName BBoxKey = new("BBox");
    private static readonly CosName[] ArtifactTypeNames = [.. Enum.GetNames<PdfArtifactType>().Select(static name => new CosName(name))];
    private static readonly string[] Pdf20ArtifactSubtypes = ["PageNum", "LineNum", "Redaction", "Bates"];

    /// <summary>
    /// Begins a tagged marked-content sequence that is a content item of <paramref name="element"/>: allocates the page's next MCID,
    /// links it into the structure tree and writes <c>/S &lt;&lt;/MCID n&gt;&gt; BDC</c>, where <c>S</c> is the element's structure type.
    /// </summary>
    /// <param name="element">The structure element of this document the content belongs to.</param>
    /// <returns>The content item added to the element.</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> belongs to another document or has no structure type.</exception>
    /// <exception cref="InvalidOperationException">The canvas draws on a form XObject (use the overload that names the page) or a pattern cell, a path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.6.1, §14.7.5.2 (MCIDs) and §14.7.5.4 (the parent tree). This is the one way to tag canvas content: the MCID is
    /// allocated through <see cref="PdfStructureElement.AddMarkedContent(PdfPage)"/>, so the page gets its <c>StructParents</c> and the
    /// parent tree its entry. End the sequence with <see cref="EndMarkedContent"/>. An element drawn on several pages gets one content
    /// item per sequence. MCIDs already in the page's content but not in the structure tree are not seen.
    /// </remarks>
    public PdfMarkedContentReference BeginTaggedContent(PdfStructureElement element) => BeginTaggedContent(element, null, null);

    /// <summary>Begins a tagged marked-content sequence that is a content item of <paramref name="element"/>, with more properties.</summary>
    /// <param name="element">The structure element of this document the content belongs to.</param>
    /// <param name="properties">More entries of the inline property list, such as <c>ActualText</c>, <c>Alt</c> or <c>Lang</c>: direct objects only, without <c>MCID</c>.</param>
    /// <returns>The content item added to the element.</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> belongs to another document or has no structure type, or <paramref name="properties"/> holds an <c>MCID</c>, an indirect reference or a stream.</exception>
    /// <exception cref="InvalidOperationException">The canvas draws on a form XObject or a pattern cell, a path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.6.2, §14.7.5.2 and §14.9 (Alt, ActualText, E, Lang on marked content). See <see cref="BeginTaggedContent(PdfStructureElement)"/>.</remarks>
    public PdfMarkedContentReference BeginTaggedContent(PdfStructureElement element, CosDictionary properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return BeginTaggedContent(element, null, properties);
    }

    /// <summary>Begins a tagged marked-content sequence that is a content item of <paramref name="element"/>, on a page or in a form drawn on <paramref name="page"/>.</summary>
    /// <param name="element">The structure element of this document the content belongs to.</param>
    /// <param name="page">
    /// The page: on a form XObject's canvas, the page the form is drawn on (the content item is an MCR with <c>Stm</c> and the form gets
    /// its own <c>StructParents</c>); on a page's canvas, that page or <see langword="null"/>.
    /// </param>
    /// <param name="properties">More entries of the inline property list (direct objects only, without <c>MCID</c>), or <see langword="null"/>.</param>
    /// <returns>The content item added to the element.</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> belongs to another document or has no structure type, <paramref name="page"/> is not the canvas's page, or <paramref name="properties"/> holds an <c>MCID</c>, an indirect reference or a stream.</exception>
    /// <exception cref="InvalidOperationException">The canvas draws on a form XObject and <paramref name="page"/> is <see langword="null"/>, it draws on a pattern cell, a path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.6.1, §14.7.5.2, Table 357 (marked-content references) and §14.7.5.4. See <see cref="BeginTaggedContent(PdfStructureElement)"/>.</remarks>
    public PdfMarkedContentReference BeginTaggedContent(PdfStructureElement element, PdfPage? page, CosDictionary? properties)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfPathOpen();
        if (!ReferenceEquals(element.Document, _document))
        {
            throw new ArgumentException("The structure element belongs to another document.", nameof(element));
        }

        CosName tag = element.StructureType
            ?? throw new ArgumentException("The structure element has no structure type (S); its tag cannot be written.", nameof(element));
        if (properties is not null && (properties.ContainsKey(McidKey) || !IsDirect(properties)))
        {
            throw new ArgumentException("The extra properties shall be direct objects and shall not hold an MCID, which is allocated (ISO 32000-2 §14.6.2, §14.7.5.2).", nameof(properties));
        }

        if (_cell is not null)
        {
            throw new InvalidOperationException("Content of a pattern cell cannot be tagged: a pattern has no structural parent key (ISO 32000-2 §14.7.5.4).");
        }

        if (_page is not null && page is not null && !ReferenceEquals(page.Dictionary, _page.Dictionary))
        {
            throw new ArgumentException("A page canvas tags content of its own page.", nameof(page));
        }

        PdfPage owner = _page ?? page
            ?? throw new InvalidOperationException("Content of a form XObject is tagged for the page the form is drawn on: pass that page (ISO 32000-2 §14.7.5.2, Table 357, Pg).");
        PdfMarkedContentReference item = _form is null ? element.AddMarkedContent(owner) : element.AddMarkedContent(owner, _form);
        var list = new CosDictionary();
        if (properties is not null)
        {
            foreach ((CosName key, CosObject value) in properties)
            {
                list[key] = value;
            }
        }

        list[McidKey] = new CosInteger(item.Mcid);
        BeginMarked(tag.Value, list);
        return item;
    }

    /// <summary>Begins an artifact without a property list (<c>/Artifact BMC</c>): content that is not part of the document's logical structure.</summary>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2. End it with <see cref="EndMarkedContent"/>. Text extraction leaves artifacts out unless asked.</remarks>
    public void BeginArtifact() => BeginMarked(ArtifactTag.Value, properties: null);

    /// <summary>Begins an artifact of <paramref name="type"/> (<c>/Artifact &lt;&lt;/Type ...&gt;&gt; BDC</c>).</summary>
    /// <param name="type">The artifact's type.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not defined.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363. End it with <see cref="EndMarkedContent"/>.</remarks>
    public void BeginArtifact(PdfArtifactType type) => BeginArtifact(type, null, PdfArtifactEdges.None, null);

    /// <summary>Begins an artifact of <paramref name="type"/> with its subtype, attached edges and bounding box.</summary>
    /// <param name="type">The artifact's type.</param>
    /// <param name="subtype">
    /// The subtype (PDF 1.7), such as <c>Header</c>, <c>Footer</c> or <c>Watermark</c>, or the PDF 2.0 <c>PageNum</c>, <c>LineNum</c>,
    /// <c>Redaction</c> and <c>Bates</c>; it should be given for pagination artifacts only. <see langword="null"/> for none.
    /// </param>
    /// <param name="attached">The page edges the artifact is attached to: pagination and background artifacts only.</param>
    /// <param name="boundingBox">The artifact's bounding box in default user space, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not defined.</exception>
    /// <exception cref="ArgumentException"><paramref name="subtype"/> is empty, or <paramref name="attached"/> names edges of a layout or page artifact.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2.2, Table 363. A subtype records PDF 1.7, and the PDF 2.0 subtypes PDF 2.0, as the version the document
    /// needs (ADR 0003). End the sequence with <see cref="EndMarkedContent"/>.
    /// </remarks>
    public void BeginArtifact(PdfArtifactType type, string? subtype, PdfArtifactEdges attached, PdfRectangle? boundingBox)
    {
        ThrowIfUndefined(type);
        if (subtype is { Length: 0 })
        {
            throw new ArgumentException("An artifact subtype shall not be empty.", nameof(subtype));
        }

        if ((attached & ~(PdfArtifactEdges.Top | PdfArtifactEdges.Bottom | PdfArtifactEdges.Left | PdfArtifactEdges.Right)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attached), attached, "Not a combination of page edges.");
        }

        if (attached != PdfArtifactEdges.None && type is not (PdfArtifactType.Pagination or PdfArtifactType.Background))
        {
            throw new ArgumentException("Attached is for pagination and full-page background artifacts only (ISO 32000-2 §14.8.2.2.2, Table 363).", nameof(attached));
        }

        ThrowIfPathOpen();
        var list = new CosDictionary { [KnownNames.Type] = ArtifactTypeNames[(int)type] };
        if (boundingBox is { } box)
        {
            list[BBoxKey] = new CosArray { Number(box.Left), Number(box.Bottom), Number(box.Right), Number(box.Top) };
        }

        if (attached != PdfArtifactEdges.None)
        {
            var edges = new CosArray();
            foreach (PdfArtifactEdges edge in (ReadOnlySpan<PdfArtifactEdges>)[PdfArtifactEdges.Top, PdfArtifactEdges.Bottom, PdfArtifactEdges.Left, PdfArtifactEdges.Right])
            {
                if (attached.HasFlag(edge))
                {
                    edges.Add(new CosName(edge.ToString()));
                }
            }

            list[AttachedKey] = edges;
        }

        if (subtype is not null)
        {
            list[FileAndLayerNames.Subtype] = new CosName(subtype);
            _document.RequireFeature(Pdf20ArtifactSubtypes.Contains(subtype, StringComparer.Ordinal)
                ? VersionedFeatures.ArtifactSubtypes20
                : VersionedFeatures.ArtifactSubtype);
        }

        BeginMarked(ArtifactTag.Value, list);
    }

    /// <summary>Begins optional content belonging to <paramref name="group"/> (<c>/OC /name BDC</c>): visible only while the group is ON.</summary>
    /// <param name="group">A group of this document: <see cref="PdfDocument.CreateOptionalContentGroup"/>, or one read from it.</param>
    /// <exception cref="ArgumentException"><paramref name="group"/> belongs to another document or is not an indirect object.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.11.3.2: the group is named in the <c>Properties</c> resources. End it with <see cref="EndMarkedContent"/>; optional content nests.</remarks>
    public void BeginOptionalContent(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        BeginOptional(OptionalContentWriter.ReferenceOf(_document, group.Document, group.Reference, nameof(group)));
    }

    /// <summary>Begins optional content whose visibility <paramref name="membership"/> decides (<c>/OC /name BDC</c>).</summary>
    /// <param name="membership">A membership dictionary of this document: <see cref="PdfDocument.CreateOptionalContentMembership(IEnumerable{PdfOptionalContentGroup}, PdfVisibilityPolicy)"/>, or one read from it.</param>
    /// <exception cref="ArgumentException"><paramref name="membership"/> belongs to another document or is not an indirect object.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.2 and §8.11.3.2. End it with <see cref="EndMarkedContent"/>.</remarks>
    public void BeginOptionalContent(PdfOptionalContentMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        BeginOptional(OptionalContentWriter.ReferenceOf(_document, membership.Document, membership.Reference, nameof(membership)));
    }

    private void BeginOptional(CosReference reference)
    {
        ThrowIfPathOpen();
        _document.RequireFeature(VersionedFeatures.OptionalContent);
        CosName name = _resources.Name(CanvasResources.Properties, "OC", reference);
        _writer.Name(FileAndLayerNames.OC).Name(name).Operator("BDC");
        _marked.Add(_inText);
    }

    private static CosObject Number(double value) => double.IsInteger(value) && Math.Abs(value) < int.MaxValue
        ? new CosInteger((long)value)
        : new CosReal(value);
}
