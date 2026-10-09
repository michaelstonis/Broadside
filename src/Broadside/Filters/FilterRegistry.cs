using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// One engine's filters by name: the managed defaults, with the replacements and additions its options registered. Immutable; built
/// once when the engine is constructed and shared by every document it opens. There is no static or global registry.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.1, Table 6. The image codecs of §7.4.6 to §7.4.9 join the defaults as Phase 2B implements them.</remarks>
internal sealed class FilterRegistry
{
    private readonly FrozenDictionary<CosName, IStreamFilter> _filters;

    private FilterRegistry(FrozenDictionary<CosName, IStreamFilter> filters) => _filters = filters;

    /// <summary>Gets the managed default filters, one stateless instance each.</summary>
    public static IReadOnlyList<IStreamFilter> Defaults { get; } =
    [
        new AsciiHexDecodeFilter(),
        new Ascii85DecodeFilter(),
        new LzwDecodeFilter(),
        new FlateDecodeFilter(),
        new RunLengthDecodeFilter(),
    ];

    /// <summary>Builds a registry of the defaults with <paramref name="registrations"/> applied in order; a later one replaces an earlier one of the same name.</summary>
    /// <param name="registrations">The filters registered through the options.</param>
    /// <returns>The registry.</returns>
    public static FilterRegistry Create(IEnumerable<IStreamFilter> registrations)
    {
        var filters = new Dictionary<CosName, IStreamFilter>();
        foreach (IStreamFilter filter in Defaults.Concat(registrations))
        {
            filters[filter.Name] = filter;
        }

        return new FilterRegistry(filters.ToFrozenDictionary());
    }

    /// <summary>Finds the filter registered under a full filter name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><see langword="true"/> when one is registered.</returns>
    public bool TryGet(CosName name, [NotNullWhen(true)] out IStreamFilter? filter) => _filters.TryGetValue(name, out filter);
}
