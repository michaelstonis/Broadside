using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Broadside.Objects;

/// <summary>A dictionary object: a mutable table from names to objects, written <c>&lt;&lt;…&gt;&gt;</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.7. Keys are <see cref="CosName"/> objects. An entry whose value is null is the same as an absent entry, so a
/// dictionary never stores <see cref="CosNull"/>: setting an entry to <see cref="CosNull.Instance"/> removes it, adding one does
/// nothing, and a parser drops null-valued entries. Entries keep the order they were added in, which is the order they are
/// written in; the order carries no meaning and <see cref="CosObject.DeepEquals"/> ignores it.
/// </para>
/// <para>Any change through the dictionary API marks the dictionary dirty.</para>
/// </remarks>
public sealed class CosDictionary : CosObject, IDictionary<CosName, CosObject>, IReadOnlyDictionary<CosName, CosObject>
{
    private readonly OrderedDictionary<CosName, CosObject> _entries;
    private bool _changed;

    /// <summary>Initializes a new, empty instance of the <see cref="CosDictionary"/> class.</summary>
    public CosDictionary() => _entries = [];

    private CosDictionary(OrderedDictionary<CosName, CosObject> entries) => _entries = entries;

    /// <inheritdoc/>
    public override bool IsDirty
    {
        get
        {
            if (_changed)
            {
                return true;
            }

            foreach (KeyValuePair<CosName, CosObject> entry in _entries)
            {
                if (entry.Value.IsDirty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a number that changes every time an entry is added, replaced or removed through the public API, and never by reading
    /// or loading: caches of state derived from the dictionary record it and rebuild when it differs. Changes to the values
    /// themselves are not counted; a cache records each container it depends on.
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the keys, in entry order.</summary>
    public ICollection<CosName> Keys => _entries.Keys;

    /// <summary>Gets the values, in entry order.</summary>
    public ICollection<CosObject> Values => _entries.Values;

    /// <inheritdoc/>
    IEnumerable<CosName> IReadOnlyDictionary<CosName, CosObject>.Keys => _entries.Keys;

    /// <inheritdoc/>
    IEnumerable<CosObject> IReadOnlyDictionary<CosName, CosObject>.Values => _entries.Values;

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<CosName, CosObject>>.IsReadOnly => false;

    /// <summary>
    /// Gets or sets the value for <paramref name="key"/>. Setting <see cref="CosNull.Instance"/> removes the entry. Setting marks the
    /// dictionary dirty.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <exception cref="KeyNotFoundException">On get, the dictionary has no entry for <paramref name="key"/>.</exception>
    [SuppressMessage("Design", "CA1043:Use integral or string argument for indexers", Justification = "Dictionary keys are names (ISO 32000-2 §7.3.7); this is the IDictionary indexer.")]
    public CosObject this[CosName key]
    {
        get => _entries[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            if (value is CosNull)
            {
                Remove(key);
                return;
            }

            _entries[key] = value;
            _changed = true;
            Version++;
        }
    }

    /// <summary>Adds an entry and marks the dictionary dirty. Adding <see cref="CosNull.Instance"/> adds nothing.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The dictionary already has an entry for <paramref name="key"/>.</exception>
    public void Add(CosName key, CosObject value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (value is CosNull)
        {
            if (_entries.ContainsKey(key))
            {
                throw new ArgumentException("The dictionary already has an entry for this key.", nameof(key));
            }

            return;
        }

        _entries.Add(key, value);
        _changed = true;
        Version++;
    }

    /// <summary>Removes the entry for <paramref name="key"/>; marks the dictionary dirty when one was removed.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public bool Remove(CosName key)
    {
        bool removed = _entries.Remove(key);
        _changed |= removed;
        Version += removed ? 1 : 0;
        return removed;
    }

    /// <summary>Removes every entry; marks the dictionary dirty when it was not already empty.</summary>
    public void Clear()
    {
        _changed |= _entries.Count > 0;
        Version += _entries.Count > 0 ? 1 : 0;
        _entries.Clear();
    }

    /// <inheritdoc/>
    public bool ContainsKey(CosName key) => _entries.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(CosName key, [MaybeNullWhen(false)] out CosObject value) => _entries.TryGetValue(key, out value);

    /// <summary>Returns the entries, in entry order.</summary>
    /// <returns>An enumerator over the entries.</returns>
    public IEnumerator<KeyValuePair<CosName, CosObject>> GetEnumerator() => _entries.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    void ICollection<KeyValuePair<CosName, CosObject>>.Add(KeyValuePair<CosName, CosObject> item) => Add(item.Key, item.Value);

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<CosName, CosObject>>.Contains(KeyValuePair<CosName, CosObject> item) =>
        ((ICollection<KeyValuePair<CosName, CosObject>>)_entries).Contains(item);

    /// <inheritdoc/>
    void ICollection<KeyValuePair<CosName, CosObject>>.CopyTo(KeyValuePair<CosName, CosObject>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<CosName, CosObject>>)_entries).CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<CosName, CosObject>>.Remove(KeyValuePair<CosName, CosObject> item)
    {
        bool removed = ((ICollection<KeyValuePair<CosName, CosObject>>)_entries).Remove(item);
        _changed |= removed;
        Version += removed ? 1 : 0;
        return removed;
    }

    /// <summary>Replaces the value at <paramref name="index"/> as part of loading (decryption), without marking the dictionary dirty.</summary>
    /// <param name="index">The entry's position.</param>
    /// <param name="value">The loaded value; not <see cref="CosNull"/>.</param>
    internal void ReplaceLoaded(int index, CosObject value) => _entries.SetAt(index, value);

    /// <summary>Gets the entry at <paramref name="index"/> in insertion order.</summary>
    /// <param name="index">The entry's position.</param>
    /// <returns>The entry.</returns>
    internal KeyValuePair<CosName, CosObject> GetAt(int index) => _entries.GetAt(index);

    /// <summary>Wraps entries the caller gives up ownership of, without copying them and without marking the dictionary dirty.</summary>
    internal static CosDictionary FromOwnedEntries(OrderedDictionary<CosName, CosObject> entries) => new(entries);
}
