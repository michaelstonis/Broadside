using Broadside.Objects;

namespace Broadside;

/// <summary>An action of a type this version does not model yet (any Table 201 type but GoTo and URI, or a type the table does not list).</summary>
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
