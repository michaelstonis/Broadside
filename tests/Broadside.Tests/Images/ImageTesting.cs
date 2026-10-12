using System.Text;
using Broadside.Content;
using Broadside.Images;
using Broadside.Tests.Document;

namespace Broadside.Tests.Images;

/// <summary>Builds documents with image XObjects and reads samples back through the public image seam (ISO 32000-2 §8.9).</summary>
internal static class ImageTesting
{
    /// <summary>
    /// A one-page document whose resources name object 5 as <c>/Im0</c>: an image XObject with the dictionary entries
    /// <paramref name="entries"/> and the data <paramref name="data"/> (Latin-1, one char per byte); further objects are numbered from 6.
    /// </summary>
    public static byte[] OneImage(string entries, string data, params string[] objects) => new TestPdf().Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>",
        Stream(string.Empty, "q 10 0 0 10 0 0 cm /Im0 Do Q"),
        Stream("/Type /XObject /Subtype /Image " + entries, data),
        .. objects,
    ]);

    /// <summary>A stream object body with the dictionary entries and the data (Latin-1).</summary>
    public static string Stream(string entries, string data) =>
        $"<< {entries} /Length {Encoding.Latin1.GetByteCount(data)} >>\nstream\n{data}\nendstream";

    /// <summary>The bytes as a Latin-1 string, for stream data in <see cref="OneImage"/>.</summary>
    public static string Bytes(params byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Unpacks row <paramref name="y"/> of an image into raw values, one per component.</summary>
    public static ushort[] Row(DecodedImage image, int y)
    {
        ushort[] values = new ushort[image.Width * image.Components];
        ImageRows.Unpack(image.GetRow(y), image.StorageBits, values.Length, values);
        return values;
    }

    /// <summary>Unpacks every row into one array of raw values.</summary>
    public static ushort[] Raw(DecodedImage image) => [.. Enumerable.Range(0, image.Height).SelectMany(y => Row(image, y))];

    /// <summary>The codes of the document's diagnostics.</summary>
    public static string[] Codes(PdfDocument document) => [.. document.Diagnostics.Select(d => d.Code)];

    /// <summary>Opens <paramref name="file"/> and returns the inline images of its first page, in content order.</summary>
    public static (PdfDocument Document, InlineImageCollector Collected) InlineImages(byte[] file, PdfOptions? options = null)
    {
        PdfDocument document = PdfDocument.Open(file, options ?? new PdfOptions());
        var collector = new InlineImageCollector();
        document.Pages[0].ProcessContent(collector);
        return (document, collector);
    }
}

/// <summary>Collects every inline image of a run through <see cref="ContentContext.GetInlineImage"/>, and what follows each.</summary>
internal sealed class InlineImageCollector : ContentProcessor
{
    public List<PdfImage> Images { get; } = [];

    public List<string> Operators { get; } = [];

    public List<byte[]> Data { get; } = [];

    public override ContentEvents Events => ContentEvents.Operators;

    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        Operators.Add(Encoding.Latin1.GetString(op.Keyword));
        if (context.GetInlineImage(op) is { } image)
        {
            Images.Add(image);
            Data.Add(op.Data.ToArray());
        }
    }
}
