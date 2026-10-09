namespace Broadside.Content;

/// <summary>What kind of content stream a run, or a nested run inside it, interprets.</summary>
/// <remarks>ISO 32000-2 §7.8.2: pages, forms, patterns, Type 3 glyphs and annotation appearances are all content streams.</remarks>
public enum ContentRunKind
{
    /// <summary>A page's <c>Contents</c> (§7.7.3.3, Table 31).</summary>
    Page,

    /// <summary>A form XObject (§8.10).</summary>
    Form,

    /// <summary>A transparency group XObject (§11.6.6).</summary>
    Group,

    /// <summary>A tiling pattern's cell (§8.7.3).</summary>
    Pattern,

    /// <summary>A Type 3 glyph procedure (§9.6.4).</summary>
    Type3Glyph,

    /// <summary>A soft mask's group (§11.6.5.2).</summary>
    SoftMask,

    /// <summary>An annotation's appearance stream (§12.5.5).</summary>
    Appearance,
}
