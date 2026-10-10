using Broadside.Objects;

namespace Broadside.Graphics.Functions;

/// <summary>A container a compiled function was built from, and its <c>Version</c> at the time.</summary>
/// <param name="Container">A <see cref="CosDictionary"/>, <see cref="CosArray"/> or <see cref="CosStream"/>.</param>
/// <param name="Version">The container's version when it was read.</param>
/// <remarks>
/// ISO 32000-2 §7.10; ADR 0004 (live views). A compiled function is a snapshot of the COS objects it read; it is stale as soon as
/// one of them has changed through the public API, and is then compiled again.
/// </remarks>
internal readonly record struct FunctionDependency(CosObject Container, int Version)
{
    /// <summary>Gets a value indicating whether the container is unchanged since it was read.</summary>
    public bool IsCurrent => VersionOf(Container) == Version;

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
}
