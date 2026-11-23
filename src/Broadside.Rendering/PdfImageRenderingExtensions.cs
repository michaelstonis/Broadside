using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// Renders a <see cref="PdfImage"/> or an <see cref="ExtractedImage"/> at its own sample resolution into a <see cref="Bitmap"/>, and
/// saves it as a PNG or JPEG file.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9 (images), §8.9.5.2 (<c>Decode</c> array, Table 88), §8.9.6 (image masks, explicit masks, colour key masking),
/// §11.6.5.2 (soft-mask images and <c>Matte</c>), §7.4.8 (<c>DCTDecode</c>), §8.6.5.6 (default colour spaces) and §8.6.5.8 (rendering
/// intents); ISO/IEC 15948 (PNG); ITU-T T.81 (ISO/IEC 10918-1, JPEG).
/// </para>
/// <para>
/// <see cref="ToBitmap(PdfImage)"/> decodes the image at its full sample dimensions, maps the samples through its <c>Decode</c> array,
/// converts them to RGB through the document's colour management, and folds any explicit mask, colour key mask, soft mask (resampled
/// at each sample's centre when the mask has another size, §8.9.6.3 and §11.6.5.2) or JPEG 2000 opacity channel (<c>SMaskInData</c>),
/// undoing <c>Matte</c> pre-blending first. An image mask (<c>ImageMask true</c>, §8.9.6.2) paints its samples with the fill colour
/// (<see cref="ExtractedImage.FillColor"/> for an extracted image, opaque black by default) and leaves the rest transparent.
/// </para>
/// <para>
/// <see cref="SaveJpeg(PdfImage, Stream)"/> writes the image's raw JPEG codestream unchanged when <see cref="PdfImage.TryGetJpegData"/>
/// succeeds and no custom quality was asked for (and, for an extracted image, no default colour space replaces the image's device
/// colour space); otherwise it renders the image to a <see cref="Bitmap"/> and encodes it with <see cref="DctEncoder"/>.
/// </para>
/// </remarks>
public static class PdfImageRenderingExtensions
{
    /// <summary>
    /// Decodes <paramref name="image"/> and converts its samples, colour space and masks into a premultiplied BGRA <see cref="Bitmap"/>
    /// at the image's sample dimensions. An image mask (<c>ImageMask true</c>) paints opaque black where its samples paint.
    /// </summary>
    /// <param name="image">The image to decode and convert.</param>
    /// <returns>
    /// A new <see cref="Bitmap"/> that the caller disposes; <see langword="null"/> when the image cannot be decoded or has no usable
    /// colour space (each reason is recorded as a diagnostic on the document).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding the image or its mask.</exception>
    /// <remarks>ISO 32000-2 §8.9.3, §8.9.5.2, §8.9.6 and §11.6.5.2.</remarks>
    public static Bitmap? ToBitmap(this PdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RenderToBitmap(image, PdfColor.FromGray(0), image.Intent ?? RenderingIntent.RelativeColorimetric, PdfDefaultColorSpaces.None);
    }

    /// <summary>
    /// Decodes <paramref name="image"/> and converts its samples, colour space and masks into a premultiplied BGRA <see cref="Bitmap"/>
    /// at the image's sample dimensions, using <paramref name="fillColor"/> when the image is an image mask (<c>ImageMask true</c>).
    /// </summary>
    /// <param name="image">The image to decode and convert.</param>
    /// <param name="fillColor">The colour to paint an image mask with; ignored for images that carry their own colour.</param>
    /// <param name="defaultColorSpaces">The default colour spaces in effect where the image is painted, or <see langword="null"/> for none.</param>
    /// <returns>
    /// A new <see cref="Bitmap"/> that the caller disposes; <see langword="null"/> when the image cannot be decoded or has no usable
    /// colour space.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding the image or its mask.</exception>
    /// <remarks>ISO 32000-2 §8.9.3, §8.9.5.2, §8.9.6, §8.6.5.6 and §11.6.5.2.</remarks>
    public static Bitmap? ToBitmap(this PdfImage image, PdfColor fillColor, PdfDefaultColorSpaces? defaultColorSpaces = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RenderToBitmap(image, fillColor, image.Intent ?? RenderingIntent.RelativeColorimetric, defaultColorSpaces ?? PdfDefaultColorSpaces.None);
    }

