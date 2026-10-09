using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Broadside.Objects;

namespace Broadside;

/// <summary>A name tree: a sorted map from string keys to objects, spread over a tree of node dictionaries. A read-only live view.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.9.6, Table 36 (PDF 1.2). Keys are strings compared byte by byte (Annex J.3.3): <c>(a)</c> and the UTF-16BE key
/// <c>&lt;FEFF0061&gt;</c> are different keys even though both read as "a". The tree is read lazily: a lookup loads one node per
/// level, guided by each node's <c>Limits</c>; enumeration walks the whole tree in order. Values come back resolved one level (a
/// value stored as an indirect reference returns the object it refers to).
/// </para>
/// <para>
/// Damaged trees are read leniently. A node without usable <c>Limits</c>, a key outside its node's <c>Limits</c>, keys out of order,
/// a key that appears twice, a node reached twice (a cycle), a tree deeper than 64 levels, an odd-length <c>Names</c> array, and a key
/// that is not a string are each reported once per node as a diagnostic; lookups then fall back to an index of every key, built once
/// by walking the whole tree and kept until a node of the tree changes. When a key appears twice, the first occurrence in tree order
/// is the one both lookups and enumeration return. Diagnostics are reported when the damage is first read, so in strict mode the
/// lookup or enumeration that meets it throws a <see cref="Diagnostics.DiagnosticException"/>, not <see cref="PdfDocument.Open(string)"/>.
/// </para>
/// <para>Safe for concurrent reads while nobody changes the document.</para>
/// </remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "The name is the ISO 32000-2 term (§7.9.6).")]
public sealed class PdfNameTree : IEnumerable<KeyValuePair<CosString, CosObject>>
{
    private readonly NameTreeReader _reader;

    internal PdfNameTree(NameTreeReader reader) => _reader = reader;

    /// <summary>Gets the root node dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.9.6, Table 36.</remarks>
    public CosDictionary Root => _reader.Root;

    /// <summary>Gets the indirect reference to the root node, or <see langword="null"/> when the root is a direct dictionary.</summary>
    public CosReference? RootReference => _reader.RootReference;

    /// <summary>Looks up the key with the same bytes as <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    /// <remarks>ISO 32000-2 §7.9.6 and Annex J.3.3: keys are compared byte by byte.</remarks>
    public bool TryGetValue(CosString key, [MaybeNullWhen(false)] out CosObject value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _reader.TryGetValue(key, out value);
    }

    /// <summary>Looks up the key whose bytes are <paramref name="key"/>.</summary>
    /// <param name="key">The key's bytes.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    /// <remarks>ISO 32000-2 §7.9.6 and Annex J.3.3.</remarks>
    public bool TryGetValue(ReadOnlySpan<byte> key, [MaybeNullWhen(false)] out CosObject value) => _reader.TryGetValue(key, out value);

    /// <summary>Looks up the key that reads as the text <paramref name="key"/>.</summary>
    /// <param name="key">The key as text.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds a key that reads as <paramref name="key"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.6 and §7.9.2.2: the file may encode a key in PDFDocEncoding or as UTF-16BE (or UTF-8) with a byte order
    /// marker. The text is tried as PDFDocEncoding bytes (when every character has a code), then as UTF-16BE with the marker; when
    /// neither is a key, every key is decoded and compared with the text ordinally, and the first match in tree order is returned.
    /// </remarks>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out CosObject value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (TextStringEncoder.TryEncodePdfDoc(key, out byte[]? encoded) && _reader.TryGetValue(encoded, out value))
        {
            return true;
        }

        byte[] utf16 = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(key)];
        if (_reader.TryGetValue(utf16, out value))
        {
            return true;
        }

        foreach ((CosString candidate, CosObject found) in _reader.Enumerate())
        {
            if (string.Equals(candidate.DecodeText(), key, StringComparison.Ordinal))
            {
                value = found;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Determines whether the tree holds the key with the same bytes as <paramref name="key"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    /// <remarks>ISO 32000-2 §7.9.6.</remarks>
    public bool ContainsKey(CosString key) => TryGetValue(key, out _);

    /// <summary>Returns the keys and values in tree order (ascending byte order in a well-formed tree), each key once.</summary>
    /// <returns>An enumerator that walks the tree; each enumeration walks it again.</returns>
    /// <remarks>ISO 32000-2 §7.9.6.</remarks>
    public IEnumerator<KeyValuePair<CosString, CosObject>> GetEnumerator()
    {
        foreach ((CosString key, CosObject value) in _reader.Enumerate())
        {
            yield return new KeyValuePair<CosString, CosObject>(key, value);
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
