using Broadside.Objects;

namespace Broadside.Caching;

/// <summary>
/// The COS containers a cached derivation read, each with its <c>Version</c> at the time: the cache is current while none of them
/// has changed through the public API (the cache-invalidation rule of live views, ADR 0004).
/// </summary>
/// <remarks>
/// Filled by the one thread that builds the derivation, then published with it and only read: safe for concurrent readers under the
/// document's thread-safety contract. Objects that cannot change (names, numbers, strings) are not recorded.
/// </remarks>
internal sealed class ContainerStamps
{
    private readonly List<(CosObject Container, int Version)> _stamps = [];

    /// <summary>Gets a value indicating whether no recorded container has changed since it was recorded.</summary>
    public bool IsCurrent
    {
        get
        {
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
