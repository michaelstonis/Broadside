using Broadside.Objects;

namespace Broadside;

/// <summary>A named action: one of the predefined actions such as going to the next page.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.12, Tables 215 and 216 (PDF 1.2). Names Table 215 does not list are allowed (not portable): they read as
/// <see cref="PdfNamedOperation.Other"/> without a diagnostic, and a processor that does not recognise one "shall take no action".
/// </remarks>
public sealed class PdfNamedAction : PdfAction
{
    internal PdfNamedAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Named;

    /// <summary>Gets the name of the action (<c>N</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.12, Table 216.</remarks>
    public CosName? Name => ReadName(ActionNames.N);

    /// <summary>Gets the predefined action <see cref="Name"/> names, or <see cref="PdfNamedOperation.Other"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.12, Table 215.</remarks>
    public PdfNamedOperation Operation => Name?.Value switch
    {
        "NextPage" => PdfNamedOperation.NextPage,
        "PrevPage" => PdfNamedOperation.PreviousPage,
        "FirstPage" => PdfNamedOperation.FirstPage,
        "LastPage" => PdfNamedOperation.LastPage,
        _ => PdfNamedOperation.Other,
    };

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(ActionNames.N))
        {
            Report("A named action shall have an N entry; it has none, so it does nothing.");
        }
    }
}
