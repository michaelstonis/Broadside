using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>An optional content group: a collection of graphics that can be made visible or invisible. A live view over its dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §8.11.2.1 (PDF 1.5), Table 96. A group's state (ON or OFF) is not stored in the file; it lives in a
/// <see cref="PdfOptionalContentState"/>. Only groups listed in the optional content properties' <c>OCGs</c> array are optional
/// content (§8.11.3.2). Its <c>Metadata</c> entry is reported by <c>PdfDocument.EnumerateObjectMetadata</c>.
/// </remarks>
public sealed class PdfOptionalContentGroup
{
    private readonly PdfDocument _document;

    internal PdfOptionalContentGroup(PdfDocument document, CosDictionary dictionary, CosReference? reference, int ordinal)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
        Ordinal = ordinal;
    }

    /// <summary>Gets the optional content group dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the group (groups shall be indirect objects), or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the name of the group for a user interface (<c>Name</c>); empty when missing.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the name is missing or not a text string.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.11.2.1, Table 96 (required, a text string). A missing name reads as empty and a name or number as its text, each
    /// with an <c>OptionalContentGroupInvalid</c> diagnostic; any other type reads as empty with the same diagnostic.
    /// </remarks>
    public string Name
    {
        get
        {
            if (ViewReading.Text(_document, Dictionary, FileAndLayerNames.Name, Issue) is { } name)
            {
                return name;
            }

            if (ViewReading.Get(_document, Dictionary, FileAndLayerNames.Name) is null)
            {
                Issue.Report("The optional content group has no Name, which Table 96 requires; it reads as empty.");
            }

            return string.Empty;
        }
    }

    /// <summary>Gets the intended uses of the group (<c>Intent</c>, a name or an array of names): <c>View</c> when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.11.2.1, Table 96, and §8.11.2.3. <c>View</c> and <c>Design</c> are defined; second-class names may appear. An
    /// array element that is not a name is skipped, and a value that is neither a name nor an array reads as <c>View</c>, each with an
    /// <c>OptionalContentGroupInvalid</c> diagnostic.
    /// </remarks>
    public IReadOnlyList<CosName> Intents => ReadIntents(_document, Dictionary, Issue);

    /// <summary>Gets the usage dictionary describing the nature of the group's content (<c>Usage</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96, and §8.11.4.4, Table 100.</remarks>
    public PdfOptionalContentUsage? Usage =>
        ViewReading.Get(_document, Dictionary, FileAndLayerNames.Usage) is CosDictionary usage ? new PdfOptionalContentUsage(_document, usage) : null;

    /// <summary>Gets the group's position in the optional content properties' group list.</summary>
    internal int Ordinal { get; }

    /// <summary>Gets the report for a malformed entry of the group (<c>OptionalContentGroupInvalid</c>).</summary>
    internal EntryReport Issue => new(_document, DiagnosticCodes.OptionalContentGroupInvalid, Reference, "The optional content group");

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>Reads an <c>Intent</c> entry (a name or an array of names); <c>View</c> when absent or unusable, with a diagnostic when unusable.</summary>
    internal static CosName[] ReadIntents(PdfDocument document, CosDictionary dictionary, EntryReport report)
    {
        switch (ViewReading.Get(document, dictionary, FileAndLayerNames.Intent))
        {
            case null:
                return [FileAndLayerNames.View];
            case CosName name:
                return [name];
            case CosArray array:
                var intents = new List<CosName>(array.Count);
                foreach (CosObject element in array)
                {
                    switch (EntryReader.Resolve(document, element))
                    {
                        case CosName intent:
                            intents.Add(intent);
                            break;
                        case null:
                            break;
                        default:
                            report.Report("An element of the Intent array is not a name; it is skipped.");
                            break;
                    }
                }

                return [.. intents];
            default:
                report.Ignored(FileAndLayerNames.Intent, "a name or an array of names");
                return [FileAndLayerNames.View];
        }
    }
}
