namespace Broadside.Layout.Flow;

/// <summary>A page's width and height, portrait as given; <see cref="Landscape"/> swaps them so the long side runs across.</summary>
/// <remarks>ISO 32000-2 §14.11.2: the page's media box, in points. ISO 216 sizes are the millimeter sizes in points.</remarks>
public readonly record struct PageSize
{
    /// <summary>Creates a page size.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <exception cref="ArgumentOutOfRangeException">A side is not positive.</exception>
    public PageSize(Length width, Length height)
    {
        Width = width.ToPoints() > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width), width, "A page side shall be positive.");
        Height = height.ToPoints() > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height), height, "A page side shall be positive.");
    }

    /// <summary>Gets ISO A3, 297 by 420 mm (841.89 by 1190.551 points).</summary>
    public static PageSize A3 { get; } = new(841.89, 1190.551);

    /// <summary>Gets ISO A4, 210 by 297 mm (595.276 by 841.89 points, as <see cref="PageSetup.A4"/>).</summary>
    public static PageSize A4 { get; } = new(595.276, 841.89);

    /// <summary>Gets ISO A5, 148 by 210 mm (419.528 by 595.276 points).</summary>
    public static PageSize A5 { get; } = new(419.528, 595.276);

    /// <summary>Gets US Letter, 612 by 792 points (8.5 by 11 inches).</summary>
    public static PageSize Letter { get; } = new(612, 792);

    /// <summary>Gets US Legal, 612 by 1008 points (8.5 by 14 inches).</summary>
    public static PageSize Legal { get; } = new(612, 1008);

    /// <summary>Gets the width.</summary>
    public Length Width { get; }

    /// <summary>Gets the height.</summary>
    public Length Height { get; }

    /// <summary>Gets this size with the long side across: width and height swapped when the page is taller than wide.</summary>
    public PageSize Landscape => Height > Width ? new(Height, Width) : this;

    /// <summary>Gets this size with the long side down: width and height swapped when the page is wider than tall.</summary>
    public PageSize Portrait => Width > Height ? new(Height, Width) : this;
}
