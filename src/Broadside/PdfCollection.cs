using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>A portable collection (portfolio): how a user interface presents the document's embedded files. A live view.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.5 (PDF 1.7), Tables 153-159, reached through the catalog's <c>Collection</c> entry (§7.7.2, Table 29). The
/// files are the entries of the EmbeddedFiles name tree (<see cref="PdfDocument.EmbeddedFiles"/>); each file's values for the schema
/// fields are in its collection item (<see cref="PdfFileSpecification.CollectionItem"/>).
/// </para>
/// <para>
/// Folders (PDF 2.0) are walked from <c>Folders</c> through <c>Child</c> and <c>Next</c> each time <see cref="RootFolder"/> is read;
/// a folder reached twice (a cycle) is skipped with <c>CollectionFolderCycle</c>, and of folders sharing an ID the first one found
/// keeps it (<c>CollectionFolderIdDuplicate</c>).
/// </para>
/// </remarks>
public sealed class PdfCollection
{
    private readonly PdfDocument _document;

    internal PdfCollection(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the collection dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 153.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the collection dictionary, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the initial presentation (<c>View</c>, default details).</summary>
    /// <remarks>
    /// ISO 32000-2 §12.3.5, Table 153. The navigator view (<c>C</c>, PDF 2.0) requires a <c>Navigator</c> entry; without one it
    /// reads as details with a <c>CollectionInvalid</c> diagnostic.
    /// </remarks>
    public PdfCollectionView View
    {
        get
        {
            switch (ViewReading.Name(_document, Dictionary, FileAndLayerNames.View)?.Value)
            {
                case "T":
                    return PdfCollectionView.Tile;
                case "H":
                    return PdfCollectionView.Hidden;
                case "C" when Navigator is not null:
                    return PdfCollectionView.Navigator;
                case "C":
                    ViewReading.Warn(_document, DiagnosticCodes.CollectionInvalid, "The collection's View is C (navigator) but it has no Navigator; details view is used.", Reference);
                    return PdfCollectionView.Details;
                default:
                    return PdfCollectionView.Details;
            }
        }
    }

    /// <summary>Gets the EmbeddedFiles key of the document shown first (<c>D</c>), or <see langword="null"/> for the collection itself.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 153. A key that matches no file means the first file.</remarks>
    public string? InitialDocument => ViewReading.Text(_document, Dictionary, FileAndLayerNames.D);

    /// <summary>Gets the navigator dictionary (<c>Navigator</c>, PDF 2.0), as is, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 153. Navigators are parse-and-preserve.</remarks>
    public CosDictionary? Navigator => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Navigator) as CosDictionary;

