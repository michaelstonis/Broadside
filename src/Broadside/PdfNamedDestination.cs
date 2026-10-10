using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>A destination referred to by name: a name object (PDF 1.1) or a byte string (PDF 1.2). A live view.</summary>
/// <remarks>
/// ISO 32000-2 §12.3.2.4. <see cref="Resolve"/> looks the name up in this document: a name in the catalog's <c>Dests</c> dictionary
/// first, a string in the name dictionary's <c>Dests</c> tree first, then each in the other (files mix the two), comparing bytes
/// (Annex J.3.3, J.3.4). A remote named destination is in another document and does not resolve here.
/// </remarks>
public sealed class PdfNamedDestination : PdfDestination
{
    internal PdfNamedDestination(PdfDocument document, CosObject name, bool isRemote, CosReference? owner)
        : base(document, name, isRemote, owner)
    {
        Name = name;
    }

    /// <summary>Gets the name as stored: a <see cref="CosName"/> or a <see cref="CosString"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.4.</remarks>
    public CosObject Name { get; }

    /// <summary>Gets the name as text, for display: a name's bytes as UTF-8, a string decoded as a text string.</summary>
    /// <remarks>ISO 32000-2 §12.3.2.4: the keys "may be treated as text strings for display purposes"; §7.9.2.2.</remarks>
    public string Text => Name switch
    {
        CosName name => name.Value,
        CosString text => text.DecodeText(),
        _ => string.Empty,
    };

    /// <summary>Looks the name up among the document's named destinations.</summary>
    /// <returns>
    /// The explicit destination it stands for; <see langword="null"/> when the destination is remote, or, with a diagnostic, when the
    /// document has no destination of that name or its value is not a destination.
    /// </returns>
    /// <exception cref="DiagnosticException">In strict mode, when the name does not resolve.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.4. A value that is a dictionary resolves to its <c>D</c> entry. A value that is itself a name or string is
    /// not followed.
    /// </remarks>
    public PdfExplicitDestination? Resolve()
    {
        if (IsRemote)
        {
            return null;
        }

        PdfExplicitDestination? destination = Document.FindNamedDestination(Name, out bool found);
        if (!found)
        {
            Document.DiagnosticSink.Report(
                DiagnosticCodes.NamedDestinationNotFound,
                DiagnosticSeverity.Warning,
                $"The document has no named destination \"{Escape(Text)}\"; the reference to it shows no page.",
                objectReference: Owner);
        }

        return destination;
    }

    /// <summary>Looks the name up and returns the structure destination its value carries, if any.</summary>
    /// <returns>
    /// The <c>SD</c> entry of the named destination's dictionary value as a destination; <see langword="null"/> when the destination
    /// is remote, the name does not resolve, its value is an array, or the dictionary has no usable <c>SD</c>.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.4: a named destination's value may be a dictionary with a <c>D</c> entry that "may optionally contain an
    /// SD entry as defined in Table 201", which should take precedence over <c>D</c> (§12.3.2.3). An <c>SD</c> that is not an array
    /// is reported (<c>DestinationInvalid</c>) and read as absent.
    /// </remarks>
    public PdfExplicitDestination? ResolveStructureDestination() =>
        IsRemote ? null : Document.FindNamedStructureDestination(Name);

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            builder.Append(char.IsControl(character) ? '?' : character);
        }

        return builder.ToString();
    }
}
