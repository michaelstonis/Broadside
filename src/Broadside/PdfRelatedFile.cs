using Broadside.Objects;

namespace Broadside;

/// <summary>One entry of a related files array: a file that belongs with an embedded file, such as a resource of a Mac OS file.</summary>
/// <remarks>ISO 32000-2 §7.11.4.2.</remarks>
public sealed class PdfRelatedFile
{
    internal PdfRelatedFile(CosString name, PdfEmbeddedFile file)
    {
        NameString = name;
        File = file;
    }

    /// <summary>Gets the name of the related file, as stored.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.2.</remarks>
    public CosString NameString { get; }

    /// <summary>Gets the name of the related file, decoded as text.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.2.</remarks>
    public string Name => NameString.DecodeText();

    /// <summary>Gets the embedded file stream that holds the related file.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.2.</remarks>
    public PdfEmbeddedFile File { get; }
}
