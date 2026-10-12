using Broadside.Objects;

namespace Broadside;

/// <summary>A rich-media-execute action: sends a command to a rich media annotation's handler. Kept as data; rich media is out of scope.</summary>
/// <remarks>ISO 32000-2 §12.6.4.18, Tables 222 and 223 (PDF 2.0), and §13.7 (rich media, exposed raw).</remarks>
public sealed class PdfRichMediaExecuteAction : PdfAction
{
    internal PdfRichMediaExecuteAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.RichMediaExecute;

    /// <summary>Gets the rich media annotation to send the command to (<c>TA</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Table 222.</remarks>
    public CosDictionary? Annotation => ReadDictionary(ActionNames.TA);

    /// <summary>Gets the RichMediaInstance dictionary the command targets (<c>TI</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Table 222.</remarks>
    public CosDictionary? Instance => ReadDictionary(ActionNames.TI);

    /// <summary>Gets the command (<c>CMD</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Tables 222 and 223.</remarks>
    public PdfRichMediaCommand? Command => ReadDictionary(ActionNames.CMD) is { } command ? new PdfRichMediaCommand(Document, command) : null;

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("rich media");
        if (!Dictionary.ContainsKey(ActionNames.TA) || !Dictionary.ContainsKey(ActionNames.CMD))
        {
            Report("A rich-media-execute action shall have a TA and a CMD entry; it lacks one, so it does nothing.");
        }
    }
}

/// <summary>The command of a rich-media-execute action: a function name and its arguments. A live view.</summary>
/// <remarks>ISO 32000-2 §12.6.4.18, Table 223 (PDF 2.0).</remarks>
public sealed class PdfRichMediaCommand
{
    private static readonly CosName C = new("C");
    private static readonly CosName A = new("A");

    private readonly PdfDocument _document;

    internal PdfRichMediaCommand(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the RichMediaCommand dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Table 223.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the script command, an ECMAScript function name (<c>C</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Table 223.</remarks>
    public string? Name => Get(C) is CosString name ? name.DecodeText() : null;

    /// <summary>Gets the arguments (<c>A</c>), resolved: one value, or each element of an array; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.18, Table 223: text strings, integers, numbers or booleans.</remarks>
    public IReadOnlyList<CosObject> Arguments => Get(A) switch
    {
        null => [],
        CosArray array => [.. array.Select(_document.Resolve)],
        var value => [value],
    };

    private CosObject? Get(CosName key) => EntryReader.Get(_document, Dictionary, key);
}
