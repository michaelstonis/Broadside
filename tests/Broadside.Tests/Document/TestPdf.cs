using System.Globalization;
using System.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// Builds a small, classic-xref PDF in memory for a test that needs one variation the corpus does not have. Object i of the
/// <c>objects</c> list is written as object number i + 1; the trailer's Root is object 1. Offsets are computed from the header,
/// so a <see cref="Prefix"/> before it exercises ISO 32000-2 §7.5.2 (offsets count from the percent sign of <c>%PDF-</c>).
/// </summary>
internal sealed class TestPdf
{
    /// <summary>Gets the header line, without its end-of-line marker.</summary>
    public string Header { get; init; } = "%PDF-1.7";

    /// <summary>Gets bytes written before the header.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Gets extra trailer entries, written after <c>/Size</c> and <c>/Root</c>.</summary>
    public string TrailerEntries { get; init; } = string.Empty;

    /// <summary>A catalog, a page tree root and one page with the given extra page entries: the shape most tests vary.</summary>
    public static byte[] OnePage(string pageEntries, string catalogEntries = "", string header = "%PDF-1.7") =>
        new TestPdf { Header = header }.Build(
            $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /Resources << >> {pageEntries} >>");

    /// <summary>Writes the file.</summary>
    public byte[] Build(params string[] objects)
    {
        var text = new StringBuilder();
        text.Append(Prefix).Append(Header).Append('\n');
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length - Prefix.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length - Prefix.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {TrailerEntries} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
