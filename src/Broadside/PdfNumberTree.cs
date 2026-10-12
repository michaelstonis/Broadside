using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside;

/// <summary>A number tree: a sorted map from integer keys to objects, spread over a tree of node dictionaries. A read-only live view.</summary>
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
/// <para>Safe for concurrent reads while nobody changes the document.</para>
/// </remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "The name is the ISO 32000-2 term (§7.9.7).")]
public sealed class PdfNumberTree : IEnumerable<KeyValuePair<int, CosObject>>
{
    private readonly NumberTreeReader _reader;

    internal PdfNumberTree(NumberTreeReader reader) => _reader = reader;

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

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
