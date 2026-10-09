using Broadside.Objects;

namespace Broadside.Security;

/// <summary>The security handlers of one engine, by <c>Filter</c> name and by <c>SubFilter</c> format. Immutable once built.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.2, Table 20: the handler a document's <c>Filter</c> names opens it; when <c>SubFilter</c> is present, any handler
/// implementing that format may. <see cref="StandardSecurityHandler"/> is the default for <c>Standard</c> and <see cref="PublicKeySecurityHandler"/>
/// for <c>Adobe.PubSec</c> and the <c>adbe.pkcs7</c> SubFilters (§7.6.5); a handler registered later for the same name replaces an
/// earlier one.
/// </remarks>
internal sealed class SecurityHandlerRegistry
{
    private readonly Dictionary<CosName, ISecurityHandler> _byFilter = [];
    private readonly Dictionary<CosName, ISecurityHandler> _bySubFilter = [];

    private SecurityHandlerRegistry()
    {
    }

    /// <summary>Gets the registry of an engine with no handlers of its own: the standard and public-key handlers.</summary>
    public static SecurityHandlerRegistry Default { get; } = Create([]);

    /// <summary>Builds a registry from the defaults and the handlers registered on the options, in order.</summary>
    /// <param name="handlers">The registered handlers.</param>
    /// <returns>The registry.</returns>
    public static SecurityHandlerRegistry Create(IEnumerable<ISecurityHandler> handlers)
    {
        var registry = new SecurityHandlerRegistry();
        registry.Add(new StandardSecurityHandler());
        registry.Add(new PublicKeySecurityHandler());
        foreach (ISecurityHandler handler in handlers)
        {
            registry.Add(handler);
        }

        return registry;
    }

    /// <summary>Finds the handler for a document: by <paramref name="filter"/>, else by <paramref name="subFilter"/>.</summary>
    /// <param name="filter">The encryption dictionary's <c>Filter</c>.</param>
    /// <param name="subFilter">Its <c>SubFilter</c>, or <see langword="null"/>.</param>
    /// <returns>The handler, or <see langword="null"/>.</returns>
    public ISecurityHandler? Find(CosName? filter, CosName? subFilter)
    {
        if (filter is not null && _byFilter.TryGetValue(filter, out ISecurityHandler? handler))
        {
            return handler;
        }

        return subFilter is not null && _bySubFilter.TryGetValue(subFilter, out handler) ? handler : null;
    }

    private void Add(ISecurityHandler handler)
    {
        _byFilter[handler.Filter] = handler;
        foreach (CosName subFilter in handler.SubFilters)
        {
            _bySubFilter[subFilter] = handler;
        }
    }
}
