namespace Broadside.Fonts.Resolution;

/// <summary>
/// An engine's font resolvers in the order they are asked: those registered with <see cref="PdfOptions.UseFontResolver"/>, then
/// the operating system's. Immutable; shared by every document of the engine.
/// </summary>
/// <remarks>ISO 32000-2 §9.6.2.2, §9.7.5.2, §9.8. ADR 0009.</remarks>
internal sealed class FontResolverChain
{
    private readonly IFontResolver[] _resolvers;

    /// <summary>Initializes a new instance of the <see cref="FontResolverChain"/> class.</summary>
    /// <param name="resolvers">The registered resolvers, in registration order.</param>
    /// <param name="system">The operating system's resolver, asked last; <see langword="null"/> for none.</param>
    public FontResolverChain(IReadOnlyList<IFontResolver> resolvers, IFontResolver? system)
    {
        _resolvers = system is null ? [.. resolvers] : [.. resolvers, system];
        System = system;
    }

    /// <summary>Gets the chain of an engine with default options: the operating system's fonts only.</summary>
    public static FontResolverChain Default { get; } = new([], SystemFontResolver.Shared);

    /// <summary>Gets the operating system's resolver, or <see langword="null"/>.</summary>
    public IFontResolver? System { get; }

    /// <summary>Asks each resolver in turn for a font program.</summary>
    /// <param name="query">The font.</param>
    /// <returns>The first answer, or <see langword="null"/>.</returns>
    public FontResolution? ResolveFont(FontQuery query)
    {
        foreach (IFontResolver resolver in _resolvers)
        {
            if (resolver.ResolveFont(query) is { } resolution)
            {
                return resolution;
            }
        }

        return null;
    }

    /// <summary>Asks each resolver in turn for a named resource (a predefined CMap, a CID-to-Unicode table).</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="name">The name.</param>
    /// <param name="data">The first answer's bytes.</param>
    /// <returns><see langword="true"/> when a resolver has it.</returns>
    public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
    {
        foreach (IFontResolver resolver in _resolvers)
        {
            if (resolver.TryResolveResource(kind, name, out data))
            {
                return true;
            }
        }

        data = default;
        return false;
    }
}
