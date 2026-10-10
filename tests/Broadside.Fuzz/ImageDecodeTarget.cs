using System.Text;
using Broadside.Content;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>image-decode</c> target (issue #60): an image dictionary and data through the image model. Input: one mode byte, then a
/// COS dictionary in PDF syntax, a line feed, and the image data. An even mode byte builds an image XObject from the dictionary and
/// data; an odd one writes an inline image (<c>BI</c>, the dictionary's entries, <c>ID</c>, the data, <c>EI Q</c>) into a content
/// stream and runs it, so the inline-image end finder sees the data. A whole corpus file (<c>%PDF-</c>) decodes every image
/// XObject of its first page. The engine's limits are tiny (2^16 pixels, 1 MiB) so mutated sizes stay cheap. Invariants: no
/// exception in lenient mode; every decoded buffer is Stride x Height bytes with the §8.9.3 stride and no more decoded rows than
/// rows; an inline image's data lies within the content.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.</remarks>
internal static class ImageDecodeTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedStreamLength(1 << 20));

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            using PdfDocument? file = FuzzTargets.OpenOrNull(data, Engine);
            if (file is not null && file.Pages.Count > 0 && file.Pages[0].Resources is { } resources
                && file.Resolve(resources.GetValueOrDefault(new CosName("XObject"))) is CosDictionary xobjects)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in xobjects)
                {
                    if (file.GetImage(entry.Value) is { } image)
                    {
                        Exercise(image);
                    }
                }
            }

            return;
        }

        if (data.IsEmpty)
        {
            return;
        }

        bool inline = (data[0] & 1) != 0;
        ReadOnlySpan<byte> rest = data[1..];
        int newline = rest.IndexOf((byte)'\n');
        ReadOnlySpan<byte> dictionaryText = newline < 0 ? rest : rest[..newline];
        ReadOnlySpan<byte> samples = newline < 0 ? [] : rest[(newline + 1)..];
        using PdfDocument document = Engine.Create();
        if (!inline)
        {
            if (new CosParser(dictionaryText, repairs: null).ParseObject() is CosDictionary dictionary && document.GetImage(new CosStream(dictionary, samples.ToArray())) is { } image)
            {
                Exercise(image);
            }

            return;
        }

        ReadOnlySpan<byte> entries = dictionaryText.Trim(" \t\r\n"u8);
        if (entries.StartsWith("<<"u8))
        {
            entries = entries[2..];
        }

        if (entries.EndsWith(">>"u8))
        {
            entries = entries[..^2];
        }

        byte[] content = [.. "q BI "u8, .. entries, .. " ID "u8, .. samples, .. " EI Q"u8];
        ContentInterpreter.RunBytes(content, document, new InlineImageChecker(content.Length), ContentInterpreter.DefaultOptions);
    }

    private static void Exercise(PdfImage image)
    {
        _ = (image.Width, image.Height, image.BitsPerComponent, image.IsStencil, image.ColorSpace, image.DecodeArray, image.Interpolate, image.Intent);
        _ = (image.MaskKind, image.ColorKey, image.Matte, image.SoftMaskInData, image.Alternates.Count, image.OptionalContent, image.Metadata, image.StructParent, image.Name);
        Check(image);
        if (image.Mask is { } mask)
        {
            Check(mask);
        }
    }

    private static void Check(PdfImage image)
    {
        using DecodedImage? decoded = image.Decode();
        if (decoded is null)
        {
            return;
        }

        long stride = ((long)decoded.Width * decoded.Components * decoded.StorageBits + 7) / 8;
        if (decoded.Stride != stride || decoded.Samples.Length != (long)decoded.Stride * decoded.Height || decoded.DecodedRows > decoded.Height || decoded.DecodedRows < 0)
        {
            throw new InvalidOperationException(
                $"Decoded {decoded.Width} x {decoded.Height} x {decoded.Components} at {decoded.StorageBits} bits: stride {decoded.Stride}, {decoded.Samples.Length} bytes, {decoded.DecodedRows} rows.");
        }

        ImageDecodeMap map = image.CreateDecodeMap(decoded);
        ushort[] row = new ushort[decoded.Width * decoded.Components];
        float[] values = new float[row.Length];
        for (int y = 0; y < decoded.Height; y++)
        {
            ImageRows.Unpack(decoded.GetRow(y), decoded.StorageBits, row.Length, row);
            map.Map(row, values);
        }
    }

    /// <summary>Decodes every inline image of the run and checks its data range.</summary>
    private sealed class InlineImageChecker(int contentLength) : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (op.Code != ContentOperatorCode.BeginInlineImage)
            {
                return;
            }

            if (op.Data.Length > contentLength || op.Offset + op.Length > contentLength)
            {
                throw new InvalidOperationException($"Inline image data of {op.Data.Length} bytes ending at {op.Offset + op.Length} in {contentLength} bytes of content.");
            }

            if (context.GetInlineImage(op) is { } image)
            {
                Exercise(image);
            }
        }
    }
}
