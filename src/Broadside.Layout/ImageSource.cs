using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>
/// Where an image for the document builder comes from: a PNG, JPEG or SVG file or bytes, independent of any document, or an image already
/// in one. A document embeds each source once however many times it is drawn (a logo repeated on 200 invoices is one image XObject).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image XObjects): PNG is written through <see cref="PdfDocument.CreateImageFromPng"/> and JPEG through
/// <see cref="PdfDocument.CreateImageFromJpeg"/>, chosen by the file signature. SVG (markup starting with <c>&lt;svg</c> or
/// <c>&lt;?xml</c>, or gzip-compressed SVGZ) is imported as a form XObject through
/// <see cref="PdfDocument.CreateFormXObjectFromSvg(ReadOnlySpan{byte}, Graphics.SvgImportOptions?)"/> and drawn as vector graphics
/// (§8.10). A file is read once, the first time a document needs it.
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
    private System.Runtime.CompilerServices.ConditionalWeakTable<PdfDocument, PdfFormXObject>? _forms;

    private ImageSource(string? path, ReadOnlyMemory<byte> bytes, PdfImage? image)
    {
        _path = path;
        _bytes = bytes;
        _image = image;
        _loaded = path is null;
    }

    /// <summary>Returns a source that reads a PNG, JPEG or SVG file the first time a document needs it.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static ImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ImageSource(path, default, null);
    }

    /// <summary>Returns a source over PNG, JPEG or SVG bytes. The bytes are not copied; do not change them while a document uses the source.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentException">The bytes are neither PNG, JPEG nor SVG.</exception>
    public static ImageSource FromBytes(ReadOnlyMemory<byte> bytes)
    {
        _ = IsSvgData(bytes.Span) || FormatOf(bytes.Span, nameof(bytes));
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
        if (IsSvgData(bytes.Span))
        {
            throw new NotSupportedException($"The source {this} is SVG, which is drawn as a vector figure (ContainerBuilder.Svg or Image), not as a page background.");
        }

        return FormatOf(bytes.Span, "source") ? document.CreateImageFromPng(bytes.Span) : document.CreateImageFromJpeg(bytes.Span);
    }

    /// <summary>Gets a value indicating whether the source holds SVG (issue #643); reads a file source the first time.</summary>
    internal bool IsSvg => _image is null && IsSvgData(Bytes.Span);

    /// <summary>Imports the SVG of the source as a form XObject of <paramref name="document"/>, once per document however often it is drawn.</summary>
    internal PdfFormXObject EmbedSvg(PdfDocument document)
    {
        ReadOnlyMemory<byte> bytes = Bytes;
        lock (_gate)
        {
            _forms ??= [];
            if (!_forms.TryGetValue(document, out PdfFormXObject? form))
            {
                form = document.CreateFormXObjectFromSvg(bytes.Span, null);
                _forms.Add(document, form);
            }

            return form;
        }
    }

    /// <summary>True for SVG or SVGZ: a gzip signature, or markup that starts with <c>&lt;svg</c>, <c>&lt;?xml</c> or a comment, after a byte order mark and white space.</summary>
    private static bool IsSvgData(ReadOnlySpan<byte> bytes)
    {
        if (bytes is [0x1F, 0x8B, ..] or [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            return true;
        }

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            bytes = bytes[3..];
        }

        bytes = bytes.TrimStart(" \t\r\n"u8);
        return bytes.StartsWith("<svg"u8) || bytes.StartsWith("<?xml"u8) || bytes.StartsWith("<!--"u8);
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
