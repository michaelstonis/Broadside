using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside;

/// <summary>A number tree: a sorted map from integer keys to objects, spread over a tree of node dictionaries. A live view.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.9.7, Table 37 (PDF 1.3). Like <see cref="PdfNameTree"/> with integer keys in ascending numerical order, held in
/// <c>Nums</c> arrays. Keys are 32-bit integers. Lookups are guided by each node's <c>Limits</c>; <see cref="TryGetFloor"/> and
/// enumeration read the whole tree. Values come back resolved one level.
/// </para>
/// <para>
/// Damage is read leniently, as for <see cref="PdfNameTree"/>: each deviation is reported once per node when it is first read (in
/// strict mode the lookup or enumeration that meets it throws), lookups fall back to an index of every key built once, and a key that
/// appears twice resolves to its first occurrence in tree order. A key written as a real with an integral value is used as that
/// integer, with a diagnostic; any other non-integer key is skipped.
/// </para>
/// <para>
/// Editable in place (issue #146): <see cref="Set"/> inserts or replaces a key and <see cref="Remove"/> removes one, changing only the
/// nodes on the way from the root to the key's leaf and keeping every <c>Limits</c> entry correct; see
/// <see cref="PdfDocument.CreateNumberTree"/> for building a new tree. A damaged tree is rebuilt balanced on its first edit.
/// </para>
/// <para>Safe for concurrent reads while nobody changes the document; edits are single-threaded, like every change to a document.</para>
/// </remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "The name is the ISO 32000-2 term (§7.9.7).")]
public sealed class PdfNumberTree : IEnumerable<KeyValuePair<int, CosObject>>
{
    private readonly NumberTreeReader _reader;
    private readonly PdfDocument _document;

    internal PdfNumberTree(NumberTreeReader reader, PdfDocument document)
    {
        _reader = reader;
        _document = document;
    }

    /// <summary>Gets the root node dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.9.7, Table 37.</remarks>
    public CosDictionary Root => _reader.Root;

    /// <summary>Gets the indirect reference to the root node, or <see langword="null"/> when the root is a direct dictionary.</summary>
    public CosReference? RootReference => _reader.RootReference;

    /// <summary>Looks up <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    /// <remarks>ISO 32000-2 §7.9.7.</remarks>
    public bool TryGetValue(int key, [MaybeNullWhen(false)] out CosObject value) => _reader.TryGetValue(key, out value);

    /// <summary>Finds the greatest key that is not greater than <paramref name="key"/>, and its value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="floorKey">The greatest key in the tree not greater than <paramref name="key"/>.</param>
    /// <param name="value">Its value, resolved one level.</param>
    /// <returns><see langword="true"/> when the tree holds such a key.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.7. This is how a range keyed by its first number is found, as page labels are (§12.4.2). Reads the whole tree
    /// once, then answers from its index until the tree changes.
    /// </remarks>
    public bool TryGetFloor(int key, out int floorKey, [MaybeNullWhen(false)] out CosObject value) => _reader.TryGetFloor(key, out floorKey, out value);

    /// <summary>Determines whether the tree holds <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    /// <remarks>ISO 32000-2 §7.9.7.</remarks>
    public bool ContainsKey(int key) => TryGetValue(key, out _);

    /// <summary>Returns the keys and values in tree order (ascending in a well-formed tree), each key once.</summary>
    /// <returns>An enumerator that walks the tree; each enumeration walks it again.</returns>
    /// <remarks>ISO 32000-2 §7.9.7.</remarks>
    public IEnumerator<KeyValuePair<int, CosObject>> GetEnumerator()
    {
        foreach ((int key, CosObject value) in _reader.Enumerate())
        {
            yield return new KeyValuePair<int, CosObject>(key, value);
        }
    }

    /// <summary>Inserts <paramref name="key"/> with <paramref name="value"/>, or replaces the value of the key the tree already holds.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, stored as given: pass an indirect reference to store one.</param>
    /// <remarks>
    /// ISO 32000-2 §7.9.7. The key goes into the leaf whose range holds it (or the nearest one, whose <c>Limits</c> then widen); a
    /// node grown past 64 entries splits in two, the root into two new kids, so every leaf stays at the same depth. Only the nodes on
    /// the path from the root change. A tree the reader finds damaged is first rebuilt balanced in place from what it reads.
    /// </remarks>
    public void Set(int key, CosObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _reader.Set(key, value, _document.AddObject);
    }

    /// <summary>Removes <paramref name="key"/> and its value.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the tree held the key.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.7. A node left empty is removed from its parent (an empty root keeps an empty <c>Nums</c> array), and the
    /// <c>Limits</c> of every node on the path are updated. A damaged tree that holds the key is first rebuilt balanced in place.
    /// </remarks>
    public bool Remove(int key)
    {
        return _reader.Remove(key, _document.AddObject);
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
