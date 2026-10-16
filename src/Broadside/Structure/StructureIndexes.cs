using Broadside.Caching;
using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>The containers one index was built from, each with the <c>Version</c> it had when read.</summary>
internal sealed class StructureStamps
{
    private readonly List<(CosObject Container, int Version)> _items = [];

    public IReadOnlyList<(CosObject Container, int Version)> Items => _items;

    /// <summary>Records <paramref name="value"/> when it is a container (a stream counts through its dictionary); anything else is immutable.</summary>
    public void Add(CosObject? value)
    {
        switch (value)
        {
            case CosDictionary dictionary:
                _items.Add((dictionary, dictionary.Version));
                break;
            case CosArray array:
                _items.Add((array, array.Version));
                break;
            case CosStream stream:
                _items.Add((stream.Dictionary, stream.Dictionary.Version));
                break;
        }
    }

    public static bool IsCurrent(CosObject container, int version) => container switch
    {
        CosDictionary dictionary => dictionary.Version == version,
        CosArray array => array.Version == version,
        _ => true,
    };
}

/// <summary>
/// One generation of the structure tree's derived indexes (the K walk, role resolutions, declared namespaces, MCID indexes, the
/// parent-tree page index), with the containers each was built from.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7. A generation is current while every container it read keeps the version it had (issue #156: read caches are
/// keyed on <c>Version</c>, so an edit of an element, a <c>K</c> array, a role map or the parent tree is seen by the next lookup).
/// The check is free while <see cref="CosMutation.Epoch"/> has not moved since the generation was last found current. Adding or
/// removing an indirect object (<see cref="PdfDocument.AddObject"/>, <see cref="PdfDocument.RemoveObject"/>) changes what a reference
/// in an unchanged container resolves to (§7.3.10), so the generation also records the document's <c>ObjectsVersion</c> (issue #302).
/// </remarks>
internal sealed class StructureIndexes
{
    private readonly Lock _gate = new();
    private readonly List<(CosObject Container, int Version)> _stamps = [];
    private readonly int _objectsVersion;
    private long _epoch;

    public StructureIndexes(long epoch, int objectsVersion)
    {
        _epoch = epoch;
        _objectsVersion = objectsVersion;
    }

    public OnceCache<int, StructureWalk> Walk { get; } = new();

    public OnceCache<StructureContext.RoleKey, RoleResolution> Roles { get; } = new(StructureContext.RoleKeyComparer.Instance);

    public OnceCache<int, HashSet<CosDictionary>> DeclaredNamespaces { get; } = new();

    public OnceCache<CosObject, MarkedContentIndex> MarkedContent { get; } = new(ReferenceEqualityComparer.Instance);

    public OnceCache<int, Dictionary<(CosDictionary, int), CosDictionary>> ParentTreePages { get; } = new();

    /// <summary>Gets the stamps of the parent tree's nodes, recorded once per generation (every MCID index depends on them).</summary>
    public OnceCache<int, bool> ParentTreeStamped { get; } = new();

    /// <summary>Adds the containers an index was built from.</summary>
    public void Record(StructureStamps stamps)
    {
        lock (_gate)
        {
            _stamps.AddRange(stamps.Items);
        }
    }

    /// <summary>Whether nothing this generation was built from has changed, given the current mutation epoch and the document's objects version.</summary>
    public bool IsCurrent(long epoch, int objectsVersion)
    {
        if (Volatile.Read(ref _epoch) == epoch)
        {
            return true;
        }

        if (objectsVersion != _objectsVersion)
        {
            return false;
        }

        lock (_gate)
        {
            foreach ((CosObject container, int version) in _stamps)
            {
                if (!StructureStamps.IsCurrent(container, version))
                {
                    return false;
                }
            }

            Volatile.Write(ref _epoch, epoch);
            return true;
        }
    }
}
