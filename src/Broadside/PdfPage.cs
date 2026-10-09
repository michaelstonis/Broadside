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

    /// <summary>Gets the files associated with the page (<c>AF</c>).</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (PDF 2.0), and §14.13.4. Read on every call; see <see cref="PdfDocument.ReadAssociatedFiles"/>.</remarks>
    public IReadOnlyList<PdfFileSpecification> AssociatedFiles => _document.ReadAssociatedFiles(Dictionary, Reference);

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
