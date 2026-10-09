using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Broadside.Objects;

/// <summary>An array object: a mutable, ordered, heterogeneous list of objects, written <c>[…]</c>.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.6. Elements are never C# <see langword="null"/>; the PDF null is <see cref="CosNull.Instance"/>, which an array
/// keeps (unlike a dictionary, where null means absent). Any change through the list API marks the array dirty.
/// </remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "The name is the ISO 32000-2 term (§7.3.6); the Cos prefix is the convention for the COS layer.")]
public sealed class CosArray : CosObject, IList<CosObject>, IReadOnlyList<CosObject>
{
    private readonly List<CosObject> _items;
    private bool _changed;

    /// <summary>Initializes a new, empty instance of the <see cref="CosArray"/> class.</summary>
    public CosArray() => _items = [];

    /// <summary>Initializes a new instance of the <see cref="CosArray"/> class holding <paramref name="items"/>.</summary>
    /// <param name="items">The elements, in order. None may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">An element is <see langword="null"/>.</exception>
    public CosArray(IEnumerable<CosObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        if (_items.Contains(null!))
        {
            throw new ArgumentException("An array element cannot be null; use CosNull.Instance.", nameof(items));
        }
    }

    private CosArray(List<CosObject> items, bool owned) => _items = owned ? items : [.. items];

    /// <inheritdoc/>
    public override bool IsDirty
    {
        get
        {
            if (_changed)
            {
                return true;
            }

            foreach (CosObject item in _items)
            {
                if (item.IsDirty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a number that changes on every mutation through the public API: caches of state derived from this array (the
    /// font model's encoding and width tables) compare it to know they are stale. Loading (<c>ReplaceLoaded</c>) does not change it.
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _items.Count;

    /// <inheritdoc/>
    bool ICollection<CosObject>.IsReadOnly => false;

    /// <summary>Gets or sets the element at <paramref name="index"/>. Setting marks the array dirty.</summary>
    /// <param name="index">The zero-based index.</param>
    public CosObject this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
            Changed();
        }
    }

    /// <summary>Appends an element and marks the array dirty.</summary>
    /// <param name="item">The element.</param>
    public void Add(CosObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        Changed();
    }

    /// <summary>Inserts an element and marks the array dirty.</summary>
    /// <param name="index">The zero-based index to insert at.</param>
    /// <param name="item">The element.</param>
    public void Insert(int index, CosObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        Changed();
    }

    /// <summary>Removes the element at <paramref name="index"/> and marks the array dirty.</summary>
    /// <param name="index">The zero-based index.</param>
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        Changed();
    }

    /// <summary>Removes the first element equal to <paramref name="item"/>; marks the array dirty when one was removed.</summary>
    /// <param name="item">The element to remove, compared with <see cref="object.Equals(object)"/>.</param>
    /// <returns><see langword="true"/> when an element was removed.</returns>
    public bool Remove(CosObject item)
    {
        bool removed = _items.Remove(item);
        if (removed)
        {
            Changed();
        }

        return removed;
    }

    /// <summary>Removes every element; marks the array dirty when it was not already empty.</summary>
    public void Clear()
    {
        if (_items.Count > 0)
        {
            Changed();
        }

        _items.Clear();
    }

    /// <inheritdoc/>
    public int IndexOf(CosObject item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public bool Contains(CosObject item) => _items.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(CosObject[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<CosObject> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Replaces the element at <paramref name="index"/> as part of loading (decryption), without marking the array dirty.</summary>
    /// <param name="index">The element's position.</param>
    /// <param name="value">The loaded value.</param>
    internal void ReplaceLoaded(int index, CosObject value) => _items[index] = value;

    /// <summary>Wraps a list the caller gives up ownership of, without copying it and without marking the array dirty.</summary>
    internal static CosArray FromOwnedList(List<CosObject> items) => new(items, owned: true);

    /// <summary>Marks the array dirty and moves <see cref="Version"/> on.</summary>
    private void Changed()
    {
        _changed = true;
        Version++;
    }
}
