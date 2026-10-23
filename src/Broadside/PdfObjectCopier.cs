using System.Runtime.InteropServices;
using Broadside.Annotations;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>
/// One copy session: copies object graphs of a source document into a target document, with every indirect reference remapped
/// to the target's copy of the object it names.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.10. An indirect object of the source becomes a new indirect object of the target, numbered as
/// <see cref="PdfDocument.AddObject"/> numbers it, the first time the session reaches it; every later reference to it, in the same
/// call or a later one, is the same target reference. So objects that several copied graphs share (a font, an image, a form XObject
/// two pages paint) are copied once per session. Cycles are followed once, and the graph is walked without recursion, so neither a
/// cyclic nor a deeply nested graph exhausts the stack. Direct objects are copied wherever they appear; immutable values are shared.
/// </para>
/// <para>
/// <b>Pages.</b> The page tree is never copied: the <c>Parent</c> entry of a page or page tree node is left out, and a reference
/// to a page tree node (§7.7.3.2) copies as null. A page is copied by <see cref="CopyPage"/> (or as the object <see cref="Copy"/> is
/// given), with the attributes it inherits (<c>Resources</c>, <c>MediaBox</c>, <c>CropBox</c>, <c>Rotate</c>, §7.7.3.4) made its
/// own, ready for the target's page tree. An <c>Annots</c> the page reads from a page tree node above it is made its own too: it
/// is not inheritable (Table 31), but it is read as viewers read it (§7.7.3.3), so the copy shows the same annotations; the source
/// records <c>AnnotsInherited</c> for it before the target changes. A reference to any other page that a copied graph reaches, such as an annotation's
/// destination or a structure element's page, is not followed: it names the page's copy once the session copies that page, before
/// or after, and is null until then (an array keeps a null element in its place; a dictionary has no entry).
/// </para>
/// <para>
/// <b>Streams</b> are copied encoded: their data as stored, the filters their dictionaries name and a direct <c>Length</c> written
/// on save (§7.3.8). Data is read from the source when the stream is copied, so the source may be disposed once copying is done.
/// The objects of an encrypted source copy decrypted (§7.6.2): strings and stream data as the document reads them, a stream's
/// <c>Crypt</c> filter left out of its <c>Filter</c> and <c>DecodeParms</c> (§7.4.10), and the source's encryption dictionary
/// copied as null. The target encrypts on save as its own options say. Everything else is copied unchanged, including what the
/// library only preserves (JavaScript, 3D and RichMedia annotations and their assets, XFA).
/// </para>
/// <para>
/// <b>Versions.</b> The features of the version table that the copied objects use (associated files, a file specification's
/// <c>UF</c> or <c>Desc</c>, embedded file streams, <c>FlateDecode</c>, structure elements and namespaces, outline item styles,
/// <c>FitB</c> destinations) are recorded in the target as its own writing APIs record theirs, so its next save raises its version to
/// the highest of them, or refuses in strict mode before writing (ISO 32000-2 §7.5.2). A copy between documents of the
/// same version raises nothing the source did not already need.
/// </para>
/// <para>
/// A session mutates the target: no read of the target may run concurrently with a copy. The source is only read, and may be read
/// concurrently by others.
/// </para>
/// </remarks>
public sealed class PdfObjectCopier
{
    private static readonly CosName[] InheritableEntries = [KnownNames.Resources, KnownNames.MediaBox, KnownNames.CropBox, KnownNames.Rotate];

    private readonly Dictionary<CosReference, CosReference> _copies = [];
    private readonly HashSet<CosReference> _excluded = [];
    private readonly Dictionary<CosReference, List<PendingPage>> _pendingPages = [];
    private readonly Stack<(CosObject Source, CosObject Copy)> _work = new();
    private readonly CosReference? _encryptionDictionary;
    private readonly HashSet<VersionedFeature> _carried = [];
    private readonly Action<VersionedFeature> _carry;

