using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>A destination: a view of a document to show, given explicitly by an array or indirectly by name. A live view.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.2. An explicit destination (<see cref="PdfExplicitDestination"/>, §12.3.2.2) is an array naming a page (or a
/// structure element, §12.3.2.3) and how to show it; a named destination (<see cref="PdfNamedDestination"/>, §12.3.2.4) is a name
/// (PDF 1.1) or byte string (PDF 1.2) looked up in the document's named destinations.
/// </para>
/// <para>
/// A destination is remote when it belongs to a remote or embedded go-to action (§12.6.4.3, §12.6.4.4): its page is a page number
/// and its names are looked up in another document, never in this one.
/// </para>
/// </remarks>
public abstract class PdfDestination
{
    private protected PdfDestination(PdfDocument document, CosObject value, bool isRemote, CosReference? owner)
    {
        Document = document;
        Value = value;
        IsRemote = isRemote;
        Owner = owner;
    }

    /// <summary>Gets the destination as stored: an array for an explicit destination, a name or a string for a named one.</summary>
    public CosObject Value { get; }

    /// <summary>Gets a value indicating whether the destination is in another document (a remote or embedded go-to action's).</summary>
    /// <remarks>ISO 32000-2 §12.3.2.2 and §12.6.4.3.</remarks>
    public bool IsRemote { get; }

    private protected PdfDocument Document { get; }

    /// <summary>Gets the indirect object the destination was read from, for diagnostics.</summary>
    private protected CosReference? Owner { get; }

    /// <summary>Reads the value of a <c>Dest</c> or <c>D</c> entry as a destination.</summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The entry's value: an array, a name or a string, or a reference to one.</param>
    /// <param name="isRemote">Whether the destination is in another document.</param>
    /// <param name="owner">The indirect object holding the entry, for diagnostics.</param>
    /// <returns>The destination, or <see langword="null"/> when the value is absent, or of no destination type (with a diagnostic).</returns>
    internal static PdfDestination? Create(PdfDocument document, CosObject? value, bool isRemote, CosReference? owner)
    {
        CosObject resolved = document.Resolve(value);
        CosReference? reference = value as CosReference ?? owner;
        switch (resolved)
        {
            case CosArray array:
                return new PdfExplicitDestination(document, array, isRemote, reference);
            case CosName or CosString:
                return new PdfNamedDestination(document, resolved, isRemote, owner);
            case CosNull:
                return null;
            default:
                document.DiagnosticSink.Report(
                    DiagnosticCodes.DestinationInvalid,
                    DiagnosticSeverity.Warning,
                    "A destination is not an array, a name or a string; it is ignored.",
                    objectReference: reference);
                return null;
        }
    }
}
