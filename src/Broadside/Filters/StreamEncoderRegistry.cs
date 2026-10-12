using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// One engine's encoders by filter name: the managed defaults, with the replacements and additions its options registered. Immutable;
/// built once when the engine is constructed and shared by every document it opens or creates. There is no static or global registry.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.1, Table 6. The defaults encode <c>ASCIIHexDecode</c>, <c>ASCII85Decode</c>, <c>FlateDecode</c> and <c>RunLengthDecode</c>.</remarks>
internal sealed class StreamEncoderRegistry
{
    private readonly FrozenDictionary<CosName, IStreamEncoder> _encoders;

    private StreamEncoderRegistry(FrozenDictionary<CosName, IStreamEncoder> encoders) => _encoders = encoders;

    /// <summary>Gets the managed default encoders, one stateless instance each.</summary>
    public static IReadOnlyList<IStreamEncoder> Defaults { get; } =
    [
        new AsciiHexEncoder(),
        new Ascii85Encoder(),
        new FlateEncoder(),
        new RunLengthEncoder(),
    ];

    /// <summary>Gets the registry of the defaults alone.</summary>
    public static StreamEncoderRegistry Default { get; } = Create([]);

    /// <summary>Gets the encoder that writes <c>FlateDecode</c> data, which saving uses to compress streams.</summary>
    public IStreamEncoder Flate => _encoders[FilterNames.FlateDecode];

    /// <summary>Builds a registry of the defaults with <paramref name="registrations"/> applied in order; a later one replaces an earlier one of the same name.</summary>
    /// <param name="registrations">The encoders registered through the options.</param>
    /// <returns>The registry.</returns>
    public static StreamEncoderRegistry Create(IEnumerable<IStreamEncoder> registrations)
    {
        var encoders = new Dictionary<CosName, IStreamEncoder>();
        foreach (IStreamEncoder encoder in Defaults.Concat(registrations))
        {
            encoders[encoder.Name] = encoder;
        }

        return new StreamEncoderRegistry(encoders.ToFrozenDictionary());
    }

    /// <summary>Finds the encoder registered under a full filter name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="encoder">The encoder.</param>
    /// <returns><see langword="true"/> when one is registered.</returns>
    public bool TryGet(CosName name, [NotNullWhen(true)] out IStreamEncoder? encoder) => _encoders.TryGetValue(name, out encoder);
}
