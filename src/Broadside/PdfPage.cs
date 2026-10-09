using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside;

/// <summary>One page of a document: a live view over a page object and the page tree nodes it inherits attributes from.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (page objects, Table 31), §7.7.3.4 (inheritance) and §14.11.2 (page boundaries). Every property reads the COS
/// objects when called, so a change made through <see cref="Dictionary"/> or an ancestor node is visible at once.
/// </para>
/// <para>
/// <c>Resources</c>, <c>MediaBox</c>, <c>CropBox</c> and <c>Rotate</c> are inherited: when the page does not have the entry, the
/// nearest ancestor node that has it supplies it, as-is and never merged (§7.7.3.4). Ancestors are the nodes on the path from the
/// page tree root that the page was found through. A value that is missing or malformed reads as the default the specification
/// gives; the reader records a diagnostic for it when it first walks the page tree.
/// </para>
/// </remarks>
public sealed class PdfPage
{
    /// <summary>The media box used when a page has none, US Letter, as pdf.js and PDFBox use.</summary>
    private static readonly PdfRectangle DefaultMediaBox = new(0, 0, 612, 792);

    private readonly PdfDocument _document;
    private readonly PageTreeAncestor? _ancestors;
    private volatile AnnotationList? _annotations;

    internal PdfPage(PdfDocument document, CosDictionary dictionary, CosReference? reference, PageTreeAncestor? ancestors)
    {
        _document = document;
        _ancestors = ancestors;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the page object.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the page object, or <see langword="null"/> when the page tree holds it directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the page's additional actions (<c>AA</c>, PDF 1.2): actions when the page opens and closes; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §12.6.3, Table 198. Not inheritable; read on every call, never created by reading.</remarks>
    public PdfPageAdditionalActions? AdditionalActions =>
        PdfAdditionalActions.Create(_document, Dictionary.TryGetValue(ActionNames.AA, out CosObject? aa) ? aa : null, Reference, static (d, a, r, o) => new PdfPageAdditionalActions(d, a, r, o));

    /// <summary>Gets the media box: the boundaries of the physical medium the page is displayed or printed on. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Required; when missing or malformed, US Letter <c>[0 0 612 792]</c>.</remarks>
    public PdfRectangle MediaBox => ReadBox(KnownNames.MediaBox, inheritable: true, out PdfRectangle box) == PageAttributeState.Valid ? box : DefaultMediaBox;

    /// <summary>Gets the crop box: the region the page's contents are clipped to when displayed or printed. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the media box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle CropBox => ClipToMediaBox(KnownNames.CropBox, inheritable: true, MediaBox);

    /// <summary>Gets the bleed box: the region the contents are clipped to in a production environment.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle BleedBox => ClipToMediaBox(KnownNames.BleedBox, inheritable: false, CropBox);

    /// <summary>Gets the trim box: the intended dimensions of the finished page after trimming.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle TrimBox => ClipToMediaBox(KnownNames.TrimBox, inheritable: false, CropBox);

    /// <summary>Gets the art box: the extent of the page's meaningful content.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle ArtBox => ClipToMediaBox(KnownNames.ArtBox, inheritable: false, CropBox);

    /// <summary>Gets the number of degrees the page is rotated clockwise when displayed or printed: 0, 90, 180 or 270. Inheritable.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.3.3, Table 31. The entry shall be a multiple of 90; it is normalized into 0 to 270 (−90 reads as 270, 450 as
    /// 90). A value that is not a multiple of 90 reads as 0.
    /// </remarks>
    public int Rotation => ReadRotation(out int degrees) is PageAttributeState.Valid or PageAttributeState.Repaired ? degrees : 0;

    /// <summary>Gets the resource dictionary the page's content streams use, or <see langword="null"/> when the page has none. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §7.8.3.</remarks>
    public CosDictionary? Resources => FindInheritable(KnownNames.Resources) as CosDictionary;

    /// <summary>Gets the size of a default user space unit in multiples of 1/72 inch. 1.0 when absent or not positive.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (PDF 1.6).</remarks>
    public double UserUnit => ReadUserUnit(out double value) == PageAttributeState.Valid ? value : 1.0;

    /// <summary>Returns the font the page's resources name <paramref name="resourceName"/>, as the <c>Tf</c> operator selects it.</summary>
    /// <param name="resourceName">The key in the <c>Font</c> subdictionary of the page's (possibly inherited) resource dictionary, without the slash.</param>
    /// <returns>The font view, or <see langword="null"/> when the resources have no font of that name.</returns>
    /// <exception cref="DiagnosticException">In strict mode, when the font dictionary's <c>Type</c> or <c>Subtype</c> is not a font's.</exception>
    /// <remarks>ISO 32000-2 §7.8.3, Table 34 (<c>Font</c>), and §9.5. The same font dictionary always gives the same view.</remarks>
    public PdfFont? GetFont(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Resources is { } resources
            && resources.TryGetValue(FontNames.Font, out CosObject? fonts)
            && _document.Resolve(fonts) is CosDictionary fontResources
            && fontResources.TryGetValue(new CosName(resourceName), out CosObject? font)
            ? _document.GetFont(font)
            : null;
    }
    /// <summary>Gets the files associated with the page (<c>AF</c>).</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (PDF 2.0), and §14.13.4. Read on every call; see <see cref="PdfDocument.ReadAssociatedFiles"/>.</remarks>
    public IReadOnlyList<PdfFileSpecification> AssociatedFiles => _document.ReadAssociatedFiles(Dictionary, Reference);

    /// <summary>Gets the page's annotations, in the order of its <c>Annots</c> array, each typed by its subtype.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the <c>Annots</c> array.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.7.3.3, Table 31 (<c>Annots</c>), and §12.5. The list is built on first use and rebuilt when the page's
    /// <c>Annots</c> entry or array, or an annotation's <c>Subtype</c>, changes; each annotation is a live view over its dictionary, and the same dictionary always
    /// gives the same instance. An element that is not an annotation dictionary is skipped; one listed twice appears twice. An
    /// <c>Annots</c> entry on a page tree node above the page, which is not inheritable, is used as viewers use it.
    /// </para>
    /// <para>Deviations in the array are recorded in <see cref="PdfDocument.Diagnostics"/> when the list is built.</para>
    /// </remarks>
    public IReadOnlyList<PdfAnnotation> Annotations
    {
        get
        {
            (CosObject? entry, bool inherited) = FindAnnots();
            var array = _document.Resolve(entry) as CosArray;
            AnnotationList? cached = _annotations;
            if (cached is not null && ReferenceEquals(cached.Entry, entry) && ReferenceEquals(cached.Array, array) && cached.ArrayVersion == (array?.Version ?? 0)
                && cached.Items.All(annotation => Equals(annotation.CreatedSubtype, PdfAnnotation.ReadSubtype(_document, annotation.Dictionary, out _))))
            {
                return cached.Items;
            }

            IReadOnlyList<PdfAnnotation> items = _document.AnnotationIndex.Read(this, entry, inherited);
            _annotations = new AnnotationList(entry, array, array?.Version ?? 0, items);
            return items;
        }
    }

    /// <summary>Runs the page's content through the content interpreter and reports what it paints to <paramref name="processor"/>.</summary>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.8.2, §8.2 to §8.5. The content is the page's <c>Contents</c> stream, or the streams of its <c>Contents</c>
    /// array read as one (Table 31), decoded through their filters; the names it uses resolve in <see cref="Resources"/>. Geometry is
    /// reported in user space with the CTM to default user space, starting at the identity: the media box, crop box, rotation and
    /// user unit are the consumer's to apply. In lenient mode malformed content is repaired and recorded in
    /// <see cref="PdfDocument.Diagnostics"/>, once per kind of deviation per content stream. Reads the page live, on every call.
    /// </remarks>
    public void ProcessContent(ContentProcessor processor) => ProcessContent(processor, ContentInterpreter.DefaultOptions);

    /// <summary>Runs the page's content through the content interpreter, with limits and cancellation.</summary>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <param name="options">Limits and cancellation for the run.</param>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <exception cref="OperationCanceledException">The options' cancellation token was canceled.</exception>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2 to §8.5. As <see cref="ProcessContent(ContentProcessor)"/>.</remarks>
    public void ProcessContent(ContentProcessor processor, ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        ContentInterpreter.RunPage(this, _document, processor, options);
    }

    /// <summary>Reads a page boundary; for an inheritable one, from the nearest node that has it.</summary>
    internal PageAttributeState ReadBox(CosName key, bool inheritable, out PdfRectangle box)
    {
        box = default;
        CosObject? value = inheritable ? FindInheritable(key) : Find(key);
        if (value is null)
        {
            return PageAttributeState.Absent;
        }

        if (_document.Resolve(value) is not CosArray { Count: 4 } array)
        {
            return PageAttributeState.Invalid;
        }

        Span<double> corners = stackalloc double[4];
        for (int index = 0; index < 4; index++)
        {
            if (_document.Resolve(array[index]) is not CosNumber number)
            {
                return PageAttributeState.Invalid;
            }

            corners[index] = number.ToDouble();
        }

        box = new PdfRectangle(corners[0], corners[1], corners[2], corners[3]);
        return PageAttributeState.Valid;
    }

    /// <summary>Reads <c>Rotate</c> normalized into 0 to 270; a real holding a multiple of 90 is <see cref="PageAttributeState.Repaired"/>.</summary>
    internal PageAttributeState ReadRotation(out int degrees)
    {
        degrees = 0;
        switch (FindInheritable(KnownNames.Rotate))
        {
            case null:
                return PageAttributeState.Absent;
            case CosInteger { Value: var value } when value % 90 == 0:
                degrees = (int)(((value % 360) + 360) % 360);
                return PageAttributeState.Valid;
            case CosReal { Value: var value } when value % 90 == 0 && Math.Abs(value) <= int.MaxValue:
                degrees = (((int)value % 360) + 360) % 360;
                return PageAttributeState.Repaired;
            default:
                return PageAttributeState.Invalid;
        }
    }

    /// <summary>Reads <c>Resources</c>.</summary>
    internal PageAttributeState ReadResources() => FindInheritable(KnownNames.Resources) switch
    {
        null => PageAttributeState.Absent,
        CosDictionary => PageAttributeState.Valid,
        _ => PageAttributeState.Invalid,
    };

    /// <summary>Reads <c>UserUnit</c>, which shall be positive.</summary>
    internal PageAttributeState ReadUserUnit(out double value)
    {
        value = 1.0;
        switch (Find(KnownNames.UserUnit))
        {
            case null:
                return PageAttributeState.Absent;
            case CosNumber number when number.ToDouble() > 0:
                value = number.ToDouble();
                return PageAttributeState.Valid;
            default:
                return PageAttributeState.Invalid;
        }
    }

    /// <summary>A box that defaults to <paramref name="fallback"/>, reduced to its intersection with the media box (§14.11.2).</summary>
    private PdfRectangle ClipToMediaBox(CosName key, bool inheritable, PdfRectangle fallback)
    {
        if (ReadBox(key, inheritable, out PdfRectangle box) != PageAttributeState.Valid)
        {
            return fallback;
        }

        return box.Intersect(MediaBox) ?? fallback;
    }

    /// <summary>The page's <c>Annots</c> entry as stored, else the nearest ancestor's (not inheritable, but read as viewers read it).</summary>
    private (CosObject? Entry, bool Inherited) FindAnnots()
    {
        if (Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? own))
        {
            return (own, false);
        }

        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (node.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? value))
            {
                return (value, true);
            }
        }

        return (null, false);
    }

    /// <summary>The page's own entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    private CosObject? Find(CosName key) =>
        Dictionary.TryGetValue(key, out CosObject? value) && _document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>The page's entry, or the nearest ancestor's, resolved, used as-is (§7.7.3.4).</summary>
    private CosObject? FindInheritable(CosName key)
    {
        if (Find(key) is { } own)
        {
            return own;
        }

        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (node.Dictionary.TryGetValue(key, out CosObject? value) && _document.Resolve(value) is not CosNull and var resolved)
            {
                return resolved;
            }
        }

        return null;
    }
}

/// <summary>A page tree node on the path from the root to a page, nearest first: the nodes a page inherits from (§7.7.3.4).</summary>
/// <param name="Dictionary">The node's dictionary.</param>
/// <param name="Parent">The node above it, or <see langword="null"/> for the root.</param>
internal sealed record PageTreeAncestor(CosDictionary Dictionary, PageTreeAncestor? Parent);

/// <summary>A page's annotation list and the <c>Annots</c> entry, array and array version it was built from.</summary>
internal sealed record AnnotationList(CosObject? Entry, CosArray? Array, int ArrayVersion, IReadOnlyList<PdfAnnotation> Items);

/// <summary>What reading a page attribute found.</summary>
internal enum PageAttributeState : byte
{
    /// <summary>The entry is absent; the default applies.</summary>
    Absent,

    /// <summary>The entry is well-formed.</summary>
    Valid,

    /// <summary>The entry deviates from its type but its value can be used.</summary>
    Repaired,

    /// <summary>The entry is malformed; the default applies.</summary>
    Invalid,
}
