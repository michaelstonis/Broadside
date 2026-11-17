using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// One engine's filters by name: the built-in general-purpose filters, with the image codecs and other filters its options registered.
/// Immutable; built once when the engine is constructed (or shared, for the options' default filter set:
/// <see cref="ImageCodecRegistry.All"/>) and shared by every document it opens. There is no mutable global registry.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.1, Table 6. The general-purpose filters of §7.4.2 to §7.4.5 are always present. The image codecs of §7.4.6 to
/// §7.4.9 are registrations of the options (<see cref="PdfOptions()"/> registers all four), so that an app whose options leave
/// them out does not carry them after trimming: nothing here names a codec type. <c>Crypt</c> (§7.4.10) is never here; the stream
/// decoder applies it through its crypt filter handler.
/// </remarks>
internal sealed class FilterRegistry
{
    private readonly FrozenDictionary<CosName, IStreamFilter> _filters;

    private FilterRegistry(FrozenDictionary<CosName, IStreamFilter> filters) => _filters = filters;

    /// <summary>Gets the built-in general-purpose filters, one stateless instance each: present in every registry.</summary>
    public static IReadOnlyList<IStreamFilter> BuiltIn { get; } =
    [
        new AsciiHexDecodeFilter(),
        new Ascii85DecodeFilter(),
        new LzwDecodeFilter(),
        new FlateDecodeFilter(),
        new RunLengthDecodeFilter(),
    ];

    /// <summary>
    /// Gets the registry of the built-in filters alone, built once: an engine whose options register nothing (not even an image
    /// codec) shares it, so constructing one does not build a frozen dictionary, whose bucket layout (and so its allocations) depends
    /// on the process's hash seed.
    /// </summary>
    public static FilterRegistry WithoutImageCodecs { get; } = Create([]);

    /// <summary>Builds a registry of the built-in filters with <paramref name="registrations"/> applied in order; a later one replaces an earlier one of the same name.</summary>
    /// <param name="registrations">The filters registered through the options, image codecs included.</param>
    /// <returns>The registry.</returns>
    public static FilterRegistry Create(IEnumerable<IStreamFilter> registrations)
    {
        var filters = new Dictionary<CosName, IStreamFilter>();
        foreach (IStreamFilter filter in BuiltIn.Concat(registrations))
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
