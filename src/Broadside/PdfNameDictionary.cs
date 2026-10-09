using Broadside.Objects;

namespace Broadside;

/// <summary>The document's name dictionary: the name trees that map names to destinations, embedded files, scripts and more. A live view.</summary>
/// <remarks>
/// ISO 32000-2 §7.7.4, Table 32 (PDF 1.2), reached through the catalog's <c>Names</c> entry (§7.7.2, Table 29). Every entry is a name
/// tree (§7.9.6): <c>Dests</c> (PDF 1.2), <c>AP</c>, <c>JavaScript</c>, <c>Pages</c>, <c>Templates</c>, <c>IDS</c>, <c>URLS</c> (PDF 1.3),
/// <c>EmbeddedFiles</c>, <c>AlternatePresentations</c> (PDF 1.4; deprecated in PDF 2.0) and <c>Renditions</c> (PDF 1.5). Each is
/// reachable through <see cref="GetTree"/>; the typed views of their values arrive with their features.
/// </remarks>
public sealed class PdfNameDictionary
{
    private readonly PdfDocument _document;

    internal PdfNameDictionary(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the name dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the tree of named destinations (string keys), or <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.4, Table 32 (PDF 1.2), and §12.3.2.4: each value is a destination array, or a dictionary whose <c>D</c> entry
    /// is one. Named destinations of PDF 1.1 (name keys) are in the catalog's own <c>Dests</c> dictionary instead.
    /// </remarks>
    public PdfNameTree? Dests => GetTree(KnownNames.Dests);

    /// <summary>Gets the name tree under <paramref name="key"/>, or <see langword="null"/> when the entry is absent.</summary>
    /// <param name="key">The entry, for example <c>EmbeddedFiles</c> or <c>JavaScript</c>.</param>
    /// <returns>The tree, read lazily; <see langword="null"/> when the entry is absent or is not a dictionary (with a diagnostic).</returns>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32, and §7.9.6.</remarks>
    public PdfNameTree? GetTree(CosName key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Dictionary.TryGetValue(key, out CosObject? root) ? _document.GetNameTree(root) : null;
    }
}
