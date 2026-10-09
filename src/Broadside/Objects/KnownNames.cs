namespace Broadside.Objects;

/// <summary>Names the library itself looks up, created once. Add a name here when code needs it, not before.</summary>
internal static class KnownNames
{
    /// <summary><c>/Length</c>, the stream dictionary entry that gives the extent of the data (ISO 32000-2 §7.3.8.2, Table 5).</summary>
    public static readonly CosName Length = new("Length");

    /// <summary><c>/Type</c>, the entry naming the type of a dictionary.</summary>
    public static readonly CosName Type = new("Type");

    /// <summary><c>/Size</c>, the trailer entry giving the number of cross-reference entries (§7.5.5, Table 15).</summary>
    public static readonly CosName Size = new("Size");

    /// <summary><c>/Prev</c>, the trailer entry giving the offset of the previous cross-reference section (§7.5.5, Table 15).</summary>
    public static readonly CosName Prev = new("Prev");

    /// <summary><c>/Root</c>, the trailer entry referring to the catalog (§7.5.5, Table 15).</summary>
    public static readonly CosName Root = new("Root");

    /// <summary><c>/Catalog</c>, the type of the document catalog (§7.7.2, Table 29).</summary>
    public static readonly CosName Catalog = new("Catalog");

    /// <summary><c>/Version</c>, the catalog entry that overrides the header version when later (§7.7.2, Table 29).</summary>
    public static readonly CosName Version = new("Version");

    /// <summary><c>/Pages</c>, the catalog entry referring to the page tree root, and the type of a page tree node (§7.7.2, §7.7.3.2).</summary>
    public static readonly CosName Pages = new("Pages");

    /// <summary><c>/Page</c>, the type of a page object (§7.7.3.3, Table 31).</summary>
    public static readonly CosName Page = new("Page");

    /// <summary><c>/Kids</c>, the children of a page tree node (§7.7.3.2, Table 30).</summary>
    public static readonly CosName Kids = new("Kids");

    /// <summary><c>/Count</c>, the number of leaf pages under a page tree node (§7.7.3.2, Table 30).</summary>
    public static readonly CosName Count = new("Count");

    /// <summary><c>/Parent</c>, the parent of a page tree node or page object (§7.7.3.2, §7.7.3.3).</summary>
    public static readonly CosName Parent = new("Parent");

    /// <summary><c>/Resources</c>, the inheritable resource dictionary of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName Resources = new("Resources");

    /// <summary><c>/MediaBox</c>, the inheritable media box of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName MediaBox = new("MediaBox");

    /// <summary><c>/CropBox</c>, the inheritable crop box of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName CropBox = new("CropBox");

    /// <summary><c>/BleedBox</c>, the bleed box of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName BleedBox = new("BleedBox");

    /// <summary><c>/TrimBox</c>, the trim box of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName TrimBox = new("TrimBox");

    /// <summary><c>/ArtBox</c>, the art box of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName ArtBox = new("ArtBox");

    /// <summary><c>/Rotate</c>, the inheritable rotation of a page (§7.7.3.3, Table 31).</summary>
    public static readonly CosName Rotate = new("Rotate");

    /// <summary><c>/UserUnit</c>, the size of a default user space unit (§7.7.3.3, Table 31).</summary>
    public static readonly CosName UserUnit = new("UserUnit");

    /// <summary><c>/Info</c>, the trailer's document information dictionary (§7.5.5, Table 15).</summary>
    public static readonly CosName Info = new("Info");

    /// <summary><c>/Encrypt</c>, the trailer's encryption dictionary (§7.5.5, Table 15).</summary>
    public static readonly CosName Encrypt = new("Encrypt");

    /// <summary><c>/ID</c>, the trailer's file identifier (§7.5.5, Table 15).</summary>
    public static readonly CosName ID = new("ID");
}
