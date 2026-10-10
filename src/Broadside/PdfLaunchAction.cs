using Broadside.Objects;

namespace Broadside;

/// <summary>A launch action: launches an application or opens or prints a document. The library never launches anything.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.6, Table 207 (PDF 1.1). The file names are untrusted data from the document. The platform entries
/// <c>Win</c>, <c>Mac</c> and <c>Unix</c> are deprecated in PDF 2.0; reading them is not a deviation.
/// </remarks>
public sealed class PdfLaunchAction : PdfAction
{
    internal PdfLaunchAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Launch;

    /// <summary>Gets the application to launch or the document to open or print (<c>F</c>; required unless <c>Win</c>, <c>Mac</c> or <c>Unix</c> is present).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 207, and §7.11.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <summary>Gets the Microsoft Windows launch parameters (<c>Win</c>; deprecated in PDF 2.0), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Tables 207 and 208.</remarks>
    public PdfWindowsLaunchParameters? Windows => ReadDictionary(ActionNames.Win) is { } win ? new PdfWindowsLaunchParameters(Document, win) : null;

    /// <summary>Gets the Mac OS launch parameters (<c>Mac</c>; deprecated in PDF 2.0, never defined), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 207.</remarks>
    public CosObject? Mac => Get(ActionNames.Mac);

    /// <summary>Gets the UNIX launch parameters (<c>Unix</c>; deprecated in PDF 2.0, never defined), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 207.</remarks>
    public CosObject? Unix => Get(ActionNames.Unix);

    /// <summary>Gets whether to open a PDF document in a new window (<c>NewWindow</c>, PDF 1.2); <see langword="null"/> when absent: the processor's preference.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 207.</remarks>
    public bool? NewWindow => ReadBoolean(ActionNames.NewWindow);

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.F) && !Dictionary.ContainsKey(ActionNames.Win) &&
            !Dictionary.ContainsKey(ActionNames.Mac) && !Dictionary.ContainsKey(ActionNames.Unix))
        {
            Report("A launch action shall have an F entry when it has no Win, Mac or Unix entry; it has none of them, so it does nothing.");
        }
    }
}

/// <summary>The Microsoft Windows parameters of a launch action (<c>Win</c>, deprecated in PDF 2.0). A live view.</summary>
/// <remarks>ISO 32000-2 §12.6.4.6, Table 208. Every value is untrusted data from the document.</remarks>
public sealed class PdfWindowsLaunchParameters
{
    private static readonly CosName P = new("P");

    private readonly PdfDocument _document;

    internal PdfWindowsLaunchParameters(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the parameter dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 208.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the file name of the application or document (<c>F</c>, required; a byte string, not a file specification), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 208.</remarks>
    public CosString? FileName => Get(NavigationNames.F) as CosString;

    /// <summary>Gets the default directory (<c>D</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 208.</remarks>
    public CosString? DefaultDirectory => Get(NavigationNames.D) as CosString;

    /// <summary>Gets the operation (<c>O</c>): <c>open</c> or <c>print</c>; <c>open</c> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 208: an ASCII string, default <c>open</c>.</remarks>
    public string Operation => Get(ActionNames.O) is CosString operation ? System.Text.Encoding.Latin1.GetString(operation.Bytes) : "open";

    /// <summary>Gets the parameters passed to the application (<c>P</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.6, Table 208.</remarks>
    public CosString? Parameters => Get(P) as CosString;

    private CosObject? Get(CosName key) => EntryReader.Get(_document, Dictionary, key);
}
