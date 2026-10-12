using Broadside.Objects;

namespace Broadside;

/// <summary>A go-to-3D-view action: sets the view of a 3D annotation. Kept as data; 3D artwork is out of scope.</summary>
/// <remarks>ISO 32000-2 §12.6.4.16, Table 220 (PDF 1.6), and §13.6 (3D artwork, exposed raw).</remarks>
public sealed class PdfGoTo3DViewAction : PdfAction
{
    internal PdfGoTo3DViewAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.GoTo3DView;

    /// <summary>Gets the 3D annotation whose view to set (<c>TA</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220.</remarks>
    public CosDictionary? Annotation => ReadDictionary(ActionNames.TA);

    /// <summary>Gets the view (<c>V</c>, required) as stored: a 3D view dictionary, an index, a view name or a keyword name; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220.</remarks>
    public CosObject? View => Get(ActionNames.V);

    /// <summary>Gets the 3D view dictionary when <c>V</c> is one, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220, and §13.6.4, Table 315.</remarks>
    public CosDictionary? ViewDictionary => View as CosDictionary;

    /// <summary>Gets the index into the 3D stream's <c>VA</c> array when <c>V</c> is an integer, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220.</remarks>
    public int? ViewIndex => View is CosInteger { Value: >= 0 and <= int.MaxValue } index ? (int)index.Value : null;

    /// <summary>Gets the view's internal name (its <c>IN</c> entry) when <c>V</c> is a text string, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220.</remarks>
    public string? ViewName => (View as CosString)?.DecodeText();

    /// <summary>Gets the keyword when <c>V</c> is a name: <c>F</c> first, <c>L</c> last, <c>N</c> next, <c>P</c> previous or <c>D</c> default; else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.16, Table 220.</remarks>
    public CosName? ViewKeyword => View as CosName;

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("3D artwork");
        if (!Dictionary.ContainsKey(ActionNames.TA) || !Dictionary.ContainsKey(ActionNames.V))
        {
            Report("A go-to-3D-view action shall have a TA and a V entry; it lacks one, so it does nothing.");
        }
    }
}
