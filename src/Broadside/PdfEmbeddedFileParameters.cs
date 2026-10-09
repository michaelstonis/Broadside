using Broadside.Objects;

namespace Broadside;

/// <summary>The parameters of an embedded file: size, dates and checksum. A live view over the parameter dictionary.</summary>
/// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
public sealed class PdfEmbeddedFileParameters
{
    private readonly PdfDocument _document;

    internal PdfEmbeddedFileParameters(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the embedded file parameter dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the size of the uncompressed file in bytes (<c>Size</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public long? Size => ViewReading.Integer(_document, Dictionary, FileAndLayerNames.Size);

    /// <summary>Gets the creation date as written (<c>CreationDate</c>, a §7.9.4 date string), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public CosString? CreationDate => ViewReading.Get(_document, Dictionary, FileAndLayerNames.CreationDate) as CosString;

    /// <summary>Gets the date the file was last modified as written (<c>ModDate</c>, a §7.9.4 date string), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45. Required when the file is an associated file (§14.13.2).</remarks>
    public CosString? ModificationDate => ViewReading.Get(_document, Dictionary, FileAndLayerNames.ModDate) as CosString;

    /// <summary>Gets the recorded MD5 checksum of the uncompressed file (<c>CheckSum</c>, 16 bytes), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45. Compare with <see cref="PdfEmbeddedFile.VerifyCheckSum"/>.</remarks>
    public ReadOnlyMemory<byte>? CheckSum => ViewReading.Get(_document, Dictionary, FileAndLayerNames.CheckSum) is CosString value ? value.Bytes.ToArray() : null;

    /// <summary>Gets the Mac OS file information (<c>Mac</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45; deprecated in PDF 2.0.</remarks>
    public CosDictionary? Mac => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Mac) as CosDictionary;
}
