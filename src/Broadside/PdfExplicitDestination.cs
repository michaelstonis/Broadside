using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>An explicit destination: <c>[page /View parameters...]</c>. A live view over the array.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.2.2, Table 149, and §12.3.2.3. The first element is the target: a page object, or for a remote destination a
/// 0-based page number in the other document; a structure element (or, remote, its ID) makes it a structure destination (PDF 2.0).
/// The second element names the view (<see cref="View"/>); the rest are its parameters in default user space. A parameter that is
/// null means "keep the current value", as does an <c>XYZ</c> zoom of 0, so <see cref="Left"/>, <see cref="Top"/> and
/// <see cref="Zoom"/> are <see langword="null"/> then; a parameter the view does not have is <see langword="null"/> too.
/// </para>
/// <para>
/// Read leniently, each with a diagnostic when the destination is read: missing trailing parameters read as null, extra ones are
/// ignored, a parameter that is not a number reads as null, an unknown view name reads as <see cref="PdfDestinationView.Unknown"/>
/// (the page is still available), a <c>FitR</c> without four numbers is not valid, and a local destination whose page is an
/// integer (a common producer error) uses it as a 0-based page index. A page that is not in the page tree has no
/// <see cref="PageIndex"/>, with a diagnostic when it is asked for.
/// </para>
/// </remarks>
public sealed class PdfExplicitDestination : PdfDestination
{
    internal PdfExplicitDestination(PdfDocument document, CosArray array, bool isRemote, CosReference? owner)
        : base(document, array, isRemote, owner)
    {
        Array = array;
        Check();
    }

    /// <summary>Gets the destination array.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public CosArray Array { get; }

    /// <summary>Gets the first element of the array as stored (a page reference, a page number, a structure element reference or ID), or null.</summary>
    public CosObject Target => Array.Count > 0 ? Array[0] : CosNull.Instance;

    /// <summary>Gets what the first element designates.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2 and §12.3.2.3.</remarks>
    public PdfDestinationTarget TargetKind
    {
        get
        {
            CosObject target = Target;
            switch (target)
            {
                case CosInteger:
                    return PdfDestinationTarget.PageNumber;
                case CosString:
                    return IsRemote ? PdfDestinationTarget.StructureElementId : PdfDestinationTarget.None;
                case CosReference or CosDictionary when !IsRemote:
                    return Document.Resolve(target) is CosDictionary node
                        ? Document.Pages.IndexOf(node) < 0 && IsStructureElement(node) ? PdfDestinationTarget.StructureElement : PdfDestinationTarget.Page
                        : PdfDestinationTarget.None;
                default:
                    return PdfDestinationTarget.None;
            }
        }
    }

    /// <summary>Gets the page number the first element gives, when it is an integer; otherwise <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2: for a remote destination, the 0-based number of a page in the other document.</remarks>
    public int? PageNumber => Target is CosInteger { Value: >= int.MinValue and <= int.MaxValue } number ? (int)number.Value : null;

    /// <summary>Gets the page of this document the destination shows, or <see langword="null"/> when it is remote or its page is not in the page tree.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2 and §12.3.2.3: a structure destination's page is found from its structure element.</remarks>
    public PdfPage? Page => PageIndex is { } index ? Document.Pages[index] : null;

    /// <summary>Gets the 0-based index of <see cref="Page"/> in <see cref="PdfDocument.Pages"/>, or <see langword="null"/>.</summary>
    /// <exception cref="DiagnosticException">In strict mode, when the destination names a page that is not in the page tree.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.2. A remote destination's page number is never resolved in this document. A local destination whose first
    /// element is an integer uses it as the index, with a diagnostic when the destination is read. A structure destination
    /// (§12.3.2.3) uses the page of the first marked-content or object reference among its element's kids, processed in array order
    /// and descending into child elements; when none identifies a page, the first page.
    /// </remarks>
    public int? PageIndex
    {
        get
        {
            if (IsRemote)
            {
                return null;
            }

            CosObject target = Target;
            if (target is CosInteger number)
            {
                if (number.Value >= 0 && number.Value < Document.Pages.Count)
                {
                    return (int)number.Value;
                }

                ReportPageNotFound("A destination's page number is not the index of a page of the document.");
                return null;
            }

            if (Document.Resolve(target) is not CosDictionary node)
            {
                return null;
            }

            int index = Document.Pages.IndexOf(node);
            if (index >= 0)
            {
                return index;
            }

            if (IsStructureElement(node))
            {
                return StructurePageIndex(node, target as CosReference);
            }

            ReportPageNotFound("A destination refers to a page object that is not in the page tree.");
            return null;
        }
    }

