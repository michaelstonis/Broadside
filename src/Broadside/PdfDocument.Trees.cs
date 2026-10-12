using Broadside.Objects;

namespace Broadside;

/// <content>Writing name trees and number trees (issue #146): ISO 32000-2 §7.9.6 and §7.9.7.</content>
public sealed partial class PdfDocument
{
    /// <summary>Builds a new name tree holding <paramref name="entries"/>, balanced, with every node a new indirect object.</summary>
    /// <param name="entries">The keys and values, in any order; each key once. Values are stored as given.</param>
    /// <returns>
    /// A view of the new tree. Its <see cref="PdfNameTree.RootReference"/> is what an entry of the name dictionary (<c>Dests</c>,
    /// <c>EmbeddedFiles</c>, …) or any other owner refers to; the tree is not reachable from the document until something does.
    /// </returns>
    /// <exception cref="ArgumentException">A key appears more than once.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.9.6, Table 36 (PDF 1.2). Keys are sorted byte by byte (Annex J.3.3). Up to 64 keys sit in the root's
    /// <c>Names</c> array; more are spread evenly over leaves of at most 64 pairs under intermediate nodes of at most 64 kids, every
    /// leaf at the same depth, every node below the root with <c>Limits</c>.
    /// </remarks>
    public PdfNameTree CreateNameTree(IEnumerable<KeyValuePair<CosString, CosObject>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CosReference root = AddObject(new CosDictionary());
        NameTreeReader reader = GetNameTreeReader(root)!;
        reader.Build(entries, AddObject);
        return new PdfNameTree(reader, this);
    }

    /// <summary>Builds a new number tree holding <paramref name="entries"/>, balanced, with every node a new indirect object.</summary>
    /// <param name="entries">The keys and values, in any order; each key once. Values are stored as given.</param>
    /// <returns>
    /// A view of the new tree. Its <see cref="PdfNumberTree.RootReference"/> is what the owner (the catalog's <c>PageLabels</c>, a
    /// structure tree root's <c>ParentTree</c>, …) refers to.
    /// </returns>
    /// <exception cref="ArgumentException">A key appears more than once.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.9.7, Table 37 (PDF 1.3). Keys are sorted in ascending numerical order and spread over the tree as
    /// <see cref="CreateNameTree"/> spreads name-tree keys, in <c>Nums</c> arrays.
    /// </remarks>
    public PdfNumberTree CreateNumberTree(IEnumerable<KeyValuePair<int, CosObject>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CosReference root = AddObject(new CosDictionary());
        NumberTreeReader reader = GetNumberTreeReader(root)!;
        reader.Build(entries, AddObject);
        return new PdfNumberTree(reader, this);
    }
}