    /// <summary>
    /// Decodes <paramref name="extracted"/>'s image and converts it into a premultiplied BGRA <see cref="Bitmap"/> at the image's
    /// sample dimensions, applying the fill colour, rendering intent and default colour spaces in effect when it was painted.
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <returns>
    /// A new <see cref="Bitmap"/> that the caller disposes; <see langword="null"/> when the image cannot be decoded or has no usable
    /// colour space.
    /// </returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding the image or its mask.</exception>
    /// <remarks>ISO 32000-2 §8.9.3, §8.9.5.2, §8.9.6, §8.6.5.6, §8.6.5.8 and §11.6.5.2.</remarks>
    public static Bitmap? ToBitmap(this ExtractedImage extracted) =>
        extracted.Image is { } image
            ? RenderToBitmap(image, extracted.FillColor, extracted.RenderingIntent, extracted.DefaultColorSpaces ?? PdfDefaultColorSpaces.None)
            : null;

    /// <summary>
    /// Decodes <paramref name="image"/> and writes it to <paramref name="stream"/> as a PNG image (8-bit RGB when opaque, 8-bit RGBA
    /// when transparent).
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §8.9; ISO/IEC 15948 (PNG).</remarks>
    public static bool SavePng(this PdfImage image, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        using Bitmap? bitmap = image.ToBitmap();
        if (bitmap is null)
        {
            return false;
        }

        bitmap.SavePng(stream);
        return true;
    }

    /// <summary>Decodes <paramref name="image"/> and writes it as a PNG image to the file at <paramref name="path"/>, replacing it.</summary>
    /// <param name="image">The image to save.</param>
    /// <param name="path">The file path. The file is created only when the image can be decoded.</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §8.9; ISO/IEC 15948 (PNG). As <see cref="SavePng(PdfImage, Stream)"/>.</remarks>
    public static bool SavePng(this PdfImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);
        using Bitmap? bitmap = image.ToBitmap();
        if (bitmap is null)
        {
            return false;
        }