    /// <summary>Gets the schema fields (<c>Schema</c>), in the order they are stored; see <see cref="PdfCollectionField.Order"/> for the column order.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Tables 154 and 155. Entries that are not dictionaries are skipped.</remarks>
    public IReadOnlyList<PdfCollectionField> Schema => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Schema) is CosDictionary schema
        ? [.. schema.Where(entry => !entry.Key.Equals(KnownNames.Type) && _document.Resolve(entry.Value) is CosDictionary)
            .Select(entry => new PdfCollectionField(_document, entry.Key, (CosDictionary)_document.Resolve(entry.Value)))]
        : [];

    /// <summary>Gets the sort order (<c>Sort</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 156.</remarks>
    public PdfCollectionSort? Sort => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Sort) is CosDictionary sort ? new PdfCollectionSort(_document, sort) : null;

    /// <summary>Gets the user-interface colours (<c>Colors</c>, PDF 2.0), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 157.</remarks>
    public PdfCollectionColors? Colors => ViewReading.Get(_document, Dictionary, FileAndLayerNames.Colors) is CosDictionary colors ? new PdfCollectionColors(_document, colors) : null;

    /// <summary>Gets the split of the user interface (<c>Split</c>, PDF 2.0), with the defaults the view implies.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 158.</remarks>
    public PdfCollectionSplit Split
    {
        get
        {
            PdfCollectionView view = View;
            PdfCollectionSplitDirection fallback = view switch
            {
                PdfCollectionView.Details => PdfCollectionSplitDirection.Horizontal,
                PdfCollectionView.Tile => PdfCollectionSplitDirection.Vertical,
                _ => PdfCollectionSplitDirection.None,
            };
            if (ViewReading.Get(_document, Dictionary, FileAndLayerNames.Split) is not CosDictionary split)
            {
                return new PdfCollectionSplit(fallback, null);
            }

            PdfCollectionSplitDirection direction = ViewReading.Name(_document, split, FileAndLayerNames.Direction)?.Value switch
            {
                "H" => PdfCollectionSplitDirection.Horizontal,
                "V" => PdfCollectionSplitDirection.Vertical,
                "N" => PdfCollectionSplitDirection.None,
                _ => fallback,
            };
            return new PdfCollectionSplit(direction, ViewReading.Number(_document, split, FileAndLayerNames.Position));
        }
    }

    /// <summary>Gets the root folder (<c>Folders</c>, PDF 2.0) with its subtree, or <see langword="null"/> when the collection has no folders.</summary>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159.</remarks>
    public PdfCollectionFolder? RootFolder => BuildFolders(out _);

    /// <summary>Returns the folder with ID <paramref name="id"/>, or <see langword="null"/>.</summary>
    /// <param name="id">The folder ID.</param>
    /// <returns>The first folder found with that ID.</returns>
    /// <remarks>ISO 32000-2 §12.3.5, Table 159.</remarks>
    public PdfCollectionFolder? FindFolder(int id)
    {
        BuildFolders(out Dictionary<int, PdfCollectionFolder> byId);
        return byId.GetValueOrDefault(id);
    }

    /// <summary>Returns the folder an embedded file belongs to: the folder its key's <c>&lt;id&gt;</c> prefix names, else the root folder.</summary>
    /// <param name="file">An entry of <see cref="PdfDocument.EmbeddedFiles"/>.</param>
    /// <returns>The folder; <see langword="null"/> when the collection has no folders.</returns>
    /// <remarks>ISO 32000-2 §12.3.5, "Folders": a key without a valid folder prefix, or naming no folder, places the file in the root.</remarks>
    public PdfCollectionFolder? GetFolder(PdfEmbeddedFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        PdfCollectionFolder? root = BuildFolders(out Dictionary<int, PdfCollectionFolder> byId);
        return file.FolderId is { } id && byId.TryGetValue(id, out PdfCollectionFolder? folder) ? folder : root;
    }

    private PdfCollectionFolder? BuildFolders(out Dictionary<int, PdfCollectionFolder> byId)
    {
        byId = [];
        if (!Dictionary.TryGetValue(FileAndLayerNames.Folders, out CosObject? entry) || _document.Resolve(entry) is not CosDictionary rootDictionary)
        {
            return null;
        }

        var root = new PdfCollectionFolder(_document, rootDictionary, entry as CosReference, null);
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { rootDictionary };
        var pending = new Stack<PdfCollectionFolder>();
        pending.Push(root);
        Register(root, byId);
        while (pending.TryPop(out PdfCollectionFolder? folder))
        {
            folder.Dictionary.TryGetValue(FileAndLayerNames.Child, out CosObject? link);
            var siblings = new List<PdfCollectionFolder>();
            while (link is not null && _document.Resolve(link) is CosDictionary child)
            {
                if (!visited.Add(child))
                {
                    ViewReading.Warn(_document, DiagnosticCodes.CollectionFolderCycle, "A collection folder's Child or Next leads back to a folder already reached; the link is ignored.", folder.Reference ?? Reference);
                    break;
                }

                var view = new PdfCollectionFolder(_document, child, link as CosReference, folder);
                folder.AddChild(view);
                Register(view, byId);
                siblings.Add(view);
                child.TryGetValue(FileAndLayerNames.Next, out link);
            }

            for (int index = siblings.Count - 1; index >= 0; index--)
            {
                pending.Push(siblings[index]);
            }
        }

        return root;

        void Register(PdfCollectionFolder folder, Dictionary<int, PdfCollectionFolder> index)
        {
            int id = folder.Id;
            if (id >= 0 && !index.TryAdd(id, folder))
            {
                ViewReading.Warn(_document, DiagnosticCodes.CollectionFolderIdDuplicate, $"Two collection folders have the ID {id}; the first one found keeps it.", folder.Reference ?? Reference);
            }
        }
    }
}
