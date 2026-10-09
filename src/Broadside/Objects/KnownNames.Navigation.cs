namespace Broadside.Objects;

/// <summary>Names looked up by name trees, number trees, destinations, outlines and actions (issue #70).</summary>
internal static partial class KnownNames
{
    /// <summary><c>/Names</c>, the catalog's name dictionary (§7.7.2, Table 29) and the pairs of a name tree node (§7.9.6, Table 36).</summary>
    public static readonly CosName Names = new("Names");

    /// <summary><c>/Nums</c>, the pairs of a number tree node (§7.9.7, Table 37).</summary>
    public static readonly CosName Nums = new("Nums");

    /// <summary><c>/Limits</c>, the least and greatest keys under a tree node (§7.9.6, Table 36; §7.9.7, Table 37).</summary>
    public static readonly CosName Limits = new("Limits");

    /// <summary><c>/Dests</c>, the named destinations: a catalog dictionary (PDF 1.1) or a name dictionary tree (§7.7.4, §12.3.2.4).</summary>
    public static readonly CosName Dests = new("Dests");

    /// <summary><c>/S</c>, the type of an action (§12.6.2, Table 196).</summary>
    public static readonly CosName S = new("S");

    /// <summary><c>/URI</c>, the URI action type and its URI entry (§12.6.4.8, Table 210).</summary>
    public static readonly CosName Uri = new("URI");

    /// <summary><c>/IsMap</c>, whether a URI action tracks the mouse position (§12.6.4.8, Table 210).</summary>
    public static readonly CosName IsMap = new("IsMap");
}