        bitmap.SavePng(path);
        return true;
    }

    /// <summary>
    /// Decodes <paramref name="extracted"/>'s image with its painted colour state and writes it to <paramref name="stream"/> as a PNG
    /// image (8-bit RGB when opaque, 8-bit RGBA when transparent).
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §8.9; ISO/IEC 15948 (PNG).</remarks>
    public static bool SavePng(this ExtractedImage extracted, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using Bitmap? bitmap = extracted.ToBitmap();
        if (bitmap is null)
        {
            return false;
        }

        bitmap.SavePng(stream);
        return true;
    }

    /// <summary>
    /// Decodes <paramref name="extracted"/>'s image with its painted colour state and writes it as a PNG image to the file at
    /// <paramref name="path"/>, replacing it.
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="path">The file path. The file is created only when the image can be decoded.</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §8.9; ISO/IEC 15948 (PNG). As <see cref="SavePng(ExtractedImage, Stream)"/>.</remarks>
    public static bool SavePng(this ExtractedImage extracted, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using Bitmap? bitmap = extracted.ToBitmap();
        if (bitmap is null)
        {
            return false;
        }

        bitmap.SavePng(path);
        return true;
    }

    /// <summary>
    /// Writes <paramref name="image"/> to <paramref name="stream"/> as a JPEG image: its raw <c>DCTDecode</c> codestream unchanged when
    /// <see cref="PdfImage.TryGetJpegData"/> succeeds, otherwise decoded and encoded as a baseline JPEG at quality
    /// <see cref="DctEncoder.DefaultQuality"/> (75) with transparent pixels composited over opaque white.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <returns><see langword="true"/> when the image was written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1).</remarks>
    public static bool SaveJpeg(this PdfImage image, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        if (image.TryGetJpegData(out ReadOnlyMemory<byte> jpeg))
        {
            stream.Write(jpeg.Span);
            return true;
        }

        return SaveJpegReencoded(image, stream, DctEncoder.DefaultQuality);
    }

    /// <summary>
    /// Decodes <paramref name="image"/> and writes it to <paramref name="stream"/> as a baseline JPEG image at
    /// <paramref name="quality"/>, with transparent pixels composited over opaque white.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="quality">The quality, 1 (smallest) to 100 (highest fidelity).</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is outside 1 to 100.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1).</remarks>
    public static bool SaveJpeg(this PdfImage image, Stream stream, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        return SaveJpegReencoded(image, stream, quality);
    }

    /// <summary>
    /// Writes <paramref name="image"/> as a JPEG image to the file at <paramref name="path"/>, replacing it: its raw <c>DCTDecode</c>
    /// codestream unchanged when <see cref="PdfImage.TryGetJpegData"/> succeeds, otherwise decoded and encoded at quality
    /// <see cref="DctEncoder.DefaultQuality"/> (75).
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="path">The file path. The file is created only when the image can be written.</param>
    /// <returns><see langword="true"/> when the image was written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1). As <see cref="SaveJpeg(PdfImage, Stream)"/>.</remarks>
    public static bool SaveJpeg(this PdfImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (image.TryGetJpegData(out ReadOnlyMemory<byte> jpeg))
        {
            using FileStream stream = File.Create(path);
            stream.Write(jpeg.Span);
            return true;
        }

        using Bitmap? bitmap = image.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(path);
        return true;
    }

    /// <summary>
    /// Decodes <paramref name="image"/> and writes it as a baseline JPEG image at <paramref name="quality"/> to the file at
    /// <paramref name="path"/>, replacing it.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="path">The file path. The file is created only when the image can be decoded.</param>
    /// <param name="quality">The quality, 1 (smallest) to 100 (highest fidelity).</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is outside 1 to 100.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1). As <see cref="SaveJpeg(PdfImage, Stream, int)"/>.</remarks>
    public static bool SaveJpeg(this PdfImage image, string path, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        using Bitmap? bitmap = image.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(path, quality);
        return true;
    }

    /// <summary>
    /// Writes <paramref name="extracted"/>'s image to <paramref name="stream"/> as a JPEG image: its raw <c>DCTDecode</c> codestream
    /// unchanged when <see cref="PdfImage.TryGetJpegData"/> succeeds and no default colour space remaps its device colour space,
    /// otherwise decoded with its painted colour state and encoded at quality <see cref="DctEncoder.DefaultQuality"/> (75).
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <returns><see langword="true"/> when the image was written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8, §8.6.5.6 and §8.9; ITU-T T.81 (ISO/IEC 10918-1).</remarks>
    public static bool SaveJpeg(this ExtractedImage extracted, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (extracted.Image is not { } image)
        {
            return false;
        }

        if (CanPassThroughJpeg(extracted) && image.TryGetJpegData(out ReadOnlyMemory<byte> jpeg))
        {
            stream.Write(jpeg.Span);
            return true;
        }

        return SaveJpegReencoded(extracted, stream, DctEncoder.DefaultQuality);
    }

    /// <summary>
    /// Decodes <paramref name="extracted"/>'s image with its painted colour state and writes it to <paramref name="stream"/> as a
    /// baseline JPEG image at <paramref name="quality"/>.
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="quality">The quality, 1 (smallest) to 100 (highest fidelity).</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is outside 1 to 100.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1).</remarks>
    public static bool SaveJpeg(this ExtractedImage extracted, Stream stream, int quality)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        return SaveJpegReencoded(extracted, stream, quality);
    }

    /// <summary>
    /// Writes <paramref name="extracted"/>'s image as a JPEG image to the file at <paramref name="path"/>, replacing it: its raw
    /// <c>DCTDecode</c> codestream unchanged when eligible, otherwise decoded with its painted colour state and encoded at quality
    /// <see cref="DctEncoder.DefaultQuality"/> (75).
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="path">The file path. The file is created only when the image can be written.</param>
    /// <returns><see langword="true"/> when the image was written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1). As <see cref="SaveJpeg(ExtractedImage, Stream)"/>.</remarks>
    public static bool SaveJpeg(this ExtractedImage extracted, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (extracted.Image is not { } image)
        {
            return false;
        }

        if (CanPassThroughJpeg(extracted) && image.TryGetJpegData(out ReadOnlyMemory<byte> jpeg))
        {
            using FileStream stream = File.Create(path);
            stream.Write(jpeg.Span);
            return true;
        }

        using Bitmap? bitmap = extracted.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(path);
        return true;
    }

    /// <summary>
    /// Decodes <paramref name="extracted"/>'s image with its painted colour state and writes it as a baseline JPEG image at
    /// <paramref name="quality"/> to the file at <paramref name="path"/>, replacing it.
    /// </summary>
    /// <param name="extracted">The extracted image.</param>
    /// <param name="path">The file path. The file is created only when the image can be decoded.</param>
    /// <param name="quality">The quality, 1 (smallest) to 100 (highest fidelity).</param>
    /// <returns><see langword="true"/> when the image was decoded and written; <see langword="false"/> when it could not be decoded.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is outside 1 to 100.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 and §8.9; ITU-T T.81 (ISO/IEC 10918-1). As <see cref="SaveJpeg(ExtractedImage, Stream, int)"/>.</remarks>
    public static bool SaveJpeg(this ExtractedImage extracted, string path, int quality)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        using Bitmap? bitmap = extracted.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(path, quality);
        return true;
    }

    private static bool CanPassThroughJpeg(in ExtractedImage extracted) =>
        extracted.Image is { ColorSpace: { } space }
        && (extracted.DefaultColorSpaces is null || ReferenceEquals(extracted.DefaultColorSpaces.Remap(space), space));

    private static bool SaveJpegReencoded(PdfImage image, Stream stream, int quality)
    {
        using Bitmap? bitmap = image.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(stream, quality);
        return true;
    }

    private static bool SaveJpegReencoded(in ExtractedImage extracted, Stream stream, int quality)
    {
        using Bitmap? bitmap = extracted.ToBitmap();
        if (bitmap is null || bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            return false;
        }

        bitmap.SaveJpeg(stream, quality);
        return true;
    }

    private static Bitmap? RenderToBitmap(PdfImage image, PdfColor fillColor, RenderingIntent intent, PdfDefaultColorSpaces defaults)
    {
        using DecodedImage? decoded = image.Decode();
        if (decoded is null)
        {
            return null;
        }

        int width = decoded.Geometry.Width;
        int height = decoded.Geometry.Height;
        if (width <= 0 || height <= 0 || (long)width * height * 4 > Array.MaxLength)
        {
            return null;
        }

        var conversion = new ColorConversion { Target = DeviceColorModel.Rgb, Intent = intent };
        if (image.IsImageMask)
        {
            return RenderImageMask(image, decoded, fillColor, conversion, defaults);
        }

        if (image.ResolveColorSpace(decoded) is not { } space
            || space.Family == PdfColorSpaceFamily.Pattern
            || space.ComponentCount != decoded.Geometry.Components)
        {
            return null;
        }

        return RenderColorImage(image, decoded, space, conversion, defaults);
    }

    private static Bitmap? RenderImageMask(PdfImage image, DecodedImage decoded, PdfColor fillColor, ColorConversion conversion, PdfDefaultColorSpaces defaults)
    {
        if (decoded.Geometry.Components != 1 || decoded.Geometry.BitsPerComponent != 1 || fillColor.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            return null;
        }

        int width = decoded.Geometry.Width;
        int height = decoded.Geometry.Height;
        PdfColorConverter converter = image.Document.GetColorConverter(fillColor.ColorSpace, conversion, defaults);
        if (converter.PaintsNothing)
        {
            return new Bitmap(width, height);
        }

        Span<float> rgbFloat = stackalloc float[3];
        converter.Convert(fillColor.Components, rgbFloat);
        byte r = (byte)((Math.Clamp(rgbFloat[0], 0f, 1f) * 255f) + 0.5f);
        byte g = (byte)((Math.Clamp(rgbFloat[1], 0f, 1f) * 255f) + 0.5f);
        byte b = (byte)((Math.Clamp(rgbFloat[2], 0f, 1f) * 255f) + 0.5f);

        bool inverted = image.CreateDecodeMap(decoded).IsInverted;
        var bitmap = new Bitmap(width, height);
        byte[] coverage = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            Span<byte> alpha = coverage.AsSpan(0, width);
            for (int y = 0; y < height; y++)
            {
                ImageRows.ImageMask(decoded.GetRow(y), width, inverted, alpha);
                Span<byte> row = bitmap.GetRow(y);
                for (int x = 0; x < width; x++)
                {
                    if (alpha[x] != 0)
                    {
                        int offset = x * 4;
                        row[offset] = b;
                        row[offset + 1] = g;
                        row[offset + 2] = r;
                        row[offset + 3] = 255;
                    }
                }
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(coverage);
        }
    }

    private static Bitmap RenderColorImage(PdfImage image, DecodedImage decoded, PdfColorSpace space, ColorConversion conversion, PdfDefaultColorSpaces defaults)
    {
        int width = decoded.Geometry.Width;
        int height = decoded.Geometry.Height;
        int components = decoded.Geometry.Components;
        int values = width * components;
        var indexed = space as PdfIndexedColorSpace;
        PdfColorConverter converter = image.Document.GetColorConverter(space, conversion, defaults);
        PdfColorConverter? baseConverter = indexed is not null ? image.Document.GetColorConverter(indexed.Base, conversion, defaults) : null;
        ImageDecodeMap map = image.CreateDecodeMap(decoded);

        Span<ComponentRange> ranges = stackalloc ComponentRange[components];
        for (int c = 0; c < components; c++)
        {
            ranges[c] = space.GetComponentRange(c);
        }

        DecodedImage? maskDecoded = null;
        ImageDecodeMap? maskMap = null;
        bool explicitMask = false;
        bool softMask = false;
        float[] matte = [];
        double[] colorKey = [];

        try
        {
            switch (image.MaskKind)
            {
                case PdfImageMaskKind.Explicit when image.Mask is { } maskModel:
                    maskDecoded = maskModel.Decode();
                    if (maskDecoded is { Geometry: { Components: 1, BitsPerComponent: 1, Width: > 0, Height: > 0 } })
                    {
                        maskMap = maskModel.CreateDecodeMap(maskDecoded);
                        explicitMask = true;
                    }
                    else
                    {
                        maskDecoded?.Dispose();
                        maskDecoded = null;
                    }

                    break;
                case PdfImageMaskKind.Soft when image.Mask is { } softModel:
                    maskDecoded = softModel.Decode();
                    if (maskDecoded is { Geometry: { Components: 1, Width: > 0, Height: > 0 } })
                    {
                        maskMap = softModel.CreateDecodeMap(maskDecoded);
                        softMask = true;
                        if (image.Matte is { } matteList)
                        {
                            matte = [.. matteList.Select(static m => (float)m)];
                        }
                    }
                    else
                    {
                        maskDecoded?.Dispose();
                        maskDecoded = null;
                    }

                    break;
                case PdfImageMaskKind.SoftInData when decoded.Alpha is { Geometry: { Components: 1, Width: > 0, Height: > 0 } } alpha:
                    maskDecoded = alpha;
                    maskMap = ImageDecodeMap.Create([0, 1], 1, alpha.Geometry.BitsPerComponent);
                    softMask = true;
                    if (image.SoftMaskInData == PdfSoftMaskInData.PremultipliedAlpha || decoded.AlphaPremultiplied)
                    {
                        int matteCount = indexed is not null ? indexed.Base.ComponentCount : components;
                        matte = new float[matteCount];
                    }

                    break;
                case PdfImageMaskKind.ColorKey when image.ColorKey is { } rangesList:
                    colorKey = [.. rangesList];
                    break;
            }

            bool sameSizeSoft = softMask && maskDecoded!.Geometry.Width == width && maskDecoded.Geometry.Height == height;
            bool hasMatte = sameSizeSoft && matte.Length > 0
                && (indexed is null ? matte.Length == components : baseConverter is not null && matte.Length == indexed.Base.ComponentCount);
            bool hasColorKey = colorKey.Length >= 2 * components;
            bool hasAlpha = softMask || explicitMask || hasColorKey;

            RowConversionKind rowKind = hasMatte ? (indexed is null ? RowConversionKind.Values : RowConversionKind.TableValues)
                : indexed is not null ? RowConversionKind.Indices
                : decoded.StorageBits <= 8 && IsDefaultDecode(map, ranges) ? RowConversionKind.Bytes
                : RowConversionKind.Values;

            int colorComponents = rowKind == RowConversionKind.TableValues ? indexed!.Base.ComponentCount : components;
            int colorValues = width * colorComponents;
            Span<ComponentRange> baseRanges = stackalloc ComponentRange[rowKind == RowConversionKind.TableValues ? colorComponents : 0];
            for (int c = 0; c < baseRanges.Length; c++)
            {
                baseRanges[c] = indexed!.Base.GetComponentRange(c);
            }

            ReadOnlyMemory<byte> lookupTable = rowKind == RowConversionKind.TableValues ? indexed!.GetLookup() : ReadOnlyMemory<byte>.Empty;
            int highValue = indexed?.HighValue ?? 0;

            int maskWidth = maskDecoded?.Geometry.Width ?? 0;
            int floatCapacity = colorValues + Math.Max(width * 3, width + maskWidth);
            ushort[] rawBuffer = ArrayPool<ushort>.Shared.Rent(values + Math.Max(width, maskWidth));
            byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(values);
            byte[] rgbBuffer = ArrayPool<byte>.Shared.Rent(width * 3);
            byte[]? opacityBuffer = hasAlpha ? ArrayPool<byte>.Shared.Rent(width + maskWidth) : null;
            float[]? floatBuffer = rowKind is RowConversionKind.Values or RowConversionKind.TableValues || softMask
                ? ArrayPool<float>.Shared.Rent(floatCapacity)
                : null;

            var bitmap = new Bitmap(width, height);
            try
            {
                Span<ushort> raw = rawBuffer.AsSpan(0, values);
                Span<byte> bytes = byteBuffer.AsSpan(0, values);
                Span<byte> rgb = rgbBuffer.AsSpan(0, width * 3);
                Span<byte> opacity = hasAlpha ? opacityBuffer!.AsSpan(0, width) : Span<byte>.Empty;

                for (int y = 0; y < height; y++)
                {
                    ReadOnlySpan<byte> row = decoded.GetRow(y);
                    if (rowKind != RowConversionKind.Bytes || hasColorKey)
                    {
                        ImageRows.Unpack(row, decoded.StorageBits, values, raw);
                    }

                    if (softMask)
                    {
                        SampleSoftMaskRow(
                            maskDecoded!,
                            maskMap!,
                            y,
                            width,
                            height,
                            opacity,
                            opacityBuffer!.AsSpan(width, maskWidth),
                            rawBuffer.AsSpan(values, maskWidth),
                            floatBuffer!.AsSpan(colorValues + width, maskWidth));
                    }
                    else if (explicitMask)
                    {
                        SampleExplicitMaskRow(
                            maskDecoded!,
                            maskMap!.IsInverted,
                            y,
                            width,
                            height,
                            opacity,
                            opacityBuffer!.AsSpan(width, maskWidth));
                    }
                    else if (hasColorKey)
                    {
                        ImageRows.ColorKey(raw, components, colorKey, opacity);
                    }

                    switch (rowKind)
                    {
                        case RowConversionKind.Indices:
                            map.MapToIndices(raw, bytes, highValue);
                            converter.Convert(bytes, rgb, width);
                            break;
                        case RowConversionKind.Bytes:
                            ImageRows.UnpackScaled(row, decoded.StorageBits, decoded.Geometry.BitsPerComponent, values, bytes);
                            converter.Convert(bytes, rgb, width);
                            break;
                        case RowConversionKind.TableValues:
                            map.MapToIndices(raw, bytes, highValue);
                            Span<float> tableValues = floatBuffer!.AsSpan(0, colorValues);
                            LookupTableValues(bytes, lookupTable.Span, baseRanges, tableValues);
                            UnmatteRow(opacity, matte, baseRanges, tableValues, floatBuffer.AsSpan(colorValues, width));
                            ConvertFloatsToRgb(baseConverter!, tableValues, rgb, width, floatBuffer.AsSpan(colorValues, width * 3));
                            break;
                        default:
                            Span<float> componentValues = floatBuffer!.AsSpan(0, values);
                            map.Map(raw, componentValues, ranges);
                            if (hasMatte)
                            {
                                UnmatteRow(opacity, matte, ranges, componentValues, floatBuffer.AsSpan(colorValues, width));
                            }

                            ConvertFloatsToRgb(converter, componentValues, rgb, width, floatBuffer.AsSpan(colorValues, width * 3));
                            break;
                    }

                    ImageRows.PackBgra(rgb, opacity, width, bitmap.GetRow(y));
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                ArrayPool<ushort>.Shared.Return(rawBuffer);
                ArrayPool<byte>.Shared.Return(byteBuffer);
                ArrayPool<byte>.Shared.Return(rgbBuffer);
                if (opacityBuffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(opacityBuffer);
                }

                if (floatBuffer is not null)
                {
                    ArrayPool<float>.Shared.Return(floatBuffer);
                }
            }
        }
        finally
        {
            if (maskDecoded is not null && !ReferenceEquals(maskDecoded, decoded.Alpha))
            {
                maskDecoded.Dispose();
            }
        }
    }

    private static void SampleSoftMaskRow(
        DecodedImage mask,
        ImageDecodeMap map,
        int y,
        int width,
        int height,
        Span<byte> opacity,
        Span<byte> maskRowScratch,
        Span<ushort> rawScratch,
        Span<float> floatScratch)
    {
        int maskWidth = mask.Geometry.Width;
        int maskHeight = mask.Geometry.Height;
        int my = maskHeight == height ? y : ImageRows.MaskIndex(y, height, maskHeight);
        Span<byte> maskRow = maskWidth == width ? opacity : maskRowScratch;
        int top = (1 << map.BitsPerComponent) - 1;
        if (mask.StorageBits <= 8 && map.Map(0, 0) == 0f && map.Map(0, top) == 1f)
        {
            ImageRows.UnpackScaled(mask.GetRow(my), mask.StorageBits, mask.Geometry.BitsPerComponent, maskWidth, maskRow);
        }
        else
        {
            ImageRows.Unpack(mask.GetRow(my), mask.StorageBits, maskWidth, rawScratch);
            map.Map(rawScratch[..maskWidth], floatScratch);
            for (int x = 0; x < maskWidth; x++)
            {
                maskRow[x] = (byte)((Math.Clamp(floatScratch[x], 0f, 1f) * 255f) + 0.5f);
            }
        }

        if (maskWidth != width)
        {
            for (int x = 0; x < width; x++)
            {
                opacity[x] = maskRow[ImageRows.MaskIndex(x, width, maskWidth)];
            }
        }
    }

    private static void SampleExplicitMaskRow(
        DecodedImage mask,
        bool inverted,
        int y,
        int width,
        int height,
        Span<byte> opacity,
        Span<byte> maskRowScratch)
    {
        int maskWidth = mask.Geometry.Width;
        int maskHeight = mask.Geometry.Height;
        int my = maskHeight == height ? y : ImageRows.MaskIndex(y, height, maskHeight);
        Span<byte> maskRow = maskWidth == width ? opacity : maskRowScratch;
        ImageRows.ImageMask(mask.GetRow(my), maskWidth, inverted, maskRow);
        if (maskWidth != width)
        {
            for (int x = 0; x < width; x++)
            {
                opacity[x] = maskRow[ImageRows.MaskIndex(x, width, maskWidth)];
            }
        }
    }

    private static void UnmatteRow(
        ReadOnlySpan<byte> opacity,
        ReadOnlySpan<float> matte,
        ReadOnlySpan<ComponentRange> ranges,
        Span<float> values,
        Span<float> alphaScratch)
    {
        for (int x = 0; x < opacity.Length; x++)
        {
            alphaScratch[x] = opacity[x] / 255f;
        }

        ImageRows.Unpremultiply(values, ranges.Length, alphaScratch, matte, ranges);
    }

    private static void LookupTableValues(
        ReadOnlySpan<byte> indices,
        ReadOnlySpan<byte> table,
        ReadOnlySpan<ComponentRange> ranges,
        Span<float> values)
    {
        int n = ranges.Length;
        for (int i = 0, v = 0; i < indices.Length; i++)
        {
            int entry = indices[i] * n;
            for (int c = 0; c < n; c++, v++)
            {
                int value = entry + c < table.Length ? table[entry + c] : 0;
                values[v] = (float)(ranges[c].Minimum + (value / 255.0 * (ranges[c].Maximum - ranges[c].Minimum)));
            }
        }
    }

    private static void ConvertFloatsToRgb(
        PdfColorConverter converter,
        ReadOnlySpan<float> values,
        Span<byte> rgb,
        int width,
        Span<float> rgbValuesScratch)
    {
        converter.Convert(values, rgbValuesScratch, width);
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)((Math.Clamp(rgbValuesScratch[i], 0f, 1f) * 255f) + 0.5f);
        }
    }

    private static bool IsDefaultDecode(ImageDecodeMap map, ReadOnlySpan<ComponentRange> ranges)
    {
        int top = (1 << map.BitsPerComponent) - 1;
        for (int c = 0; c < ranges.Length; c++)
        {
            if (Math.Abs(map.Map(c, 0) - ranges[c].Minimum) > 1e-6 || Math.Abs(map.Map(c, top) - ranges[c].Maximum) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private enum RowConversionKind
    {
        Indices,
        Bytes,
        Values,
        TableValues,
    }
}
