using System.Globalization;
using Broadside.Objects;

namespace Broadside;

/// <summary>One entry of the document's <c>EmbeddedFiles</c> name tree: a name and the file specification it maps to.</summary>
/// <remarks>
/// ISO 32000-2 §7.7.4, Table 32 (<c>EmbeddedFiles</c>, PDF 1.4), and §7.11.4.1. In a portable collection with folders, a key of the
/// form <c>&lt;id&gt;name</c> places the file in the folder with that ID; any other key places it in the root folder (§12.3.5).
/// </remarks>
public sealed class PdfEmbeddedFileEntry
{
    internal PdfEmbeddedFileEntry(CosString key, PdfFileSpecification file)
    {
        Key = key;
        File = file;
    }

    /// <summary>Gets the name tree key, as stored.</summary>
    /// <remarks>ISO 32000-2 §7.9.6.</remarks>
    public CosString Key { get; }

    /// <summary>Gets the name tree key, decoded as text.</summary>
    public string Name => Key.DecodeText();

    /// <summary>Gets the file specification the name maps to.</summary>
    /// <remarks>ISO 32000-2 §7.11.3.</remarks>
    public PdfFileSpecification File { get; }

    /// <summary>Gets the collection folder ID the key's <c>&lt;id&gt;</c> prefix names, or <see langword="null"/> without a prefix.</summary>
    /// <remarks>ISO 32000-2 §12.3.5 (PDF 2.0), "Folders".</remarks>
    public int? FolderId => SplitFolder(Name, out _);

    /// <summary>Gets the key without its <c>&lt;id&gt;</c> folder prefix.</summary>
    /// <remarks>ISO 32000-2 §12.3.5 (PDF 2.0), "Folders".</remarks>
    public string FileName
    {
        get
        {
            SplitFolder(Name, out string name);
            return name;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>Splits a <c>&lt;digits&gt;name</c> key.</summary>
    internal static int? SplitFolder(string key, out string name)
    {
        int close = key.IndexOf('>', StringComparison.Ordinal);
        if (key.Length > 2 && key[0] == '<' && close > 1
            && !key.AsSpan(1, close - 1).ContainsAnyExceptInRange('0', '9')
            && int.TryParse(key.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            name = key[(close + 1)..];
            return id;
        }

        name = key;
        return null;
    }
}
