namespace Broadside.Rendering;

/// <summary>What a <see cref="HitTestIndex"/> holds: glyphs, images, paths and links; combined to say which kinds a query looks at.</summary>
/// <remarks>Viewer capability 4 (hit-testing for text selection and link activation); ISO 32000-2 §9.4.4, §8.9, §8.5 and §12.5.6.5.</remarks>
[Flags]
public enum HitTestKinds
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Shown character codes and their boxes (<see cref="HitTestIndex.GetGlyph"/>).</summary>
    Glyphs = 1,

    /// <summary>Painted images and stencil masks (<see cref="HitTestIndex.Images"/>).</summary>
    Images = 2,

    /// <summary>Filled and stroked paths (<see cref="HitTestIndex.Paths"/>).</summary>
    Paths = 4,

    /// <summary>Link annotations (<see cref="HitTestIndex.Links"/>).</summary>
    Links = 8,

    /// <summary>Every kind.</summary>
    All = Glyphs | Images | Paths | Links,
}

/// <summary>One match of a <see cref="HitTestIndex"/> query: its kind and its index among the index's items of that kind.</summary>
/// <param name="Kind">
/// The kind (one flag): <see cref="HitTestKinds.Glyphs"/> indexes <see cref="HitTestIndex.GetGlyph"/>, <see cref="HitTestKinds.Images"/>
/// <see cref="HitTestIndex.Images"/>, <see cref="HitTestKinds.Paths"/> <see cref="HitTestIndex.Paths"/> and
/// <see cref="HitTestKinds.Links"/> <see cref="HitTestIndex.Links"/>.
/// </param>
/// <param name="Index">The index among the items of that kind; for glyphs, the content order.</param>
/// <remarks>Viewer capability 4.</remarks>
public readonly record struct HitTestItem(HitTestKinds Kind, int Index);
