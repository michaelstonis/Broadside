using Broadside.Objects;

namespace Broadside;

/// <summary>A handler that checks a requirement: an element of a requirement's <c>RH</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §12.11.3, Table 276. Type <c>JS</c> names a document-level JavaScript (<see cref="Script"/>) that decides whether the
/// requirement is met; <c>NoOp</c> does nothing. Exposed, never run. A live view over the handler dictionary.
/// </remarks>
public sealed class PdfRequirementHandler
{
    private static readonly CosName SKey = new("S");
    private static readonly CosName ScriptKey = new("Script");

    private readonly PdfDocument _document;

    internal PdfRequirementHandler(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the handler dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.11.3, Table 276.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the type of the handler (<c>S</c>): <c>JS</c> or <c>NoOp</c>; <see langword="null"/> when missing.</summary>
    /// <remarks>ISO 32000-2 §12.11.3, Table 276. A processor ignores a handler of a type it does not know.</remarks>
    public CosName? HandlerType => _document.Resolve(Dictionary.GetValueOrDefault(SKey)) as CosName;

    /// <summary>Gets the name of the document-level JavaScript a <c>JS</c> handler runs (<c>Script</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.11.3, Table 276; the script is in the <c>JavaScript</c> name tree (§7.7.4).</remarks>
    public string? Script => _document.Resolve(Dictionary.GetValueOrDefault(ScriptKey)) is CosString script ? script.DecodeText() : null;
}
