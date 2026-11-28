using Broadside.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>hocr</c> and <c>alto</c> targets (issue #649): the input is read as hOCR or ALTO, and every page read is laid onto a new
/// page as an invisible text layer. Input that holds no markup (hOCR) or is not well-formed XML (ALTO) throws
/// <see cref="FormatException"/>, a documented outcome; anything else that escapes is a finding. Invariants: every page has a
/// positive finite size, every word a finite box; the text layer writes, and its text extracts, without an exception.
/// </summary>
/// <remarks>hOCR 1.2; ALTO 2.0 to 4.4; ISO 32000-2 §9.3.6 and §14.8.2.4.</remarks>
internal static class OcrTarget
{
    public static void Hocr(ReadOnlySpan<byte> data) => Run(data, HocrReader.Read);

    public static void Alto(ReadOnlySpan<byte> data) => Run(data, AltoReader.Read);

    private static void Run(ReadOnlySpan<byte> data, Func<byte[], IReadOnlyList<OcrPage>> read)
    {
        IReadOnlyList<OcrPage> pages;
        try
        {
            pages = read(data.ToArray());
        }
        catch (FormatException)
        {
            return;
        }

        foreach (OcrPage page in pages)
        {
            if (!(page.Width > 0) || !(page.Height > 0) || !double.IsFinite(page.Width) || !double.IsFinite(page.Height))
            {
                throw new InvalidOperationException($"A page was read with the size {page.Width} by {page.Height}.");
            }

            foreach (OcrLine line in page.Lines)
            {
                foreach (OcrWord word in line.Words)
                {
                    PdfRectangle box = word.Bounds;
                    if (!double.IsFinite(box.Left) || !double.IsFinite(box.Right) || !double.IsFinite(box.Bottom) || !double.IsFinite(box.Top))
                    {
                        throw new InvalidOperationException($"A word was read with the box {box}.");
                    }
                }
            }
        }

        if (pages.Count == 0)
        {
            return;
        }

        using PdfDocument document = PdfDocument.Create();
        PdfPage target = document.Pages[0];
        target.AddTextLayer(pages[0]);
        _ = target.ExtractText().Words;
    }
}
