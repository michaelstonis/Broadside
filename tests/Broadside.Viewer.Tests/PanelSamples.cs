using System.Globalization;
using System.Text;

namespace Broadside.Viewer.Tests;

/// <summary>Small hand-written files for the side-panel models (#636): annotations with authors and replies, attachments, text.</summary>
internal static class PanelSamples
{
    /// <summary>
    /// Three Letter pages: page 1 a note by Alice with a reply by Bob and its popup, page 2 a square and a hidden note, page 3 nothing.
    /// </summary>
    public static byte[] Comments() => Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Count 3 /Kids [3 0 R 4 0 R 5 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [6 0 R 7 0 R 8 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [9 0 R 10 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
        "<< /Type /Annot /Subtype /Text /Rect [100 700 120 720] /T (Alice) /Contents (First comment) /M (D:20240102030405Z) /Popup 8 0 R >>",
        "<< /Type /Annot /Subtype /Text /Rect [130 700 150 720] /T (Bob) /Contents (A reply) /IRT 6 0 R >>",
        "<< /Type /Annot /Subtype /Popup /Rect [200 600 300 700] /Parent 6 0 R >>",
        "<< /Type /Annot /Subtype /Square /Rect [100 100 200 200] /Contents (Box) >>",
        "<< /Type /Annot /Subtype /Text /Rect [300 300 320 320] /Contents (Hidden) /F 2 >>");

    /// <summary>
    /// One page with a file attachment annotation, and an EmbeddedFiles tree of three: good.txt (checksum matches), bad.txt (checksum
    /// differs), missing.txt (an EF entry without a stream).
    /// </summary>
    public static byte[] Attachments()
    {
        string good = "good data", bad = "bad data";
        return Build(
            "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(bad.txt) 6 0 R (good.txt) 4 0 R (missing.txt) 8 0 R] >> >> >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [9 0 R] >>",
            "<< /Type /Filespec /F (good.txt) /UF (good.txt) /Desc (A good file) /EF << /F 5 0 R >> >>",
            Stream($"/Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size {good.Length} /CheckSum <{Md5(good)}> >>", good),
            "<< /Type /Filespec /F (bad.txt) /UF (bad.txt) /EF << /F 7 0 R >> >>",
            Stream($"/Type /EmbeddedFile /Params << /Size {bad.Length} /CheckSum <{Md5("something else")}> >>", bad),
            "<< /Type /Filespec /F (missing.txt) /EF << /F 42 >> >>",
            "<< /Type /Annot /Subtype /FileAttachment /Rect [10 10 30 30] /Contents (Clip) /FS << /Type /Filespec /F (clip.txt) /EF << /F 10 0 R >> >> >>",
            Stream("/Type /EmbeddedFile", "clipped"));
    }

    /// <summary>A file of Letter pages, each showing its lines in Helvetica 12 from (72, 700) down 14 points a line.</summary>
    public static byte[] Text(params string[][] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Length} /Kids [{string.Join(' ', Enumerable.Range(0, pages.Length).Select(i => $"{4 + (2 * i)} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (string[] lines in pages)
        {
            var content = new StringBuilder("BT /F1 12 Tf 14 TL 72 700 Td");
            foreach (string line in lines)
            {
                content.Append(" (").Append(line).Append(") Tj T*");
            }

            content.Append(" ET");
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents " + (objects.Count + 2).ToString(CultureInfo.InvariantCulture) + " 0 R >>");
            objects.Add(Stream(string.Empty, content.ToString()));
        }

        return Build([.. objects]);
    }

#pragma warning disable CA5351 // the checksum the spec defines (ISO 32000-2 §7.11.4.1, Table 45)
    private static string Md5(string text) => Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.Latin1.GetBytes(text)));
#pragma warning restore CA5351

    private static string Stream(string dictionary, string data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {dictionary} /Length {data.Length} >>\nstream\n{data}\nendstream");

    private static byte[] Build(params string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        foreach (string body in objects)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}

/// <summary>A viewer that records where the panels sent it.</summary>
internal sealed class RecordingViewer(DocumentSession session, DocumentSearch? search = null, AnnotationLayer? annotations = null) : IDocumentViewer
{
    public event EventHandler? ViewChanged
    {
        add { }
        remove { }
    }

    public DocumentSession? Session => session;

    public int PageIndex { get; private set; }

    public int PageCount => session.PageCount;

    public double Zoom => 1;

    public TextSelection? Selection => null;

    public DocumentSearch? Search => search;

    public AnnotationLayer? Annotations => annotations;

    public PageLabelTable? PageLabels => null;

    public PageLayoutOptions? LayoutOptions => null;

    public Rendering.ColorFilter? ColorFilter { get; set; }

    public (int PageIndex, PdfRectangle Area)? Revealed { get; private set; }

    public ViewTarget? Target { get; private set; }

    public void GoToPage(int pageIndex) => PageIndex = pageIndex;

    public void NextPage() => PageIndex++;

    public void PreviousPage() => PageIndex--;

    public void GoTo(ViewTarget target)
    {
        Target = target;
        PageIndex = target.PageIndex;
    }

    public void ZoomTo(ZoomMode mode)
    {
    }

    public void ZoomBy(double factor)
    {
    }

    public void SetLayout(PageLayoutOptions options)
    {
    }

    public void Reveal(int pageIndex, PdfRectangle area)
    {
        Revealed = (pageIndex, area);
        PageIndex = pageIndex;
    }
}
