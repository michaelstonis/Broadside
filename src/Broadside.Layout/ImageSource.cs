using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>
/// Where an image for the document builder comes from: a PNG, JPEG, TIFF, GIF or BMP file or bytes, independent of any document, or an
/// image already in one. A document embeds each source once however many times it is drawn (a logo repeated on 200 invoices is one image XObject).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image XObjects): PNG is written through <see cref="PdfDocument.CreateImageFromPng"/>, JPEG through
/// <see cref="PdfDocument.CreateImageFromJpeg"/>, TIFF through <see cref="PdfDocument.CreateImageFromTiff"/> (its first page), GIF through
/// <see cref="PdfDocument.CreateImageFromGif"/> (its first frame) and BMP through <see cref="PdfDocument.CreateImageFromBmp"/>, chosen by
/// the file signature. A file is read once, the first time a document needs it.
/// A source is immutable and safe to share across documents and threads, except one made <see cref="FromImage"/>, which belongs to its
/// document.
/// </para>
/// </remarks>
public sealed class ImageSource
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private enum Format
    {
        Png,
        Jpeg,
        Tiff,
        Gif,
        Bmp,
    }

    private readonly string? _path;
    private readonly PdfImage? _image;
    private readonly Lock _gate = new();
    private ReadOnlyMemory<byte> _bytes;
    private bool _loaded;

    private ImageSource(string? path, ReadOnlyMemory<byte> bytes, PdfImage? image)
    {
        _path = path;
        _bytes = bytes;
        _image = image;
        _loaded = path is null;
    }

    /// <summary>Returns a source that reads a PNG, JPEG, TIFF, GIF or BMP file the first time a document needs it.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static ImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ImageSource(path, default, null);
    }

    /// <summary>Returns a source over PNG, JPEG, TIFF, GIF or BMP bytes. The bytes are not copied; do not change them while a document uses the source.</summary>
    /// <param name="bytes">The encoded image; of a TIFF file the first page is drawn, of a GIF file the first frame.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException">The bytes are none of PNG, JPEG, TIFF, GIF or BMP.</exception>
    public static ImageSource FromBytes(ReadOnlyMemory<byte> bytes)
    {
        _ = FormatOf(bytes.Span, nameof(bytes));
        return new ImageSource(null, bytes, null);
    }

    /// <summary>Returns a source over an image already in a document; only that document may draw it.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    public static ImageSource FromImage(PdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new ImageSource(null, default, image);
    }

    /// <inheritdoc/>
    public override string ToString() => _path ?? (_image is null ? "image bytes" : "document image");

    /// <summary>Embeds the image in <paramref name="document"/> (the caller caches per document).</summary>
    internal PdfImage Embed(PdfDocument document)
    {
        if (_image is { } image)
        {
            return image;
        }

        ReadOnlySpan<byte> bytes = Bytes.Span;
        return FormatOf(bytes, "source") switch
        {
            Format.Png => document.CreateImageFromPng(bytes),
            Format.Tiff => document.CreateImageFromTiff(bytes, 0),
            Format.Gif => document.CreateImageFromGif(bytes),
            Format.Bmp => document.CreateImageFromBmp(bytes),
            _ => document.CreateImageFromJpeg(bytes),
        };
    }

    private ReadOnlyMemory<byte> Bytes
    {
        get
        {
            lock (_gate)
            {
                if (!_loaded)
                {
                    _bytes = File.ReadAllBytes(_path!);
                    _loaded = true;
                }

                return _bytes;
            }
        }
    }

    /// <summary>The format by the file signature; throws for anything else.</summary>
    private static Format FormatOf(ReadOnlySpan<byte> bytes, string parameter)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return Format.Png;
        }

        if (bytes is [0xFF, 0xD8, 0xFF, ..])
        {
            return Format.Jpeg;
        }

        if (bytes is [(byte)'I', (byte)'I', 42, 0, ..] or [(byte)'M', (byte)'M', 0, 42, ..])
        {
            return Format.Tiff;
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return Format.Gif;
        }

        if (bytes is [(byte)'B', (byte)'M', ..])
        {
            return Format.Bmp;
        }

        throw new ArgumentException("The image is none of PNG, JPEG, TIFF, GIF or BMP.", parameter);
    }
}
