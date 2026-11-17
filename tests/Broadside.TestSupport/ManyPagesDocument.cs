using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Builds a minimal PDF with many pages, for viewer tests and benchmarks that scroll through long documents.</summary>
public static class ManyPagesDocument
{
    private const string Paths =
        "0.2 0.4 0.8 rg 72 72 300 400 re f 0 0 0 RG 6 w 1 J 100 600 m 500 740 l 520 100 l S 0.9 0.1 0.1 rg 400 500 150 150 re f";

    /// <summary>A minimal file of <paramref name="count"/> Letter pages sharing one content stream of paths.</summary>
    public static byte[] Create(int count) => Create(count, Paths, null);

    /// <summary>
    /// A minimal file of <paramref name="count"/> Letter pages sharing one content stream of <paramref name="lines"/> lines of
    /// Helvetica text (a standard 14 font, nothing embedded): for viewer tests that select or copy across many pages.
    /// </summary>
    public static byte[] CreateWithText(int count, int lines = 40)
    {
        var content = new StringBuilder("BT /F1 12 Tf 14 TL 72 740 Td");
        for (int line = 0; line < lines; line++)
        {
            content.Append(" (The quick brown fox jumps over the lazy dog.) '");
        }

        content.Append(" ET");
        return Create(count, content.ToString(), "/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>");
    }

    private static byte[] Create(int count, string content, string? resources)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        void Add(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{4 + i} 0 R ");
        }

        Add(resources is null ? $"<< /Type /Pages /Count {count} /Kids [{kids}] >>" : $"<< /Type /Pages /Count {count} /Kids [{kids}] {resources} >>");
        Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        for (int i = 0; i < count; i++)
        {
            Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 3 0 R >>");
        }

        int xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
