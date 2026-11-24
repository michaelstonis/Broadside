using Broadside.Caching;
using Broadside.Diagnostics;

namespace Broadside.Fonts.Resolution;

/// <summary>A parsed font resource and the deviations its parse recorded, which each document that uses it reports once.</summary>
/// <typeparam name="T">The parsed type.</typeparam>
/// <param name="Value">The parsed table; <see langword="null"/> when no resolver has the resource.</param>
/// <param name="Diagnostics">What the parse found wrong with the resource, in order.</param>
internal sealed record SharedFontResource<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
    where T : class
{
    /// <summary>Gets the answer for a resource no resolver supplies.</summary>
    public static SharedFontResource<T> Absent { get; } = new(null, []);
}

/// <summary>
/// The parsed predefined CMaps and CID-to-Unicode tables of one engine: each is parsed the first time any document of the engine
/// needs it and shared, immutable, by every document and thread after that.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.5.2 (Table 116) and §9.10.2 (step d). The resources come from the engine's font resolvers, which are fixed when
/// the engine is built, so a table can only differ between engines that were given different resolvers; hence the cache belongs to
/// the engine's <see cref="FontResolverChain"/> and nothing is shared between engines.
/// </para>
/// <para>
/// Bounded: only a table a resolver supplied is kept (a name no resolver has is asked again, and a document remembers that answer
/// for itself), at most <see cref="MaxTables"/> tables and <see cref="MaxSourceBytes"/> bytes of resource text together. A table
/// that would exceed either is still returned, parsed for that document alone, and not kept. A parsed table takes about three times
/// the size of its text on the heap (measured: the Broadside.Fonts.Cmaps package's 59 CMaps, 2.3 MB of text, hold 5.5 MB; its five
/// UCS2 tables, 1.1 MB of text, hold 3.4 MB; 8.9 MB with all 64 kept), so the byte limit bounds what an engine holds at about
/// 50 MB and the package, in full, uses a sixth of it.
/// </para>
/// <para>
/// Thread safety: every table is built once however many threads ask at the same time (<see cref="OnceCache{TKey, TValue}"/>);
/// the parsed tables are never changed after they are built.
/// </para>
/// </remarks>
internal sealed class FontResourceCache
{
    /// <summary>The most tables kept: four times the Broadside.Fonts.Cmaps package's 64 files.</summary>
    public const int MaxTables = 256;

    /// <summary>The most bytes of resource text whose parsed tables are kept.</summary>
    public const long MaxSourceBytes = 16L * 1024 * 1024;

    private readonly OnceCache<string, SharedFontResource<CMap>> _cmaps = new(StringComparer.Ordinal);
    private readonly OnceCache<string, SharedFontResource<ToUnicodeMap>> _unicodeTables = new(StringComparer.Ordinal);
    private long _keptBytes;
    private int _keptTables;

    /// <summary>Gets how many tables are kept.</summary>
    public int Count => Volatile.Read(ref _keptTables);

    /// <summary>Gets how many bytes of resource text the kept tables were parsed from.</summary>
    public long KeptBytes => Volatile.Read(ref _keptBytes);

    /// <summary>Returns the predefined CMap a resolver supplies, parsed once for the engine, with the CMap it uses.</summary>
    /// <param name="resolvers">The engine's resolvers.</param>
    /// <param name="name">The CMap's name; Identity-H and Identity-V are built in and never asked for here.</param>
    /// <param name="depth">How many CMaps use this one through <c>usecmap</c> on the way here.</param>
    /// <returns>The CMap, or <see cref="SharedFontResource{T}.Absent"/> when no resolver has it.</returns>
    /// <remarks>Adobe TN 5014 §7.4: <c>usecmap</c> nests at most five levels; deeper, or in a cycle, the CMap is not available.</remarks>
    public SharedFontResource<CMap> GetCMap(FontResolverChain resolvers, string name, int depth) =>
        _cmaps.GetOrCreate(
            name,
            (Cache: this, Resolvers: resolvers, Depth: depth),
            static (key, state) => state.Cache.LoadCMap(state.Resolvers, key, state.Depth),
            static (key, _) => new SharedFontResource<CMap>(
                null,
                [new Diagnostic(
                    CMapDiagnostics.CMapUseCMapCycle,
                    DiagnosticSeverity.Error,
                    $"The predefined CMap /{key} uses itself through its usecmap chain (Adobe TN 5014 §7.4); the chain is cut there.")]));

