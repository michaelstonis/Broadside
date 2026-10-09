using System.Globalization;
using System.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// Builds a PDF 1.5 file in memory whose cross-reference information is a cross-reference stream (ISO 32000-2 §7.5.8), for tests
/// that need a variation the corpus does not have. Objects are written in the order they are added; <see cref="Offset"/> gives the
/// offset of each, and <see cref="Finish"/> writes the cross-reference stream from rows the test states field by field.
/// </summary>
internal sealed class XrefStreamPdf
{
    private readonly StringBuilder _text = new("%PDF-1.5\n");
    private readonly Dictionary<int, int> _offsets = [];

    /// <summary>The catalog, page tree root and page every test file needs, as objects 1, 2 and 3.</summary>
    public static readonly string[] OnePage =
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
    ];

    /// <summary>Gets the offset the next object will be written at.</summary>
    public int Position => _text.Length;

    /// <summary>Gets the offset object <paramref name="number"/> was written at.</summary>
    public int Offset(int number) => _offsets[number];

    /// <summary>Writes <c>number 0 obj body endobj</c>.</summary>
    public XrefStreamPdf Add(int number, string body, int generation = 0)
    {
        _offsets[number] = _text.Length;
        _text.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj\n{body}\nendobj\n");
        return this;
    }

    /// <summary>Writes objects 1, 2 and 3 of <see cref="OnePage"/>.</summary>
    public XrefStreamPdf AddOnePage()
    {
        for (int index = 0; index < OnePage.Length; index++)
        {
            Add(index + 1, OnePage[index]);
        }

        return this;
    }

    /// <summary>
    /// Writes an object stream (§7.5.7) holding <paramref name="members"/>, each followed by <paramref name="separator"/>. The header
    /// pairs come from <paramref name="header"/> when given, else from the members in order; <c>N</c> is <paramref name="count"/>
    /// when given, else the number of members.
    /// </summary>
    public XrefStreamPdf AddObjectStream(int number, (int Number, string Body)[] members, string separator = "\n", string? header = null, string? count = null)
    {
        var pairs = new StringBuilder();
        var data = new StringBuilder();
        foreach ((int member, string body) in members)
        {
            pairs.Append(CultureInfo.InvariantCulture, $"{member} {data.Length} ");
            data.Append(body).Append(separator);
        }

        string head = header ?? pairs.ToString();
        string content = head + data;
        return Add(number, $"<< /Type /ObjStm /N {count ?? members.Length.ToString(CultureInfo.InvariantCulture)} /First {head.Length} /Length {content.Length} >>\nstream\n{content}\nendstream");
    }

    /// <summary>Writes raw text, such as a classic section.</summary>
    public XrefStreamPdf Raw(string text)
    {
        _text.Append(text);
        return this;
    }

    /// <summary>
    /// Writes the cross-reference stream as object <paramref name="number"/> with <paramref name="entries"/> in its dictionary (which
    /// must state <c>W</c>), its data the rows encoded big-endian with <paramref name="widths"/>, then <c>startxref</c> and <c>%%EOF</c>.
    /// </summary>
    public byte[] Finish(int number, string entries, int[] widths, params (long Type, long Field2, long Field3)[] rows)
    {
        byte[] data = Rows(widths, rows);
        return Finish(number, entries, data);
    }

    /// <summary>Writes the cross-reference stream with the given raw data, then <c>startxref</c> and <c>%%EOF</c>.</summary>
    public byte[] Finish(int number, string entries, byte[] data)
    {
        int start = _text.Length;
        _text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /XRef {entries} /Length {data.Length} >>\nstream\n");
        _text.Append(Encoding.Latin1.GetString(data));
        _text.Append(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{start}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(_text.ToString());
    }

    /// <summary>Encodes rows of three fields big-endian, each field in its width; a width of 0 omits the field.</summary>
    public static byte[] Rows(int[] widths, params (long Type, long Field2, long Field3)[] rows)
    {
        var bytes = new List<byte>();
        foreach ((long type, long field2, long field3) in rows)
        {
            Append(bytes, type, widths[0]);
            Append(bytes, field2, widths[1]);
            Append(bytes, field3, widths[2]);
        }

        return [.. bytes];
    }

    private static void Append(List<byte> bytes, long value, int width)
    {
        for (int shift = (width - 1) * 8; shift >= 0; shift -= 8)
        {
            bytes.Add((byte)(value >> shift));
        }
    }
}
