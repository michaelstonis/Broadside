using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>The document information dictionary: a live view over the trailer's <c>Info</c> entry.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.3.3, Table 349. Every entry is a text string except the two dates (§7.9.4) and <c>Trapped</c> (a name); keys the
/// table does not define are allowed and "shall be text strings". PDF 2.0 deprecates every entry except <c>CreationDate</c> and
/// <c>ModDate</c> in favour of the XMP metadata stream (<see cref="PdfDocument.Metadata"/>); <see cref="PdfDocument.Properties"/>
/// resolves the two sources.
/// </para>
/// <para>
/// Every property reads the dictionary when called; nothing is ever written to it. A value of the wrong type is read as its text
/// where it has one (a name, a number) and otherwise as absent, and a date that does not follow §7.9.4 is repaired when it can be;
/// each records a diagnostic the first time it is read (in strict mode, the property throws). The raw text of a date that cannot
/// be read stays available through <see cref="GetText"/>.
/// </para>
/// </remarks>
public sealed class PdfDocumentInformation
{
    private static readonly CosName TitleKey = new("Title");
    private static readonly CosName AuthorKey = new("Author");
    private static readonly CosName SubjectKey = new("Subject");
    private static readonly CosName KeywordsKey = new("Keywords");
    private static readonly CosName CreatorKey = new("Creator");
    private static readonly CosName ProducerKey = new("Producer");
    private static readonly CosName CreationDateKey = new("CreationDate");
    private static readonly CosName ModDateKey = new("ModDate");
    private static readonly CosName TrappedKey = new("Trapped");

    private readonly DictionaryView _view;

    internal PdfDocumentInformation(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _view = new DictionaryView(document, dictionary, reference);
    }

    /// <summary>Gets the document information dictionary.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public CosDictionary Dictionary => _view.Dictionary;

    /// <summary>Gets the document's title (<c>Title</c>, PDF 1.1; deprecated in PDF 2.0 for <c>dc:title</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Title => ReadText(TitleKey);

    /// <summary>Gets the name of the person who created the document (<c>Author</c>; deprecated in PDF 2.0 for <c>dc:creator</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Author => ReadText(AuthorKey);

    /// <summary>Gets the subject of the document (<c>Subject</c>, PDF 1.1; deprecated in PDF 2.0 for <c>dc:description</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Subject => ReadText(SubjectKey);

    /// <summary>Gets the keywords associated with the document (<c>Keywords</c>, PDF 1.1; deprecated in PDF 2.0 for <c>pdf:Keywords</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Keywords => ReadText(KeywordsKey);

    /// <summary>
    /// Gets the name of the application that created the original document, when it was converted to PDF (<c>Creator</c>; deprecated
    /// in PDF 2.0 for <c>xmp:CreatorTool</c>).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Creator => ReadText(CreatorKey);

    /// <summary>Gets the name of the application that converted the document to PDF (<c>Producer</c>; deprecated in PDF 2.0 for <c>pdf:Producer</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Producer => ReadText(ProducerKey);

    /// <summary>Gets the date and time the document was created (<c>CreationDate</c>), or <see langword="null"/> when absent or not a date.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349, and §7.9.4.</remarks>
    public PdfDate? CreationDate => ReadDate(CreationDateKey);

    /// <summary>Gets the date and time the document was most recently modified (<c>ModDate</c>, PDF 1.1), or <see langword="null"/> when absent or not a date.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349, and §7.9.4.</remarks>
    public PdfDate? ModificationDate => ReadDate(ModDateKey);

    /// <summary>Gets whether the document includes trapping information (<c>Trapped</c>, PDF 1.3; deprecated in PDF 2.0). Default <see cref="PdfTrapped.Unknown"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.3.3, Table 349. The value is a name; a boolean or a string spelling <c>True</c> or <c>False</c> is read as that
    /// value with a diagnostic, anything else as <see cref="PdfTrapped.Unknown"/> with a diagnostic.
    /// </remarks>
    public PdfTrapped Trapped
    {
        get
        {
            CosObject? value = _view.Get(TrappedKey);
            (string? spelled, bool isName) = value switch
            {
                null => ("Unknown", true),
                CosName name => (name.Value, true),
                CosBoolean boolean => (boolean.Value ? "True" : "False", false),
                CosString text => (text.DecodeText(), false),
                _ => (null, false),
            };
            PdfTrapped? trapped = spelled switch
            {
                "True" => PdfTrapped.True,
                "False" => PdfTrapped.False,
                "Unknown" => PdfTrapped.Unknown,
                _ => null,
            };
            if (trapped is null)
            {
                _view.Report(DiagnosticCodes.InfoTrappedInvalid, "The Info dictionary's Trapped entry shall be /True, /False or /Unknown; it is read as /Unknown.");
                return PdfTrapped.Unknown;
            }

            if (!isName)
            {
                _view.Report(DiagnosticCodes.InfoTrappedInvalid, "The Info dictionary's Trapped entry shall be a name; it is a boolean or a string, read as the name it spells.");
            }

            return trapped.Value;
        }
    }

    /// <summary>Returns the text of any entry, such as a custom key: a text string decoded per §7.9.2.2.</summary>
    /// <param name="key">The key, such as <c>Title</c> or a custom key.</param>
    /// <returns>The text, or <see langword="null"/> when the entry is absent or has no text.</returns>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349: keys the table does not define "shall be text strings".</remarks>
    public string? GetText(string key) => ReadText(new CosName(key));

    /// <summary>Returns the date an entry holds, such as a custom key with a date value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The date, or <see langword="null"/> when the entry is absent or not a date.</returns>
    /// <remarks>ISO 32000-2 §7.9.4.</remarks>
    public PdfDate? GetDate(string key) => ReadDate(new CosName(key));

    private string? ReadText(CosName key) => _view.ReadText(key, DiagnosticCodes.InfoValueNotTextString);

    private PdfDate? ReadDate(CosName key)
    {
        string? text = ReadText(key);
        if (text is null)
        {
            return null;
        }

        switch (PdfDate.Parse(text, out PdfDate date))
        {
            case DateParseOutcome.Valid:
                return date;
            case DateParseOutcome.Repaired:
                _view.Report(DiagnosticCodes.DateInvalid, $"The Info dictionary's {key.Value} entry does not follow the date format of §7.9.4; it is read with the deviating fields repaired.");
                return date;
            default:
                _view.Report(DiagnosticCodes.DateUnreadable, $"The Info dictionary's {key.Value} entry is not a date (§7.9.4); its text is kept, its date is unknown.");
                return null;
        }
    }
}
