using Broadside.Diagnostics;

namespace Broadside.Text;

/// <summary>
/// One page of OCR output: the size of the source image and the lines of words recognized on it, read from hOCR or ALTO or built from
/// any engine's word boxes, to lay under a scanned page as an invisible text layer.
/// </summary>
/// <remarks>
/// <para>
/// OCR itself is out of scope: bring the output of the engine you use (Tesseract, ABBYY, a cloud service) and
/// <see cref="PdfPage.AddTextLayer(OcrPage, TextLayerOptions?)"/> writes it as invisible text (ISO 32000-2 §9.3.6, §14.8.2.4), so
/// search, selection and copy work on the scan. Coordinates are in the units of the source image, top-left origin, y down.
/// </para>
/// <para>
/// The readers are lenient (ADR 0005): unknown elements are ignored, and a word whose box or coordinates cannot be read is skipped
/// and recorded in <see cref="Diagnostics"/>. Input that is not markup at all throws <see cref="FormatException"/>. hOCR 1.2:
/// http://kba.github.io/hocr-spec/1.2/; ALTO 2.0 to 4.4: https://www.loc.gov/standards/alto/.
/// </para>
/// </remarks>
public sealed class OcrPage
{
    /// <summary>Initializes a new instance of the <see cref="OcrPage"/> class.</summary>
    /// <param name="width">The width of the source image, in its units (pixels).</param>
    /// <param name="height">The height of the source image, in its units.</param>
    /// <param name="lines">The lines, in reading order.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not a positive finite number.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is <see langword="null"/>.</exception>
    public OcrPage(double width, double height, IReadOnlyList<OcrLine> lines)
        : this(width, height, lines, [])
    {
    }

    internal OcrPage(double width, double height, IReadOnlyList<OcrLine> lines, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (!(width > 0) || !double.IsFinite(width))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The image width shall be a positive finite number.");
        }

        if (!(height > 0) || !double.IsFinite(height))
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The image height shall be a positive finite number.");
        }

        ArgumentNullException.ThrowIfNull(lines);
        Width = width;
        Height = height;
        Lines = lines;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the width of the source image, in its units (pixels).</summary>
    public double Width { get; }

    /// <summary>Gets the height of the source image, in its units (pixels).</summary>
    public double Height { get; }

    /// <summary>Gets the lines, in reading order.</summary>
    public IReadOnlyList<OcrLine> Lines { get; }

    /// <summary>Gets the repairs made while reading the page from hOCR or ALTO; empty for a page built in code.</summary>
    /// <remarks>Codes of <see cref="TextDiagnostics"/>: <see cref="TextDiagnostics.OcrBoundsInvalid"/>, <see cref="TextDiagnostics.OcrPageSizeInvalid"/>, <see cref="TextDiagnostics.OcrConfidenceInvalid"/>.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Reads every page of an hOCR document (the HTML Tesseract and other engines write), as XHTML or as tolerant HTML.</summary>
    /// <param name="hocr">The document, read to its end (UTF-8 unless it starts with a UTF-16 byte order mark).</param>
    /// <returns>The pages (<c>ocr_page</c> elements) in document order; empty when the document has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hocr"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The input holds no markup.</exception>
    /// <remarks>
    /// hOCR 1.2: a page is an <c>ocr_page</c> whose <c>title</c> gives <c>bbox</c>; lines are <c>ocr_line</c>, <c>ocrx_line</c>,
    /// <c>ocr_caption</c>, <c>ocr_header</c> or <c>ocr_textfloat</c> (with <c>textangle</c>); words are <c>ocrx_word</c> with
    /// <c>bbox</c> and <c>x_wconf</c> (0 to 100). <c>lang</c> is inherited from the nearest element that has one. Tags are read by a
    /// tolerant reader, not an XML parser, so HTML that is not well-formed XML is read too.
    /// </remarks>
    public static IReadOnlyList<OcrPage> FromHocr(Stream hocr)
    {
        ArgumentNullException.ThrowIfNull(hocr);
        return HocrReader.Read(ReadAll(hocr));
    }

    /// <summary>Reads every page of an ALTO document, version 2 to 4 (the XML ABBYY, Tesseract and library digitization tools write).</summary>
    /// <param name="alto">The document, read to its end.</param>
    /// <returns>The pages (<c>Page</c> elements) in document order; empty when the document has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="alto"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The input is not well-formed XML.</exception>
    /// <remarks>
    /// ALTO 2.0 to 4.4: elements are matched by local name in any namespace; words are <c>String</c> (<c>CONTENT</c>, <c>HPOS</c>,
    /// <c>VPOS</c>, <c>WIDTH</c>, <c>HEIGHT</c>, <c>WC</c> 0 to 1) in <c>TextLine</c>; <c>ROTATION</c> on <c>TextBlock</c>,
    /// <c>TextLine</c> or <c>String</c> and <c>LANG</c> on any of them are honored. Coordinates stay in the file's
    /// <c>MeasurementUnit</c>, as do the page's <c>WIDTH</c> and <c>HEIGHT</c>, so they map onto the page alike. DTDs are not processed.
    /// </remarks>
    public static IReadOnlyList<OcrPage> FromAlto(Stream alto)
    {
        ArgumentNullException.ThrowIfNull(alto);
        return AltoReader.Read(ReadAll(alto));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