    /// <summary>Gets the structure element the destination designates (a structure destination), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.3 (PDF 2.0).</remarks>
    public CosDictionary? StructureElement =>
        !IsRemote && Document.Resolve(Target) is CosDictionary node && Document.Pages.IndexOf(node) < 0 && IsStructureElement(node) ? node : null;

    /// <summary>Gets the ID of the structure element in another document a remote structure destination designates, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.3, Table 355 (ID).</remarks>
    public CosString? StructureElementId => IsRemote ? Target as CosString : null;

    /// <summary>Gets the view the destination asks for.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public PdfDestinationView View => ViewOf(ViewName);

    /// <summary>Gets the view name as stored, or <see langword="null"/> when the second element is not a name.</summary>
    public CosName? ViewName => Array.Count > 1 ? Document.Resolve(Array[1]) as CosName : null;

    /// <summary>Gets the left coordinate (<c>XYZ</c>, <c>FitV</c>, <c>FitBV</c>, <c>FitR</c>), or <see langword="null"/> to keep the current one.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149; default user space.</remarks>
    public double? Left => Parameter(Slot.Left);

    /// <summary>Gets the top coordinate (<c>XYZ</c>, <c>FitH</c>, <c>FitBH</c>, <c>FitR</c>), or <see langword="null"/> to keep the current one.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149; default user space.</remarks>
    public double? Top => Parameter(Slot.Top);

    /// <summary>Gets the right coordinate of a <c>FitR</c> rectangle, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149; default user space.</remarks>
    public double? Right => Parameter(Slot.Right);

    /// <summary>Gets the bottom coordinate of a <c>FitR</c> rectangle, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149; default user space.</remarks>
    public double? Bottom => Parameter(Slot.Bottom);

    /// <summary>Gets the magnification of an <c>XYZ</c> destination, or <see langword="null"/> to keep the current one (null or 0 in the file).</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149: "A zoom value of 0 has the same meaning as a null value".</remarks>
    public double? Zoom => Parameter(Slot.Zoom) is { } zoom && zoom != 0 ? zoom : null;

    /// <summary>
    /// Gets a value indicating whether the destination can be shown: a known view, a target of an allowed type and, for <c>FitR</c>, a
    /// rectangle of four numbers.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public bool IsValid =>
        View != PdfDestinationView.Unknown
        && TargetKind != PdfDestinationTarget.None
        && (View != PdfDestinationView.FitR || (Left, Bottom, Right, Top) is ({ }, { }, { }, { }));

    /// <summary>Whether a dictionary is a structure element rather than a page (§14.7.2, Table 355).</summary>
    private static bool IsStructureElement(CosDictionary node) =>
        node.TryGetValue(KnownNames.Type, out CosObject? type)
            ? NavigationNames.StructElem.Equals(type)
            : node.ContainsKey(NavigationNames.S) && node.ContainsKey(NavigationNames.P);

    /// <summary>
    /// The page of a structure destination (§12.3.2.3): the page of the first marked-content or object reference found among the
    /// element's kids in array order, descending into child elements; the first page when none is found.
    /// </summary>
    private int? StructurePageIndex(CosDictionary element, CosReference? reference)
    {
        if (Document.Pages.Count == 0)
        {
            return null;
        }

        PdfPage? page = Document.StructureTree is { } tree ? FirstContentPage(tree.GetElement(element, reference)) : null;
        return page is not null && Document.Pages.IndexOf(page.Dictionary) is >= 0 and var index ? index : 0;
    }

    /// <summary>The page of the first content item under <paramref name="element"/> whose page is known, depth first in <c>K</c> order.</summary>
    private static PdfPage? FirstContentPage(Structure.PdfStructureElement element)
    {
        foreach (Structure.PdfStructureItem kid in element.Children)
        {
            switch (kid)
            {
                case Structure.PdfMarkedContentReference { Page: { } page }:
                    return page;
                case Structure.PdfObjectReference { Page: { } page }:
                    return page;
                case Structure.PdfStructureElement child when FirstContentPage(child) is { } found:
                    return found;
            }
        }

        return null;
    }

