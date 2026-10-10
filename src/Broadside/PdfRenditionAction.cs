using Broadside.Objects;

namespace Broadside;

/// <summary>A rendition action: plays, stops, pauses or resumes multimedia content, or runs a script. Kept as data; the library never plays or runs anything.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.14, Table 218 (PDF 1.5), and §13.2.3 (renditions, exposed raw). Either <c>JS</c> or <c>OP</c> shall be present;
/// when both are, <c>OP</c> is the fallback for a processor that cannot run scripts. An unrecognised <c>OP</c> without <c>JS</c> makes
/// the action invalid.
/// </remarks>
public sealed class PdfRenditionAction : PdfAction
{
    internal PdfRenditionAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Rendition;

    /// <summary>Gets the rendition object (<c>R</c>; required when <c>OP</c> is 0 or 4), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218, and §13.2.3.</remarks>
    public CosDictionary? Rendition => ReadDictionary(ActionNames.R);

    /// <summary>Gets the screen annotation (<c>AN</c>; required when <c>OP</c> is present), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218, and §12.5.6.18.</remarks>
    public CosDictionary? ScreenAnnotation => ReadDictionary(ActionNames.AN);

    /// <summary>Gets the operation (<c>OP</c>) as stored, or <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218.</remarks>
    public int? OperationValue => ReadInteger(ActionNames.OP);

    /// <summary>Gets the operation (<c>OP</c>), or <see langword="null"/> when absent or not one of 0 to 4.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218.</remarks>
    public PdfRenditionOperation? Operation => OperationValue is >= 0 and <= 4 and int value ? (PdfRenditionOperation)value : null;

    /// <summary>Gets the script (<c>JS</c>), decoded from its text string or text stream, or <see langword="null"/>. Never run.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218, and §12.6.4.17. The raw value is <see cref="ScriptObject"/>.</remarks>
    public string? Script => ReadScript(ActionNames.JS);

    /// <summary>Gets the <c>JS</c> entry as stored (resolved): a string or a stream; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.14, Table 218.</remarks>
    public CosObject? ScriptObject => Get(ActionNames.JS);

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("multimedia playback and scripts");
        bool script = Dictionary.ContainsKey(ActionNames.JS);
        int? operation = OperationValue;
        if (!script && !Dictionary.ContainsKey(ActionNames.OP))
        {
            Report("A rendition action shall have a JS or an OP entry; it has neither.");
        }
        else if (!script && operation is not (>= 0 and <= 4))
        {
            Report("A rendition action's OP is not one of 0 to 4 and it has no JS entry, so the action is invalid.");
        }
        else if (operation is >= 0 and <= 4 && !Dictionary.ContainsKey(ActionNames.AN))
        {
            Report("A rendition action with an OP entry shall have an AN entry naming a screen annotation; it has none.");
        }
        else if (operation is 0 or 4 && !Dictionary.ContainsKey(ActionNames.R))
        {
            Report("A rendition action whose OP is 0 or 4 shall have an R entry naming a rendition; it has none.");
        }
    }
}