    /// <summary>Initializes a new copy session from <paramref name="source"/> into <paramref name="target"/>.</summary>
    /// <param name="source">The document the objects are copied from.</param>
    /// <param name="target">The document the copies are added to.</param>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public PdfObjectCopier(PdfDocument source, PdfDocument target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
        _encryptionDictionary = source.IsEncrypted && source.Trailer.TryGetValue(KnownNames.Encrypt, out CosObject? encrypt)
            ? encrypt as CosReference
            : null;
        _carry = Carry;
    }

    /// <summary>Gets the document objects are copied from.</summary>
    public PdfDocument Source { get; }

    /// <summary>Gets the document copies are added to.</summary>
    public PdfDocument Target { get; }

    /// <summary>Copies <paramref name="value"/>, and every object it reaches, from <see cref="Source"/> into <see cref="Target"/>.</summary>
    /// <param name="value">An object of the source: a direct object, or a reference to an indirect one.</param>
    /// <returns>
    /// The copy: for a reference, the reference to the target's copy (the same reference every time the session copies it); for a
    /// direct container, a new container to place in the target; an immutable value itself. <see cref="CosNull.Instance"/> for a
    /// reference to nothing, to a page tree node, or to the source's encryption dictionary.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// A stream's data is encrypted with a crypt filter the source was opened without the key to (§7.6.6), so it cannot be copied
    /// decrypted.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §7.3.10. A page (a dictionary whose <c>Type</c> is <c>Page</c>) is copied as <see cref="CopyPage"/> copies one,
    /// inheriting through its <c>Parent</c> chain; every other page the graph reaches is handled as the class remarks say. A mutation
    /// of the target.
    /// </remarks>
    public CosObject Copy(CosObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CosObject resolved = Source.Resolve(value);
        if (resolved is CosDictionary dictionary && IsType(dictionary, KnownNames.Page))
        {
            CosReference? reference = value as CosReference;
            CosObject? annots = InheritedAnnotsThroughParents(dictionary);
            ReportInheritedAnnots(reference, annots);
            return CopyPageObject(reference, dictionary, key => InheritedThroughParents(dictionary, key), annots);
        }

        CosObject copy = Map(value);
        Drain();
        return copy;
    }

    /// <summary>
    /// Copies <paramref name="page"/> into <see cref="Target"/> as a new page object, without a <c>Parent</c>, for the target's page
    /// tree.
    /// </summary>
    /// <param name="page">A page of <see cref="Source"/>.</param>
    /// <returns>The reference to the copied page object; the same reference when the session has copied the page before.</returns>
    /// <exception cref="ArgumentException"><paramref name="page"/> is not a page of <see cref="Source"/>.</exception>
    /// <exception cref="NotSupportedException">A stream's data cannot be copied decrypted (see <see cref="Copy"/>).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// The source is in strict mode and the page reads its <c>Annots</c> from a page tree node (Table 31).
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3.3, §7.7.3.4 and Table 31. Everything the page object holds is copied (contents, resources, annotations and what they
    /// reach) except <c>Parent</c>; inherited attributes become the copy's own entries, and so does an <c>Annots</c> the page reads
    /// from a page tree node (not inheritable, Table 31, but read as viewers read it; recorded as <c>AnnotsInherited</c> in the
    /// source's diagnostics, so in strict mode this throws before the target changes). References to the page from inside the copied
    /// graph (an annotation's <c>P</c>, say) name the copy. Place the reference in the target's page tree and set its <c>Parent</c>
    /// for it to be a page of the target. A mutation of the target.
    /// </remarks>
    public CosReference CopyPage(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(page.Document, Source))
        {
            throw new ArgumentException("The page is not a page of the source document.", nameof(page));
        }

        CosObject? annots = page.FindInheritedAnnots();
        ReportInheritedAnnots(page.Reference, annots);
        CosObject copy = CopyPageObject(page.Reference, page.Dictionary, page.FindInheritedEntry, annots);
        return copy as CosReference ?? Target.AddObject(copy);
    }

    /// <summary>Gets the target's copy of the source object <paramref name="reference"/> names, when this session has copied it.</summary>
    internal bool TryGetCopy(CosReference reference, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CosReference? copy) =>
        _copies.TryGetValue(reference, out copy);

    private CosObject CopyPageObject(CosReference? reference, CosDictionary page, Func<CosName, CosObject?> inherited, CosObject? inheritedAnnots)
    {
        if (reference is not null && _copies.TryGetValue(reference, out CosReference? existing))
        {
            return existing;
        }

        var copy = new CosDictionary();
        CosObject result = copy;
        if (reference is not null)
        {
            CosReference copied = Target.AddObject(copy);
            Register(reference, copied);
            result = copied;
        }

        _work.Push((page, copy));
        Drain();
        foreach (CosName key in InheritableEntries)
        {
            if (!copy.ContainsKey(key) && inherited(key) is { } value && Map(value) is { } mapped and not CosNull)
            {
                copy[key] = mapped;
            }
        }

        if (!copy.ContainsKey(AnnotationNames.Annots) && inheritedAnnots is not null && Map(inheritedAnnots) is { } annots and not CosNull)
        {
            copy[AnnotationNames.Annots] = annots;
        }

        Drain();
        return result;
    }

    /// <summary>Records in the source that a page reads its <c>Annots</c> from a page tree node (Table 31), when it does.</summary>
    private void ReportInheritedAnnots(CosReference? page, CosObject? annots)
    {
        if (annots is not null)
        {
            Source.AnnotationIndex.ReportInherited(page);
        }
    }

    /// <summary>
    /// The <c>Annots</c> entry a page reads from the nearest node of its <c>Parent</c> chain, as stored, when the page has none of its
    /// own; as <see cref="PdfPage.FindInheritedAnnots"/> reads it for a page of the tree.
    /// </summary>
    private CosObject? InheritedAnnotsThroughParents(CosDictionary page)
    {
        if (page.ContainsKey(AnnotationNames.Annots))
        {
            return null;
        }

        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { page };
        for (CosObject node = EntryReader.Get(Source, page, KnownNames.Parent) ?? CosNull.Instance;
            node is CosDictionary ancestor && seen.Add(ancestor);
            node = EntryReader.Get(Source, ancestor, KnownNames.Parent) ?? CosNull.Instance)
        {
            if (ancestor.TryGetValue(AnnotationNames.Annots, out CosObject? annots))
            {
                return annots;
            }
        }

        return null;
    }

    /// <summary>The nearest ancestor's entry as stored, through the <c>Parent</c> chain, when the page lacks a resolved one.</summary>
    private CosObject? InheritedThroughParents(CosDictionary page, CosName key)
    {
        if (EntryReader.Get(Source, page, key) is not null)
        {
            return null;
        }

        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { page };
        for (CosObject node = EntryReader.Get(Source, page, KnownNames.Parent) ?? CosNull.Instance;
            node is CosDictionary ancestor && seen.Add(ancestor);
            node = EntryReader.Get(Source, ancestor, KnownNames.Parent) ?? CosNull.Instance)
        {
            if (EntryReader.Get(Source, ancestor, key) is not null)
            {
                return ancestor[key];
            }
        }

        return null;
    }

    /// <summary>Gets the target references of every indirect object the session has copied or been given (<see cref="Assign"/>).</summary>
    internal IEnumerable<CosReference> Copies => _copies.Values;

    /// <summary>
    /// Makes <paramref name="copy"/>, an object of the target the caller fills, the copy of the source object <paramref name="source"/>:
    /// every reference to it the session reaches names <paramref name="copy"/>, and the session never fills it (issue #161: structure
    /// elements copied element by element).
    /// </summary>
    internal void Assign(CosReference source, CosReference copy) => Register(source, copy);

    /// <summary>Makes every reference to the source object <paramref name="source"/> the session reaches copy as null (an element not kept).</summary>
    internal void Exclude(CosReference source) => _excluded.Add(source);

    /// <summary>Records <paramref name="copy"/> as the copy of <paramref name="source"/> and fills the slots that waited for it.</summary>
    private void Register(CosReference source, CosReference copy)
    {
        _copies[source] = copy;
        if (_pendingPages.Remove(source, out List<PendingPage>? waiting))
        {
            foreach (PendingPage slot in waiting)
            {
                if (slot.Array is { } array)
                {
                    array[slot.Index] = copy;
                }
                else
                {
                    slot.Dictionary![slot.Key!] = copy;
                }
            }
        }
    }

    /// <summary>
    /// The copy of one value: immutable values as they are, a direct container as a new empty one queued to be filled, a reference
    /// as the reference to the target's copy (created and queued the first time). Null when the value copies as nothing.
    /// </summary>
    private CosObject Map(CosObject value)
    {
        switch (value)
        {
            case CosReference reference:
                if (_copies.TryGetValue(reference, out CosReference? copied))
                {
                    return copied;
                }

                if (reference.Equals(_encryptionDictionary) || _excluded.Contains(reference))
                {
                    return CosNull.Instance;
                }

                CosObject resolved = Source.Resolve(reference);
                if (resolved is CosNull || (resolved is CosDictionary node && IsType(node, KnownNames.Pages)))
                {
                    return CosNull.Instance;
                }

                CosObject shell = Shell(resolved);
                CosReference added = Target.AddObject(shell);
                Register(reference, added);
                return added;
            case CosArray or CosDictionary or CosStream:
                return Shell(value);
            default:
                return value;
        }
    }

    /// <summary>A new, empty container of <paramref name="value"/>'s kind, queued to be filled; an immutable value as it is.</summary>
    private CosObject Shell(CosObject value)
    {
        CosObject shell = value switch
        {
            CosArray => new CosArray(),
            CosDictionary => new CosDictionary(),
            CosStream stream => new CosStream(new CosDictionary(), StreamData(stream)),
            _ => value,
        };

        if (!ReferenceEquals(shell, value))
        {
            _work.Push((value, shell));
        }

        return shell;
    }

    /// <summary>The encoded data of a source stream, as the target can hold it after the source is disposed.</summary>
    private ReadOnlyMemory<byte> StreamData(CosStream stream)
    {
        if (Source.Streams.CryptFilter.IsLocked(stream.Dictionary))
        {
            throw new NotSupportedException(
                "A stream's data is encrypted with a crypt filter the source document was opened without the key to; it cannot be copied decrypted.");
        }

        // Data in a managed array (memory sources, decrypted data) stays alive with the copy; data in a mapped file view does not.
        ReadOnlyMemory<byte> data = stream.EncodedData;
        return MemoryMarshal.TryGetArray(data, out _) ? data : data.ToArray();
    }

    /// <summary>Fills queued containers until none is left: the walk that replaces recursion.</summary>
    private void Drain()
    {
        while (_work.TryPop(out (CosObject Source, CosObject Copy) item))
        {
            switch (item.Source)
            {
                case CosArray array:
                    FillArray(array, (CosArray)item.Copy);
                    break;
                case CosStream stream:
                    FillDictionary(stream.Dictionary, ((CosStream)item.Copy).Dictionary, isStream: true);
                    if (!Source.Streams.CryptFilter.IsLocked(stream.Dictionary))
                    {
                        CarriedFeatures.InStreamData(Source, stream.Dictionary, stream.EncodedData.Span, _carry);
                    }

                    break;
                case CosDictionary dictionary:
                    FillDictionary(dictionary, (CosDictionary)item.Copy, isStream: false);
                    break;
            }
        }
    }

    private void FillArray(CosArray source, CosArray copy)
    {
        CarriedFeatures.InArray(source, _carry);
        for (int index = 0; index < source.Count; index++)
        {
            CosObject item = source[index];
            if (IsUncopiedPage(item, out CosReference? page))
            {
                Wait(page, new PendingPage(copy, copy.Count, null, null));
                copy.Add(CosNull.Instance);
                continue;
            }

            copy.Add(Map(item));
        }
    }

    private void FillDictionary(CosDictionary source, CosDictionary copy, bool isStream)
    {
        bool pageTreeNode = IsType(source, KnownNames.Page) || IsType(source, KnownNames.Pages);
        bool dropCrypt = isStream && Source.IsEncrypted && StartsWithCrypt(source);
        CarriedFeatures.InDictionary(Source, source, isStream, _carry);
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            if ((pageTreeNode && entry.Key.Equals(KnownNames.Parent)) || (isStream && entry.Key.Equals(KnownNames.Length)))
            {
                continue;
            }

            if (dropCrypt && (entry.Key.Equals(FilterNames.Filter) || entry.Key.Equals(FilterNames.DecodeParms)))
            {
                if (WithoutFirst(entry.Value) is { } rest)
                {
                    copy[entry.Key] = rest;
                }

                continue;
            }

            if (IsUncopiedPage(entry.Value, out CosReference? page))
            {
                Wait(page, new PendingPage(null, 0, copy, entry.Key));
                continue;
            }

            if (Map(entry.Value) is { } mapped and not CosNull)
            {
                copy[entry.Key] = mapped;
            }
        }
    }

    /// <summary>
    /// Records in the target a feature a copied object uses (issue #290), once per session: the target's next save raises its
    /// version to the feature's, or refuses in strict mode (§7.5.2).
    /// </summary>
    private void Carry(VersionedFeature feature)
    {
        if (_carried.Add(feature))
        {
            Target.RequireFeature(feature);
        }
    }

    /// <summary>
    /// Whether the stream's first filter is <c>Crypt</c> (§7.4.10: it shall be first): its data is already decrypted when read, so
    /// the copy leaves the filter and its parameters out.
    /// </summary>
    private bool StartsWithCrypt(CosDictionary stream)
    {
        CosObject filter = EntryReader.Get(Source, stream, FilterNames.Filter) ?? CosNull.Instance;
        CosObject first = filter is CosArray { Count: > 0 } filters ? Source.Resolve(filters[0]) : filter;
        return FilterNames.Crypt.Equals(first);
    }

    /// <summary>A <c>Filter</c> or <c>DecodeParms</c> value without its first element; null when nothing is left.</summary>
    private CosArray? WithoutFirst(CosObject value)
    {
        if (Source.Resolve(value) is not CosArray { Count: > 1 } array)
        {
            return null;
        }

        var rest = new CosArray();
        for (int index = 1; index < array.Count; index++)
        {
            rest.Add(Map(array[index]));
        }

        return rest;
    }

    /// <summary>Whether <paramref name="value"/> is a reference to a page of the source that the session has not copied yet.</summary>
    private bool IsUncopiedPage(CosObject value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CosReference? page)
    {
        page = value as CosReference;
        return page is not null
            && !_copies.ContainsKey(page)
            && Source.Resolve(page) is CosDictionary dictionary
            && IsType(dictionary, KnownNames.Page);
    }

    private void Wait(CosReference page, PendingPage slot)
    {
        if (!_pendingPages.TryGetValue(page, out List<PendingPage>? slots))
        {
            slots = [];
            _pendingPages[page] = slots;
        }

        slots.Add(slot);
    }

    private bool IsType(CosDictionary dictionary, CosName type) =>
        type.Equals(EntryReader.Get(Source, dictionary, KnownNames.Type));

    /// <summary>A slot in a copied container that names a source page the session has not copied yet.</summary>
    private sealed record PendingPage(CosArray? Array, int Index, CosDictionary? Dictionary, CosName? Key);
}
