using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>How the document asks to be presented on the screen and in print: a live view over the catalog's <c>ViewerPreferences</c> dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.2, Tables 147 and 148. Every property reads the dictionary when called and returns the default Table 147 gives
/// when the entry is absent; nothing is ever written to the dictionary. A malformed entry reads as its default and records a
/// <c>ViewerPreferenceInvalid</c> diagnostic the first time it is read (in strict mode, the property throws). Keys the table does
/// not define are ignored: a dictionary may carry private keys.
/// </para>
/// <para>
/// The entries a viewer applies itself (the print entries in particular) are only preferences: Table 147 lets the user override all of
/// them, except the ones <see cref="Enforce"/> names.
/// </para>
/// </remarks>
public sealed class PdfViewerPreferences
{
    private static readonly CosName HideToolbarKey = new("HideToolbar");
    private static readonly CosName HideMenubarKey = new("HideMenubar");
    private static readonly CosName HideWindowUIKey = new("HideWindowUI");
    private static readonly CosName FitWindowKey = new("FitWindow");
    private static readonly CosName CenterWindowKey = new("CenterWindow");
    private static readonly CosName DisplayDocTitleKey = new("DisplayDocTitle");
    private static readonly CosName NonFullScreenPageModeKey = new("NonFullScreenPageMode");
    private static readonly CosName DirectionKey = new("Direction");
    private static readonly CosName ViewAreaKey = new("ViewArea");
    private static readonly CosName ViewClipKey = new("ViewClip");
    private static readonly CosName PrintAreaKey = new("PrintArea");
    private static readonly CosName PrintClipKey = new("PrintClip");
    private static readonly CosName PrintScalingKey = new("PrintScaling");
    private static readonly CosName DuplexKey = new("Duplex");
    private static readonly CosName PickTrayByPdfSizeKey = new("PickTrayByPDFSize");
    private static readonly CosName PrintPageRangeKey = new("PrintPageRange");
    private static readonly CosName NumCopiesKey = new("NumCopies");
    private static readonly CosName EnforceKey = new("Enforce");

    private readonly DictionaryView _view;

    internal PdfViewerPreferences(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _view = new DictionaryView(document, dictionary, reference);
    }

    /// <summary>Gets the viewer preferences dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public CosDictionary Dictionary => _view.Dictionary;

    /// <summary>Gets a value indicating whether the viewer hides its tool bars while the document is active (<c>HideToolbar</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool HideToolbar => ReadFlag(HideToolbarKey);

    /// <summary>Gets a value indicating whether the viewer hides its menu bar while the document is active (<c>HideMenubar</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool HideMenubar => ReadFlag(HideMenubarKey);

    /// <summary>Gets a value indicating whether the viewer hides the window's user interface elements, leaving only the contents (<c>HideWindowUI</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool HideWindowUI => ReadFlag(HideWindowUIKey);

    /// <summary>Gets a value indicating whether the window is resized to fit the first displayed page (<c>FitWindow</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool FitWindow => ReadFlag(FitWindowKey);

    /// <summary>Gets a value indicating whether the window is centred on the screen (<c>CenterWindow</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool CenterWindow => ReadFlag(CenterWindowKey);

    /// <summary>
    /// Gets a value indicating whether the title bar shows the document's title (<c>dc:title</c> of its XMP metadata) instead of the
    /// file name (<c>DisplayDocTitle</c>, PDF 1.4). Default <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool DisplayDocTitle => ReadFlag(DisplayDocTitleKey);