    /// <summary>Returns the Registry-Ordering-UCS2 table a resolver supplies, parsed once for the engine.</summary>
    /// <param name="resolvers">The engine's resolvers.</param>
    /// <param name="name">The table's name, such as <c>Adobe-Japan1-UCS2</c>.</param>
    /// <returns>The table, or <see cref="SharedFontResource{T}.Absent"/> when no resolver has it.</returns>
    public SharedFontResource<ToUnicodeMap> GetUnicodeTable(FontResolverChain resolvers, string name) =>
        _unicodeTables.GetOrCreate(
            name,
            (Cache: this, Resolvers: resolvers),
            static (key, state) => state.Cache.LoadUnicodeTable(state.Resolvers, key),
            static (_, _) => SharedFontResource<ToUnicodeMap>.Absent);

    private Created<SharedFontResource<CMap>> LoadCMap(FontResolverChain resolvers, string name, int depth)
    {
        if (!resolvers.TryResolveResource(FontResourceKind.CMap, name, out ReadOnlyMemory<byte> data))
        {
            return new(SharedFontResource<CMap>.Absent, Keep: false);
        }

        // The table comes from the machine, not the file: it is parsed outside any document, and each document reports what it found.
        var context = new CMapContext();
        CMapFile file = CMapParser.Parse(data.Span, context);
        CMap? parent = null;
        if (file.UseCMapName is { } used)
        {
            parent = FindParent(resolvers, used, depth, context);
        }

        var cmap = CMap.Create(file, parent, file.WMode ?? 0, context);
        return new(new SharedFontResource<CMap>(cmap, context.Diagnostics), Keep: TryKeep(data.Length));
    }

    private static CMap? FindParent(FontResolverChain resolvers, string used, int depth, CMapContext context)
    {
        // The shared cache is per engine, so the parent comes from the same cache (a built-in is answered before it).
        if (CMap.FindBuiltIn(used) is { } builtIn)
        {
            return builtIn;
        }

        if (depth + 1 <= CMapContext.DefaultMaxUseCMapDepth && resolvers.Resources.GetCMap(resolvers, used, depth + 1) is { Value: { } found } resource)
        {
            foreach (Diagnostic diagnostic in resource.Diagnostics)
            {
                // A context's diagnostics are always made from typed codes (ContextDiagnostics), so this never looks a name up.
                context.Report(diagnostic.TypedKind ?? new DiagnosticCode(diagnostic.Code, DiagnosticArea.None, diagnostic.Severity, clause: null, description: null), diagnostic.Severity, diagnostic.Message);
            }

            return found;
        }

        CMap? fallback = PredefinedCMapTable.CreateFallback(used);
        context.Report(CMapDiagnostics.CMapUnavailable, DiagnosticSeverity.Information, PdfDocument.UnavailableMessage(used, fallback is not null));
        return fallback;
    }

    private Created<SharedFontResource<ToUnicodeMap>> LoadUnicodeTable(FontResolverChain resolvers, string name)
    {
        if (!resolvers.TryResolveResource(FontResourceKind.CidToUnicode, name, out ReadOnlyMemory<byte> data))
        {
            return new(SharedFontResource<ToUnicodeMap>.Absent, Keep: false);
        }

        var context = new CMapContext();
        ToUnicodeMap map = ToUnicodeMap.Parse(data.Span, context);
        return new(new SharedFontResource<ToUnicodeMap>(map, context.Diagnostics), Keep: TryKeep(data.Length));
    }

    private bool TryKeep(int sourceLength)
    {
        if (Interlocked.Increment(ref _keptTables) > MaxTables || Interlocked.Add(ref _keptBytes, sourceLength) > MaxSourceBytes)
        {
            Interlocked.Decrement(ref _keptTables);
            Interlocked.Add(ref _keptBytes, -sourceLength);
            return false;
        }

        return true;
    }
}
