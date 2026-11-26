using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>
/// Where an image for the document builder comes from: a PNG or JPEG file or bytes, independent of any document, or an image already in
/// one. A document embeds each source once however many times it is drawn (a logo repeated on 200 invoices is one image XObject).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image XObjects): PNG is written through <see cref="PdfDocument.CreateImageFromPng"/> and JPEG through
/// <see cref="PdfDocument.CreateImageFromJpeg"/>, chosen by the file signature. A file is read once, the first time a document needs it.
/// A source is immutable and safe to share across documents and threads, except one made <see cref="FromImage"/>, which belongs to its
/// document.
/// </para>
/// </remarks>
public sealed class ImageSource
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

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

    /// <summary>Returns a source that reads a PNG or JPEG file the first time a document needs it.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static ImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ImageSource(path, default, null);
    }

    /// <summary>Returns a source over PNG or JPEG bytes. The bytes are not copied; do not change them while a document uses the source.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException">The bytes are neither PNG nor JPEG.</exception>
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

        ReadOnlyMemory<byte> bytes = Bytes;
        return FormatOf(bytes.Span, "source") ? document.CreateImageFromPng(bytes.Span) : document.CreateImageFromJpeg(bytes.Span);
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

    /// <summary>True for PNG, false for JPEG; throws for anything else.</summary>
    private static bool FormatOf(ReadOnlySpan<byte> bytes, string parameter)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return true;
        }

        if (bytes is [0xFF, 0xD8, 0xFF, ..])
        {
            return false;
        }

        throw new ArgumentException("The image is neither PNG nor JPEG.", parameter);
    }
}