    /// <summary>
    /// Gets the page mode used on leaving full-screen mode (<c>NonFullScreenPageMode</c>): <see cref="PdfPageMode.UseNone"/> (the
    /// default), <see cref="PdfPageMode.UseOutlines"/>, <see cref="PdfPageMode.UseThumbs"/> or <see cref="PdfPageMode.UseOC"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.2, Table 147. Meaningful only when the catalog's page mode is <see cref="PdfPageMode.FullScreen"/>; it "shall
    /// be ignored otherwise", which is the caller's to apply: the value is returned whatever the page mode.
    /// </remarks>
    public PdfPageMode NonFullScreenPageMode => _view.ReadName(
        NonFullScreenPageModeKey,
        DiagnosticCodes.ViewerPreferenceInvalid,
        PdfPageMode.UseNone,
        ("UseNone", PdfPageMode.UseNone),
        ("UseOutlines", PdfPageMode.UseOutlines),
        ("UseThumbs", PdfPageMode.UseThumbs),
        ("UseOC", PdfPageMode.UseOC))!.Value;

    /// <summary>Gets the predominant order of text, which places pages side by side (<c>Direction</c>, PDF 1.3). Default left to right.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public PdfReadingDirection Direction => _view.ReadName(
        DirectionKey,
        DiagnosticCodes.ViewerPreferenceInvalid,
        PdfReadingDirection.LeftToRight,
        ("L2R", PdfReadingDirection.LeftToRight),
        ("R2L", PdfReadingDirection.RightToLeft))!.Value;

    /// <summary>Gets the page boundary displayed on the screen (<c>ViewArea</c>, PDF 1.4, deprecated in PDF 2.0). Default the crop box.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147. A boundary the page does not define takes its default from Table 31.</remarks>
    public PdfPageBoundary ViewArea => ReadBoundary(ViewAreaKey);

    /// <summary>Gets the page boundary page contents are clipped to on the screen (<c>ViewClip</c>, PDF 1.4, deprecated in PDF 2.0). Default the crop box.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public PdfPageBoundary ViewClip => ReadBoundary(ViewClipKey);

    /// <summary>Gets the page boundary rendered when printing (<c>PrintArea</c>, PDF 1.4, deprecated in PDF 2.0). Default the crop box.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public PdfPageBoundary PrintArea => ReadBoundary(PrintAreaKey);

    /// <summary>Gets the page boundary page contents are clipped to when printing (<c>PrintClip</c>, PDF 1.4, deprecated in PDF 2.0). Default the crop box.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public PdfPageBoundary PrintClip => ReadBoundary(PrintClipKey);

    /// <summary>Gets the page scaling a print dialog starts with (<c>PrintScaling</c>, PDF 1.6). Default <see cref="PdfPrintScaling.AppDefault"/>.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147: "If this entry has an unrecognised value, AppDefault shall be used."</remarks>
    public PdfPrintScaling PrintScaling => ReadPrintScaling() ?? PdfPrintScaling.AppDefault;

    /// <summary>Gets the paper handling a print dialog starts with (<c>Duplex</c>, PDF 1.7), or <see langword="null"/>: implementation dependent.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public PdfDuplex? Duplex => _view.ReadName<PdfDuplex>(
        DuplexKey,
        DiagnosticCodes.ViewerPreferenceInvalid,
        null,
        ("Simplex", PdfDuplex.Simplex),
        ("DuplexFlipShortEdge", PdfDuplex.DuplexFlipShortEdge),
        ("DuplexFlipLongEdge", PdfDuplex.DuplexFlipLongEdge));

    /// <summary>
    /// Gets a value indicating whether the page size selects the input paper tray (<c>PickTrayByPDFSize</c>, PDF 1.7), or
    /// <see langword="null"/>: implementation dependent.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool? PickTrayByPdfSize => _view.ReadBoolean(PickTrayByPdfSizeKey, DiagnosticCodes.ViewerPreferenceInvalid, null);

    /// <summary>
    /// Gets the page ranges a print dialog starts with (<c>PrintPageRange</c>, PDF 1.7), numbered from 1, or <see langword="null"/>:
    /// implementation dependent.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.2, Table 147: an even number of integers read in pairs, first and last page. A pair that is not two positive
    /// integers with first not after last, and an element left over from an odd count, are dropped with a diagnostic.
    /// </remarks>
    public IReadOnlyList<PdfPageRange>? PrintPageRange
    {
        get
        {
            CosObject? value = _view.Get(PrintPageRangeKey);
            if (value is null)
            {
                return null;
            }

            if (value is not CosArray array)
            {
                Invalid(PrintPageRangeKey);
                return null;
            }

            var ranges = new List<PdfPageRange>(array.Count / 2);
            bool dropped = array.Count % 2 != 0;
            for (int index = 0; index + 1 < array.Count; index += 2)
            {
                if (_view.Document.Resolve(array[index]) is CosInteger { Value: >= 1 and <= int.MaxValue } first
                    && _view.Document.Resolve(array[index + 1]) is CosInteger { Value: >= 1 and <= int.MaxValue } last
                    && first.Value <= last.Value)
                {
                    ranges.Add(new PdfPageRange((int)first.Value, (int)last.Value));
                }
                else
                {
                    dropped = true;
                }
            }

            if (dropped)
            {
                Invalid(PrintPageRangeKey);
            }

            return ranges;
        }
    }

    /// <summary>Gets the number of copies a print dialog starts with (<c>NumCopies</c>, PDF 1.7), or <see langword="null"/>: implementation dependent, typically 1.</summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147. A value that is not a positive integer is ignored with a diagnostic.</remarks>
    public int? NumCopies
    {
        get
        {
            int? copies = _view.ReadInteger(NumCopiesKey, DiagnosticCodes.ViewerPreferenceInvalid);
            if (copies is < 1)
            {
                Invalid(NumCopiesKey);
                return null;
            }

            return copies;
        }
    }

    /// <summary>Gets the names of the preferences the viewer shall enforce, which the user cannot override (<c>Enforce</c>, PDF 2.0); empty when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.2, Tables 147 and 148. The only name Table 148 defines is <c>PrintScaling</c>, valid only when
    /// <see cref="PrintScaling"/> is given and not <c>AppDefault</c>. Other names are returned as written, with a diagnostic; elements
    /// that are not names are dropped with a diagnostic.
    /// </remarks>
    public IReadOnlyList<CosName> Enforce
    {
        get
        {
            CosObject? value = _view.Get(EnforceKey);
            if (value is null)
            {
                return [];
            }

            if (value is not CosArray array)
            {
                Invalid(EnforceKey);
                return [];
            }

            var names = new List<CosName>(array.Count);
            bool invalid = false;
            foreach (CosObject element in array)
            {
                if (_view.Document.Resolve(element) is CosName name)
                {
                    names.Add(name);
                    invalid |= !PrintScalingKey.Equals(name) || ReadPrintScaling() is not PdfPrintScaling.None;
                }
                else
                {
                    invalid = true;
                }
            }

            if (invalid)
            {
                Invalid(EnforceKey);
            }

            return names;
        }
    }

    private bool ReadFlag(CosName key) => _view.ReadBoolean(key, DiagnosticCodes.ViewerPreferenceInvalid, false)!.Value;

    private PdfPageBoundary ReadBoundary(CosName key) => _view.ReadName(
        key,
        DiagnosticCodes.ViewerPreferenceInvalid,
        PdfPageBoundary.CropBox,
        ("CropBox", PdfPageBoundary.CropBox),
        ("MediaBox", PdfPageBoundary.MediaBox),
        ("BleedBox", PdfPageBoundary.BleedBox),
        ("TrimBox", PdfPageBoundary.TrimBox),
        ("ArtBox", PdfPageBoundary.ArtBox))!.Value;

    /// <summary>The valid PrintScaling value, or null when absent or unrecognised (reported).</summary>
    private PdfPrintScaling? ReadPrintScaling() => _view.ReadName<PdfPrintScaling>(
        PrintScalingKey,
        DiagnosticCodes.ViewerPreferenceInvalid,
        null,
        ("None", PdfPrintScaling.None),
        ("AppDefault", PdfPrintScaling.AppDefault));

    private void Invalid(CosName key) =>
        _view.Report(DiagnosticCodes.ViewerPreferenceInvalid, $"The viewer preference {key.Value} does not have a value Table 147 allows; it is read as its default.");
}
