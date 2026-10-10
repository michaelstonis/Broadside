using Broadside.Objects;

namespace Broadside;

/// <summary>A folder of a portable collection. Its entries are read live; its place in the tree is a snapshot of the walk that found it.</summary>
/// <remarks>
/// ISO 32000-2 §12.3.5 (PDF 2.0), Table 159, "Folders". The tree is reached from the collection's <c>Folders</c> root through
/// <c>Child</c> (first child) and <c>Next</c> (next sibling); <see cref="Parent"/> and <see cref="Children"/> reflect that walk,
/// not the <c>Parent</c> entries. A file belongs to a folder through the <c>&lt;id&gt;</c> prefix of its EmbeddedFiles key.
/// </remarks>
public sealed class PdfCollectionFolder
{
    private readonly PdfDocument _document;
    private readonly List<PdfCollectionFolder> _children = [];

    internal PdfCollectionFolder(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfCollectionFolder? parent)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
        Parent = parent;
    }

    /// <summary>Gets the folder dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the folder, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the folder's ID (<c>ID</c>, a non-negative integer unique in the collection), or -1 when missing or invalid.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the ID is missing or invalid.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.5, Table 159 (required). A missing, negative or non-integer ID reads as -1 and a real holding a whole number
    /// as that number, each with a <c>CollectionFolderInvalid</c> diagnostic.
    /// </remarks>
    public int Id
    {
        get
        {
            switch (ViewReading.Int32(_document, Dictionary, FileAndLayerNames.ID, Issue))
            {
                case >= 0 and var id:
                    return id;
                case { }:
                    Issue.Report("The collection folder's ID shall be a non-negative integer; it reads as -1.");
                    return -1;
                case null when ViewReading.Get(_document, Dictionary, FileAndLayerNames.ID) is null:
                    Issue.Report("The collection folder has no ID, which Table 159 requires; it reads as -1.");
                    return -1;
                default:
                    return -1;
            }
        }
    }

    /// <summary>Gets the folder's name (<c>Name</c>); empty when missing.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the name is missing or not a text string.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.5, Table 159 (required; file-name rules apply). A missing name reads as empty and a name or number as its
    /// text, each with a <c>CollectionFolderInvalid</c> diagnostic.
    /// </remarks>
    public string Name
    {
        get
        {
            if (ViewReading.Text(_document, Dictionary, FileAndLayerNames.Name, Issue) is { } name)
            {
                return name;
            }

            if (ViewReading.Get(_document, Dictionary, FileAndLayerNames.Name) is null)
            {
                Issue.Report("The collection folder has no Name, which Table 159 requires; it reads as empty.");
            }

            return string.Empty;
        }
    }

    /// <summary>Gets the folder's description (<c>Desc</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159.</remarks>
    public string? Description => ViewReading.Text(_document, Dictionary, FileAndLayerNames.Desc);

    /// <summary>Gets the folder this one was found under, or <see langword="null"/> for the root.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159 (<c>Parent</c>).</remarks>
    public PdfCollectionFolder? Parent { get; }

    /// <summary>Gets the subfolders, in <c>Child</c>/<c>Next</c> order.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159 (<c>Child</c>, <c>Next</c>).</remarks>
    public IReadOnlyList<PdfCollectionFolder> Children => _children;

    /// <summary>Gets the folder's collection item (<c>CI</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159, and §7.11.6.</remarks>
    public PdfCollectionItem? CollectionItem =>
        ViewReading.Get(_document, Dictionary, FileAndLayerNames.CI) is CosDictionary item ? new PdfCollectionItem(_document, item) : null;

    /// <summary>Gets the creation date (<c>CreationDate</c>), or <see langword="null"/> when absent or unreadable.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159, and §7.9.4.</remarks>
    public PdfDate? CreationDate => ViewReading.Date(_document, Dictionary, FileAndLayerNames.CreationDate, Reference);

    /// <summary>Gets the modification date (<c>ModDate</c>), or <see langword="null"/> when absent or unreadable.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159, and §7.9.4.</remarks>
    public PdfDate? ModificationDate => ViewReading.Date(_document, Dictionary, FileAndLayerNames.ModDate, Reference);

    /// <summary>Gets the folder's thumbnail image (<c>Thumb</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159, and §12.3.4.</remarks>
    public CosStream? Thumbnail => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Thumb) as CosStream;

    /// <summary>Gets the ranges of IDs free for new folders (<c>Free</c>, root folder only).</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159: an array of pairs of integers.</remarks>
    public IReadOnlyList<PdfCollectionIdRange> FreeIds
    {
        get
        {
            if (ViewReading.Get(_document, Dictionary, FileAndLayerNames.Free) is not CosArray array)
            {
                return [];
            }

            var ranges = new List<PdfCollectionIdRange>(array.Count / 2);
            for (int index = 0; index + 1 < array.Count; index += 2)
            {
                if (_document.Resolve(array[index]) is CosInteger { Value: >= 0 and <= int.MaxValue } low
                    && _document.Resolve(array[index + 1]) is CosInteger { Value: >= 0 and <= int.MaxValue } high)
                {
                    ranges.Add(new PdfCollectionIdRange((int)low.Value, (int)high.Value));
                }
            }

            return ranges;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    internal void AddChild(PdfCollectionFolder child) => _children.Add(child);

    private EntryReport Issue => new(_document, Parsing.DiagnosticCodes.CollectionFolderInvalid, Reference, "The collection folder");
}
