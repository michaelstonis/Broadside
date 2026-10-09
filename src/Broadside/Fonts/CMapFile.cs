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
}
