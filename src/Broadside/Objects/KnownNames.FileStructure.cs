namespace Broadside.Objects;

/// <summary>The names of the file structure (§7.5): cross-reference streams and object streams.</summary>
internal static partial class KnownNames
{
    /// <summary><c>/XRef</c>, the type of a cross-reference stream (§7.5.8.2, Table 17).</summary>
    public static readonly CosName XRef = new("XRef");

    /// <summary><c>/XRefStm</c>, the hybrid-reference trailer entry locating a cross-reference stream (§7.5.8.4, Table 19).</summary>
    public static readonly CosName XRefStm = new("XRefStm");

    /// <summary><c>/W</c>, the field widths of a cross-reference stream's entries (§7.5.8.2, Table 17).</summary>
    public static readonly CosName W = new("W");

    /// <summary><c>/Index</c>, the subsections of a cross-reference stream (§7.5.8.2, Table 17).</summary>
    public static readonly CosName Index = new("Index");

    /// <summary><c>/ObjStm</c>, the type of an object stream (§7.5.7, Table 16).</summary>
    public static readonly CosName ObjStm = new("ObjStm");

    /// <summary><c>/N</c>, the number of objects in an object stream (§7.5.7, Table 16).</summary>
    public static readonly CosName N = new("N");

    /// <summary><c>/First</c>, the offset of an object stream's first object in its decoded data (§7.5.7, Table 16).</summary>
    public static readonly CosName First = new("First");
}
