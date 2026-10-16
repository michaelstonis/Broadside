using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Builds a minimal PDF with many pages, for viewer tests and benchmarks that scroll through long documents.</summary>
public static class ManyPagesDocument
{
    /// <summary>A minimal file of <paramref name="count"/> Letter pages sharing one content stream of paths.</summary>
    public static byte[] Create(int count)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        void Add(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        const string content = "0.2 0.4 0.8 rg 72 72 300 400 re f 0 0 0 RG 6 w 1 J 100 600 m 500 740 l 520 100 l S 0.9 0.1 0.1 rg 400 500 150 150 re f";
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{4 + i} 0 R ");
        }

        Add($"<< /Type /Pages /Count {count} /Kids [{kids}] >>");
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
