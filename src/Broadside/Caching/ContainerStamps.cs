using Broadside.Objects;

namespace Broadside.Caching;

/// <summary>
/// The COS containers a cached derivation read, each with its <c>Version</c> at the time, and the document's <c>ObjectsVersion</c>:
/// the cache is current while none of them has changed through the public API (the cache-invalidation rule of live views).
/// </summary>
/// <remarks>
/// <para>
/// A removed object (<see cref="PdfDocument.RemoveObject"/>) changes what a reference in an unchanged container resolves to, and an
/// added one can give a dangling reference an object (ISO 32000-2 §7.3.10), so the container versions alone would miss both: the
/// document's <c>ObjectsVersion</c>, recorded when the stamps are created (before the derivation reads anything), catches them
/// (issue #303).
/// </para>
/// <para>
/// Filled by the one thread that builds the derivation, then published with it and only read: safe for concurrent readers under the
/// document's thread-safety contract. Objects that cannot change (names, numbers, strings) are not recorded.
/// </para>
/// </remarks>
internal sealed class ContainerStamps
{
    private readonly List<(CosObject Container, int Version)> _stamps = [];
    private readonly PdfDocument _document;
    private readonly int _objectsVersion;

    /// <summary>Initializes a new instance of the <see cref="ContainerStamps"/> class, recording the document's objects as they are now.</summary>
    /// <param name="document">The document the derivation resolves references through.</param>
    public ContainerStamps(PdfDocument document)
    {
        _document = document;
        _objectsVersion = document.ObjectsVersion;
    }

    /// <summary>Gets a value indicating whether no object was added or removed and no recorded container has changed since they were recorded.</summary>
    public bool IsCurrent
    {
        get
        {
            if (_document.ObjectsVersion != _objectsVersion)
            {
                return false;
            }

            foreach ((CosObject container, int version) in _stamps)
            {
                if (VersionOf(container) != version)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The current version of a container; 0 for objects that cannot change.</summary>
    /// <param name="value">The object.</param>
    /// <returns>Its version.</returns>
    public static int VersionOf(CosObject value) => value switch
    {
        CosDictionary dictionary => dictionary.Version,
        CosArray array => array.Version,
        CosStream stream => stream.Version,
        _ => 0,
    };

    /// <summary>Records <paramref name="container"/> at its current version; anything that cannot change is ignored.</summary>
    /// <param name="container">The container read, or <see langword="null"/>.</param>
    public void Add(CosObject? container)
    {
        if (container is CosDictionary or CosArray or CosStream)
        {
            _stamps.Add((container, VersionOf(container)));
        }
    }
}
