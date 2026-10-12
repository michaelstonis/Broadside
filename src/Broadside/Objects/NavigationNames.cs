namespace Broadside.Objects;

/// <summary>Names looked up by name trees, number trees, destinations, outlines and actions (issue #70); kept apart from <see cref="KnownNames"/> so parallel tickets do not collide.</summary>
internal static class NavigationNames
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

    /// <summary><c>/Outlines</c>, the catalog's outline dictionary (§7.7.2, Table 29) and its type (§12.3.3, Table 150).</summary>
    public static readonly CosName Outlines = new("Outlines");

    /// <summary><c>/First</c>, the first child of an outline dictionary or item (§12.3.3, Tables 150 and 151).</summary>
    public static readonly CosName First = new("First");

    /// <summary><c>/Last</c>, the last child of an outline dictionary or item (§12.3.3, Tables 150 and 151).</summary>
    public static readonly CosName Last = new("Last");

    /// <summary><c>/Next</c>, the next outline item at the same level (§12.3.3, Table 151) and the next action (§12.6.2, Table 196).</summary>
    public static readonly CosName Next = new("Next");

    /// <summary><c>/Title</c>, the text of an outline item (§12.3.3, Table 151).</summary>
    public static readonly CosName Title = new("Title");

    /// <summary><c>/Dest</c>, the destination of an outline item (§12.3.3, Table 151).</summary>
    public static readonly CosName Dest = new("Dest");

    /// <summary><c>/A</c>, the action of an outline item (§12.3.3, Table 151).</summary>
    public static readonly CosName A = new("A");

    /// <summary><c>/SE</c>, the structure element of an outline item (§12.3.3, Table 151).</summary>
    public static readonly CosName SE = new("SE");

    /// <summary><c>/C</c>, the text colour of an outline item (§12.3.3, Table 151).</summary>
    public static readonly CosName C = new("C");

    /// <summary><c>/F</c>, the style flags of an outline item (§12.3.3, Tables 151 and 152).</summary>
    public static readonly CosName F = new("F");

    /// <summary><c>/D</c>, the destination of a named destination's dictionary value (§12.3.2.4) and of a go-to action (§12.6.4.2, Table 202).</summary>
    public static readonly CosName D = new("D");

    /// <summary><c>/SD</c>, the structure destination of a go-to action (§12.6.4.2, Table 202).</summary>
    public static readonly CosName SD = new("SD");

    /// <summary><c>/P</c>, the parent of a structure element (§14.7.2, Table 355).</summary>
    public static readonly CosName P = new("P");

    /// <summary><c>/StructElem</c>, the type of a structure element (§14.7.2, Table 355).</summary>
    public static readonly CosName StructElem = new("StructElem");

    /// <summary><c>/GoTo</c>, the go-to action type (§12.6.4.2, Table 202).</summary>
    public static readonly CosName GoTo = new("GoTo");
}
