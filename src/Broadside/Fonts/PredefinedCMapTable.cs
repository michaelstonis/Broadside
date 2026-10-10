using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Broadside.Fonts;

/// <summary>
/// What the core knows of each predefined CMap of Table 116 without its mappings: character collection, writing mode and codespace
/// ranges (with those of the CMap it uses). It stands in for a CMap no font resolver supplies, so codes still split correctly.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7.5.2, Table 116, and §9.7.6.2; Adobe TN 5099 §1.3. The mappings ship in the Broadside.Fonts.Cmaps package;
/// the table is generated from the same pinned files (<c>src/Broadside.Fonts.Cmaps/Data/generate.py</c>).
/// </remarks>
internal static partial class PredefinedCMapTable
{
    private static readonly ConcurrentDictionary<string, CMap> Fallbacks = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns a CMap with the codespace, character collection and writing mode of a Table 116 CMap and no mappings, so every code
    /// selects CID 0; <see langword="null"/> when the name is not in Table 116. Built once per name and shared.
    /// </summary>
    public static CMap? CreateFallback(string name) =>
        ByName.Value.TryGetValue(name, out Entry? entry)
            ? Fallbacks.GetOrAdd(name, static (_, entry) => CMap.CreateFallback(entry.Name, entry.WritingMode, new CidSystemInfo(entry.Registry, entry.Ordering, entry.Supplement), entry.Codespace), entry)
            : null;

    /// <summary>Returns whether a name is a predefined CMap of Table 116 other than Identity-H and Identity-V.</summary>
    public static bool Contains(string name) => ByName.Value.ContainsKey(name);

    /// <summary>Gets the entries by name (built on first use, after the generated array is initialized).</summary>
    private static Lazy<FrozenDictionary<string, Entry>> ByName { get; } =
        new(() => Entries.ToFrozenDictionary(entry => entry.Name, StringComparer.Ordinal), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>One CMap: its name, <c>CIDSystemInfo</c>, <c>WMode</c> and codespace ranges.</summary>
    private sealed record Entry(string Name, string Registry, string Ordering, int Supplement, int WritingMode, CodespaceRange[] Codespace);
}
