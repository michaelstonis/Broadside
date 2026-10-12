using Broadside.Objects;

namespace Broadside;

/// <summary>An action whose type Table 201 does not list, such as an extension's; every listed type has its own model.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.1, Table 201. Kept as it is (parse-and-preserve) and reachable through <see cref="PdfAction.Dictionary"/>;
/// <see cref="PdfAction.ActionType"/> says which type it is.
/// </remarks>
public sealed class PdfUnknownAction : PdfAction
{
    internal PdfUnknownAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Unknown;
}