    private static PdfDestinationView ViewOf(CosName? name) => name?.Value switch
    {
        "XYZ" => PdfDestinationView.Xyz,
        "Fit" => PdfDestinationView.Fit,
        "FitH" => PdfDestinationView.FitH,
        "FitV" => PdfDestinationView.FitV,
        "FitR" => PdfDestinationView.FitR,
        "FitB" => PdfDestinationView.FitB,
        "FitBH" => PdfDestinationView.FitBH,
        "FitBV" => PdfDestinationView.FitBV,
        _ => PdfDestinationView.Unknown,
    };

    /// <summary>The parameters a view takes, in array order after the view name.</summary>
    private static Slot[] SlotsOf(PdfDestinationView view) => view switch
    {
        PdfDestinationView.Xyz => [Slot.Left, Slot.Top, Slot.Zoom],
        PdfDestinationView.FitH or PdfDestinationView.FitBH => [Slot.Top],
        PdfDestinationView.FitV or PdfDestinationView.FitBV => [Slot.Left],
        PdfDestinationView.FitR => [Slot.Left, Slot.Bottom, Slot.Right, Slot.Top],
        _ => [],
    };

    private double? Parameter(Slot slot)
    {
        int position = System.Array.IndexOf(SlotsOf(View), slot);
        if (position < 0 || position + 2 >= Array.Count)
        {
            return null;
        }

        return Document.Resolve(Array[position + 2]) is CosNumber number && double.IsFinite(number.ToDouble()) ? number.ToDouble() : null;
    }

    /// <summary>Reports what is wrong with the array, once, when the destination is read.</summary>
    private void Check()
    {
        PdfDestinationView view = View;
        if (Array.Count < 2 || ViewName is null)
        {
            Report("A destination array shall be [page /View parameters...]; it has no view name, so it cannot be shown.");
            return;
        }

        if (view == PdfDestinationView.Unknown)
        {
            Report(string.Create(CultureInfo.InvariantCulture, $"A destination's view /{ViewName.Value} is not one of Table 149; the destination cannot be shown."));
        }

        Slot[] slots = SlotsOf(view);
        int given = Array.Count - 2;
        if (view != PdfDestinationView.Unknown && given != slots.Length)
        {
            Report(given < slots.Length
                ? view == PdfDestinationView.FitR
                    ? "A FitR destination shall have four coordinates; it has fewer, so it cannot be shown."
                    : "A destination has fewer parameters than its view takes; the missing ones read as null (keep the current value)."
                : "A destination has more parameters than its view takes; the extra ones are ignored.");
        }

        for (int index = 0; index < Math.Min(given, slots.Length); index++)
        {
            CosObject parameter = Document.Resolve(Array[index + 2]);
            if (parameter is CosNull && view == PdfDestinationView.FitR)
            {
                Report("A FitR destination's coordinates shall be numbers; one is null, so it cannot be shown.");
            }
            else if (parameter is not (CosNumber or CosNull))
            {
                Report("A destination parameter is not a number or null; it reads as null (keep the current value).");
            }
        }

        switch (Target)
        {
            case CosInteger when !IsRemote:
                Report("A destination in this document shall refer to its page by an indirect reference; it gives a page number, used as a 0-based page index.");
                break;
            case CosReference or CosDictionary when IsRemote:
            case CosString when !IsRemote:
                Report("A destination's first element is of no type its context allows; it designates no page.");
                break;
            case CosReference or CosDictionary or CosInteger or CosString:
                break;
            default:
                Report("A destination's first element is not a page, a page number or a structure element; it designates no page.");
                break;
        }
    }

    private void Report(string message) =>
        Document.DiagnosticSink.Report(DiagnosticCodes.DestinationInvalid, DiagnosticSeverity.Warning, message, objectReference: Owner);

    private void ReportPageNotFound(string message) =>
        Document.DiagnosticSink.Report(DiagnosticCodes.DestinationPageNotFound, DiagnosticSeverity.Warning, message + " It shows no page.", objectReference: Owner);

    /// <summary>A parameter of Table 149.</summary>
    private enum Slot
    {
        Left,
        Top,
        Right,
        Bottom,
        Zoom,
    }
}
