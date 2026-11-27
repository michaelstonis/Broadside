using System.Runtime.InteropServices;
using Broadside.Objects;

namespace Broadside.Forms.FormData;

/// <summary>
/// Copies object graphs between a document and a forms data file, in either direction: each indirect object of the source becomes
/// one new indirect object of the target (made by <c>add</c>) the first time it is reached, and every later reference names that
/// copy. Pages are never followed: a reference to a page or page tree node copies as null. A reference to an object whose body is
/// itself a reference, which no indirect object can be (§7.3.10), copies as null too, and <c>invalid</c> is told which reference
/// named it, so the caller records the repair (ADR 0005).
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.3.10 and §12.7.8.3.4 (an FDF annotation names its page by number, not by reference). The walk keeps a stack, so a
/// cyclic or deeply nested graph exhausts neither the stack nor memory. <see cref="PdfObjectCopier"/> does the same between two
/// documents; a forms data file is not a document, so it has its own.
/// </remarks>
internal sealed class CosGraphCopier(Func<CosObject?, CosObject> resolve, Func<CosObject, CosReference> add, Action<CosReference>? invalid = null)
{
    private static readonly CosName Page = new("Page");
    private static readonly CosName Pages = new("Pages");

    private readonly Dictionary<CosReference, CosReference> _copies = [];
    private readonly Stack<(CosObject Source, CosObject Copy)> _work = new();

    /// <summary>Makes <paramref name="copy"/> the copy of <paramref name="source"/>: every reference to it names the copy, and the walk never fills it.</summary>
    public void Assign(CosReference source, CosReference copy) => _copies[source] = copy;

    /// <summary>Gets the target's copy of the source object <paramref name="source"/> names, when the walk has made or been given one.</summary>
    public bool TryGetCopy(CosReference source, out CosReference? copy) => _copies.TryGetValue(source, out copy);

    /// <summary>Copies <paramref name="value"/> and every object it reaches; returns the copy (null for nothing or a page).</summary>
    public CosObject Copy(CosObject value)
    {
        CosObject copy = Map(value);
        Drain();
        return copy;
    }

    /// <summary>Copies the entries of <paramref name="source"/> into <paramref name="target"/>, but those <paramref name="skip"/> names.</summary>
    public void CopyEntries(CosDictionary source, CosDictionary target, Func<CosName, bool> skip)
    {
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            if (!skip(entry.Key) && Map(entry.Value) is { } mapped and not CosNull)
            {
                target[entry.Key] = mapped;
            }
        }

        Drain();
    }

    private CosObject Map(CosObject value)
    {
        switch (value)
        {
            case CosReference reference:
                if (_copies.TryGetValue(reference, out CosReference? copied))
                {
                    return copied;
                }

                CosObject resolved = resolve(reference);
                if (resolved is CosReference)
                {
                    invalid?.Invoke(reference);
                    return CosNull.Instance;
                }

                if (resolved is CosNull || IsPage(resolved))
                {
                    return CosNull.Instance;
                }

                CosObject shell = Shell(resolved);
                CosReference added = add(shell);
                _copies[reference] = added;
                return added;
            case CosDictionary dictionary when IsPage(dictionary):
                return CosNull.Instance;
            case CosArray or CosDictionary or CosStream:
                return Shell(value);
            default:
                return value;
        }
    }

    private bool IsPage(CosObject value) =>
        value is CosDictionary dictionary && dictionary.TryGetValue(KnownNames.Type, out CosObject? type) && resolve(type) is CosName name
        && (name.Equals(Page) || name.Equals(Pages));

    private CosObject Shell(CosObject value)
    {
        CosObject shell = value switch
        {
            CosArray => new CosArray(),
            CosDictionary => new CosDictionary(),
            CosStream stream => new CosStream(new CosDictionary(), Owned(stream.EncodedData)),
            _ => value,
        };

        if (!ReferenceEquals(shell, value))
        {
            _work.Push((value, shell));
        }

        return shell;
    }

    /// <summary>Data in a managed array stays alive with the copy; data in a mapped file view is copied out.</summary>
    private static ReadOnlyMemory<byte> Owned(ReadOnlyMemory<byte> data) => MemoryMarshal.TryGetArray(data, out _) ? data : data.ToArray();

    private void Drain()
    {
        while (_work.TryPop(out (CosObject Source, CosObject Copy) item))
        {
            switch (item.Source)
            {
                case CosArray array:
                    var target = (CosArray)item.Copy;
                    foreach (CosObject element in array)
                    {
                        target.Add(Map(element));
                    }

                    break;
                case CosStream stream:
                    Fill(stream.Dictionary, ((CosStream)item.Copy).Dictionary, isStream: true);
                    break;
                case CosDictionary dictionary:
                    Fill(dictionary, (CosDictionary)item.Copy, isStream: false);
                    break;
            }
        }
    }

    private void Fill(CosDictionary source, CosDictionary copy, bool isStream)
    {
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            if (isStream && entry.Key.Equals(KnownNames.Length))
            {
                continue;
            }

            if (Map(entry.Value) is { } mapped and not CosNull)
            {
                copy[entry.Key] = mapped;
            }
        }
    }
}
