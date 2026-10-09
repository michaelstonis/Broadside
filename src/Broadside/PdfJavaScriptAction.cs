using Broadside.Objects;

namespace Broadside;

/// <summary>An ECMAScript (JavaScript) action: a script to run. Kept as data; the library never runs scripts and has no script engine.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.17, Table 221 (PDF 1.3). The type is named <c>JavaScript</c> in files although the specification calls the
/// language ECMAScript; a name written with an escape such as <c>/Java#53cript</c> is the same name (§7.3.5). Document-level scripts
/// are in the name dictionary's <c>JavaScript</c> tree (<see cref="PdfNameDictionary.JavaScript"/>).
/// </remarks>
public sealed class PdfJavaScriptAction : PdfAction
{
    internal PdfJavaScriptAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.JavaScript;

    /// <summary>Gets the script (<c>JS</c>, required), decoded from its text string or text stream, or <see langword="null"/>. Never run.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.6.4.17, Table 221, and §7.9.2.2: a byte order mark selects UTF-16BE or UTF-8, else PDFDocEncoding. A stream is
    /// decoded through its filters into new memory; the stream is not changed. The raw value is <see cref="ScriptObject"/>.
    /// </remarks>
    public string? Script => ReadScript(ActionNames.JS);

    /// <summary>Gets the <c>JS</c> entry as stored (resolved): a string or a stream; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.17, Table 221.</remarks>
    public CosObject? ScriptObject => Get(ActionNames.JS);

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("ECMAScript");
        if (!Dictionary.ContainsKey(ActionNames.JS))
        {
            Report("An ECMAScript action shall have a JS entry; it has none.");
        }
    }
}
