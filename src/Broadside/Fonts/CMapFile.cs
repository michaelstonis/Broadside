namespace Broadside.Fonts;

/// <summary>What <see cref="CMapParser"/> reads from a CMap file, before the parent is resolved and the tables are built.</summary>
/// <remarks>Adobe TN 5014 §7.</remarks>
internal sealed class CMapFile
{
    /// <summary>Gets or sets the <c>CMapName</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the <c>WMode</c> the file gives, if any.</summary>
    public int? WMode { get; set; }

    /// <summary>Gets or sets the <c>CIDSystemInfo</c> (the first one, when the file gives an array).</summary>
    public CidSystemInfo? SystemInfo { get; set; }

    /// <summary>Gets or sets the name before <c>usecmap</c>.</summary>
    public string? UseCMapName { get; set; }

    /// <summary>Gets the codespace ranges, in file order.</summary>
    public List<CodespaceRange> Codespace { get; } = [];

    /// <summary>Gets the character mappings by code length (index 0 = one byte), in file order.</summary>
    public List<IntervalTable.Interval>[] Cids { get; } = [[], [], [], []];

    /// <summary>Gets the notdef mappings by code length, in file order.</summary>
    public List<IntervalTable.Interval>[] Notdefs { get; } = [[], [], [], []];

    /// <summary>
    /// Gets the Unicode mappings of a ToUnicode CMap by code length (index 0 = one byte), in file order: each interval's value is
    /// an index into <see cref="BfEntries"/>. Filled only when the file is read for its Unicode destinations.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10.3; Adobe TN 5014 §7.4 (<c>beginbfchar</c>, <c>beginbfrange</c>).</remarks>
    public List<IntervalTable.Interval>[] Bf { get; } = [[], [], [], []];

    /// <summary>Gets the destinations of <see cref="Bf"/>.</summary>
    public List<BfEntry> BfEntries { get; } = [];

    /// <summary>Gets the UTF-16 text of the destinations, which <see cref="BfEntries"/> and <see cref="BfArrayElements"/> slice.</summary>
    public List<char> BfText { get; } = [];

    /// <summary>Gets the elements of array destinations (<c>[&lt;…&gt; &lt;…&gt;]</c>): slices of <see cref="BfText"/>.</summary>
    public List<(int Start, int Length)> BfArrayElements { get; } = [];

    /// <summary>One Unicode destination: for an increment entry a slice of <see cref="BfText"/> whose last scalar grows with the code; for an array entry a slice of <see cref="BfArrayElements"/>.</summary>
    /// <param name="Low">The first code of the entry.</param>
    /// <param name="IsArray">Whether the entry maps each code to its own array element.</param>
    /// <param name="Start">The first UTF-16 unit, or the first array element.</param>
    /// <param name="Count">The number of UTF-16 units, or of array elements.</param>
    internal readonly record struct BfEntry(uint Low, bool IsArray, int Start, int Count);
}
