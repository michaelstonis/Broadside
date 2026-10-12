using System.IO.Compression;

namespace Broadside;

/// <summary>Options for one save: how the written file is laid out and which streams are compressed.</summary>
/// <remarks>
/// ISO 32000-2 §7.5. Separate from <see cref="PdfOptions"/>, which configures reading, because a document can be saved several times
/// in different layouts. The defaults write a classic cross-reference table, which every reader supports, and compress new and
/// changed streams.
/// </remarks>
public sealed class PdfSaveOptions
{
    private PdfCrossReferenceLayout _crossReferenceLayout = PdfCrossReferenceLayout.Table;
    private PdfStreamCompression _streamCompression = PdfStreamCompression.ChangedStreams;
    private CompressionLevel _compressionLevel = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets how the file stores its cross-reference information and objects. The default is
    /// <see cref="PdfCrossReferenceLayout.Table"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8. See <see cref="PdfCrossReferenceLayout"/> for the combinations and their versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined layout.</exception>
    public PdfCrossReferenceLayout CrossReferenceLayout
    {
        get => _crossReferenceLayout;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a cross-reference layout.");
            }

            _crossReferenceLayout = value;
        }
    }

    /// <summary>
    /// Gets or sets which unfiltered streams are compressed with the <c>FlateDecode</c> encoder. The default is
    /// <see cref="PdfStreamCompression.ChangedStreams"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4. See <see cref="PdfStreamCompression"/> for which streams qualify.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined setting.</exception>
    public PdfStreamCompression StreamCompression
    {
        get => _streamCompression;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a stream compression setting.");
            }

            _streamCompression = value;
        }
    }

    /// <summary>
    /// Gets or sets how hard the <c>FlateDecode</c> encoder compresses the streams it writes, the object streams and cross-reference
    /// stream included. The default is <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4.1: RFC 1951 leaves the effort to the encoder; every level decodes the same way.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined level.</exception>
    public CompressionLevel CompressionLevel
    {
        get => _compressionLevel;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a compression level.");
            }

            _compressionLevel = value;
        }
    }

    /// <summary>Sets <see cref="CrossReferenceLayout"/>.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layout"/> is not a defined layout.</exception>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8.</remarks>
    public PdfSaveOptions WithCrossReferenceLayout(PdfCrossReferenceLayout layout)
    {
        CrossReferenceLayout = layout;
        return this;
    }

    /// <summary>Sets <see cref="StreamCompression"/>.</summary>
    /// <param name="compression">Which unfiltered streams to compress.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compression"/> is not a defined setting.</exception>
    /// <remarks>ISO 32000-2 §7.4.4.</remarks>
    public PdfSaveOptions WithStreamCompression(PdfStreamCompression compression)
    {
        StreamCompression = compression;
        return this;
    }

    /// <summary>Sets <see cref="CompressionLevel"/>.</summary>
    /// <param name="level">How hard to compress.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined level.</exception>
    /// <remarks>ISO 32000-2 §7.4.4.1.</remarks>
    public PdfSaveOptions WithCompressionLevel(CompressionLevel level)
    {
        CompressionLevel = level;
        return this;
    }
}
