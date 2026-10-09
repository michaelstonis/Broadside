using Broadside.Objects;

namespace Broadside;

/// <summary>The parameters of an embedded file: size, dates and checksum. A live view over the parameter dictionary.</summary>
/// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
public sealed class PdfEmbeddedFileParameters
{
    private readonly PdfDocument _document;

    private readonly CosReference? _reference;

    internal PdfEmbeddedFileParameters(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        _reference = reference;
    }

    /// <summary>Gets the embedded file parameter dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the size of the uncompressed file in bytes (<c>Size</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45.</remarks>
    public long? Size => ViewReading.Integer(_document, Dictionary, FileAndLayerNames.Size);

    /// <summary>Gets the date the file was created (<c>CreationDate</c>), or <see langword="null"/> when absent or unreadable.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45, and §7.9.4. A repaired date is reported as <c>DateInvalid</c>, an unreadable one as <c>DateUnreadable</c>.</remarks>
    public PdfDate? CreationDate => ViewReading.Date(_document, Dictionary, FileAndLayerNames.CreationDate, _reference);

    /// <summary>Gets the date the file was last modified (<c>ModDate</c>), or <see langword="null"/> when absent or unreadable.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45, and §7.9.4. Required when the file is an associated file (§14.13.2).</remarks>
    public PdfDate? ModificationDate => ViewReading.Date(_document, Dictionary, FileAndLayerNames.ModDate, _reference);

    /// <summary>Gets the recorded MD5 checksum of the uncompressed file (<c>CheckSum</c>, 16 bytes), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45. Compare with <see cref="PdfEmbeddedFile.VerifyCheckSum"/>.</remarks>
    public ReadOnlyMemory<byte>? CheckSum => ViewReading.Get(_document, Dictionary, FileAndLayerNames.CheckSum) is CosString value ? value.Bytes.ToArray() : null;

    /// <summary>Gets the Mac OS file information (<c>Mac</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45; deprecated in PDF 2.0.</remarks>
    public CosDictionary? Mac => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Mac) as CosDictionary;
}
