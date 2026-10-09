using Broadside.Objects;

namespace Broadside;

/// <summary>A thread action: jumps to an article thread (bead) in this or another document.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.7, Table 209 (PDF 1.1), and §12.4.3 (articles, exposed raw). The thread is given by its dictionary
/// (<see cref="ThreadDictionary"/>), by its index in the catalog's <c>Threads</c> array (<see cref="ThreadIndex"/>) or by its title
/// (<see cref="ThreadTitle"/>); the bead likewise by dictionary or index. When <see cref="File"/> is present they belong to that file.
/// </remarks>
public sealed class PdfThreadAction : PdfAction
{
    internal PdfThreadAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Thread;

    /// <summary>Gets the file holding the thread (<c>F</c>), or <see langword="null"/>: this document.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209, and §7.11.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <summary>Gets the thread (<c>D</c>, required) as stored: a thread dictionary, an index or a title; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209.</remarks>
    public CosObject? Thread => Get(NavigationNames.D);

    /// <summary>Gets the thread dictionary when <c>D</c> is one, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209, and §12.4.3, Table 162.</remarks>
    public CosDictionary? ThreadDictionary => Thread as CosDictionary;

    /// <summary>Gets the 0-based index of the thread in the catalog's <c>Threads</c> array when <c>D</c> is an integer, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209.</remarks>
    public int? ThreadIndex => Thread is CosInteger { Value: >= 0 and <= int.MaxValue } index ? (int)index.Value : null;

    /// <summary>Gets the title of the thread when <c>D</c> is a text string, else <see langword="null"/>; the first thread with that title is meant.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209.</remarks>
    public string? ThreadTitle => (Thread as CosString)?.DecodeText();

    /// <summary>Gets the bead (<c>B</c>) as stored: a bead dictionary or an index into the thread's beads; <see langword="null"/>: the first bead.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209.</remarks>
    public CosObject? Bead => Get(ActionNames.B);

    /// <summary>Gets the bead dictionary when <c>B</c> is one, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209, and §12.4.3, Table 163.</remarks>
    public CosDictionary? BeadDictionary => Bead as CosDictionary;

    /// <summary>Gets the 0-based index of the bead in the thread when <c>B</c> is an integer, else <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.7, Table 209.</remarks>
    public int? BeadIndex => Bead is CosInteger { Value: >= 0 and <= int.MaxValue } index ? (int)index.Value : null;

    /// <inheritdoc/>
    private protected override void Check()
    {
        switch (Thread)
        {
            case null:
                Report("A thread action shall have a D entry naming the thread; it has none, so it goes nowhere.");
                break;
            case CosDictionary or CosInteger or CosString:
                break;
            default:
                ReportEntry(NavigationNames.D, "a thread dictionary, an integer or a text string");
                break;
        }

        if (Bead is not (null or CosDictionary or CosInteger))
        {
            ReportEntry(ActionNames.B, "a bead dictionary or an integer");
        }
    }
}
