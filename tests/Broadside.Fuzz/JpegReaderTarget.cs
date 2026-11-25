using Broadside.Images;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>jpeg-reader</c> target (issue #274): JPEG input to the canvas. The input is a JPEG file; a whole PDF file (the smoke
/// seeds) is read from its first <c>FF D8 FF</c> on, so the corpus files with DCTDecode images seed it. The file goes through
/// <see cref="PdfDocument.CreateImageFromJpeg"/>, which reads the markers before the first scan (frame header, Adobe APP14,
/// Photoshop APP13, ICC profile APP2 segments); a refusal is an <see cref="ArgumentException"/> and anything else is a finding. An
/// image that is made must have a positive size, decode (when it decodes) to its own width and at most its own height, and be
/// the same image when made again.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.8 (Table 13), §8.9.5 (Table 87); ITU-T T.81 Annex B; ICC.1:2022 Annex B.4.</remarks>
internal static class JpegReaderTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            int start = data.IndexOf((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
            data = start < 0 ? [] : data[start..];
        }

        byte[] jpeg = data.ToArray();
        using PdfDocument document = PdfDocument.Create();
        PdfImage image;
        try
        {
            image = document.CreateImageFromJpeg(jpeg);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (image.Width <= 0 || image.Height <= 0 || image.BitsPerComponent != 8)
        {
            throw new InvalidOperationException($"A made JPEG image is {image.Width} x {image.Height} at {image.BitsPerComponent} bits.");
        }

        using (DecodedImage? decoded = image.Decode())
        {
            if (decoded is not null && (decoded.Geometry.Width != image.Width || decoded.DecodedRows > image.Height))
            {
                throw new InvalidOperationException($"A made {image.Width} x {image.Height} image decodes to {decoded.Geometry.Width} x {decoded.DecodedRows}.");
            }
        }

        if (!ReferenceEquals(image.Stream, document.CreateImageFromJpeg(jpeg).Stream))
        {
            throw new InvalidOperationException("The same JPEG made a second image.");
        }
    }
}
