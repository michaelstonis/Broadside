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

    /// <summary>
    /// Gets whether the comment line of four bytes of 128 or more follows the header (§7.5.2: a file with binary data shall have it);
    /// <see langword="null"/>, the default, writes it when an object holds such a byte.
    /// </summary>
    public bool? BinaryComment { get; init; }

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

    /// <summary>
    /// Appends an incremental update (ISO 32000-2 §7.5.6) to <paramref name="file"/>: the objects, a cross-reference section with one
    /// subsection per object, and a trailer with <paramref name="trailerEntries"/> and a <c>Prev</c> entry pointing at the
    /// section the file's last <c>startxref</c> names. An object whose body is <see langword="null"/> is written as a free entry
    /// carrying <c>Generation</c>, the generation a reuse of the number takes.
    /// </summary>
    public static byte[] AppendUpdate(byte[] file, string trailerEntries, params (int Number, int Generation, string? Body)[] objects)
    {
        string existing = Encoding.Latin1.GetString(file);
        int startxref = existing.LastIndexOf("startxref", StringComparison.Ordinal);
        string prev = existing[(startxref + "startxref".Length)..].Trim().Split('\n')[0].Trim();
        var text = new StringBuilder(existing);
        var offsets = new Dictionary<int, int>();
        foreach ((int number, int generation, string? body) in objects)
        {
            if (body is not null)
            {
                offsets[number] = text.Length;
                text.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj\n{body}\nendobj\n");
            }
        }

        int xref = text.Length;
        text.Append("xref\n");
        foreach ((int number, int generation, string? body) in objects.OrderBy(o => o.Number))
        {
            string entry = body is null ? $"0000000000 {generation:D5} f" : $"{offsets[number]:D10} {generation:D5} n";
            text.Append(CultureInfo.InvariantCulture, $"{number} 1\n{entry} \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< {trailerEntries} /Prev {prev} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>Writes the file.</summary>
    public byte[] Build(params string[] objects)
    {
        var text = new StringBuilder();
        text.Append(Prefix).Append(Header).Append('\n');
        if (BinaryComment ?? objects.Any(static body => body.Any(static c => c >= '\u0080')))
        {
            text.Append("%\u00e2\u00e3\u00cf\u00d3\n");
        }

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
