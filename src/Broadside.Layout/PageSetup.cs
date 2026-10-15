namespace Broadside.Layout.Flow;

/// <summary>The four margins of a page, in points, between the media box edges and the content area text flows in.</summary>
/// <param name="Left">The distance from the left edge.</param>
/// <param name="Top">The distance from the top edge.</param>
/// <param name="Right">The distance from the right edge.</param>
/// <param name="Bottom">The distance from the bottom edge.</param>
/// <remarks>ISO 32000-2 §14.11.2 (page boundaries): the margins are measured inside the media box in default user space.</remarks>
public readonly record struct PageMargins(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Creates margins of one size on all four sides.</summary>
    /// <param name="all">The margin on every side, in points.</param>
    public PageMargins(double all)
        : this(all, all, all, all)
    {
    }
}

/// <summary>
/// The size and margins of the pages a <see cref="FlowDocument"/> creates: the media box is <c>[0 0 Width Height]</c> and the
/// content area is the media box less the margins. Immutable; <c>With*</c> returns a changed copy.
/// </summary>
/// <param name="Width">The page width in points.</param>
/// <param name="Height">The page height in points.</param>
/// <param name="Margins">The margins around the content area.</param>
/// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (<c>MediaBox</c>) and §14.11.2 (page boundaries).</remarks>
public readonly record struct PageSetup(double Width, double Height, PageMargins Margins)
{
    /// <summary>US Letter (612 by 792 points) with one-inch (72-point) margins.</summary>
    public static PageSetup Letter => new(612, 792, new PageMargins(72));

    /// <summary>ISO A4 (595.276 by 841.89 points, 210 by 297 mm) with 2 cm (56.693-point) margins.</summary>
    public static PageSetup A4 => new(595.276, 841.89, new PageMargins(56.693));

    /// <summary>Returns this page size with other margins.</summary>
    /// <param name="margins">The new margins.</param>
    /// <returns>The changed copy.</returns>
    public PageSetup WithMargins(PageMargins margins) => this with { Margins = margins };

    /// <summary>Returns this page setup with another size, keeping the margins.</summary>
    /// <param name="width">The new width in points.</param>
    /// <param name="height">The new height in points.</param>
    /// <returns>The changed copy.</returns>
    public PageSetup WithSize(double width, double height) => this with { Width = width, Height = height };

    /// <summary>The width of the content area: the page width less the left and right margins.</summary>
    public double ContentWidth => Width - Margins.Left - Margins.Right;

    /// <summary>The height of the content area: the page height less the top and bottom margins.</summary>
    public double ContentHeight => Height - Margins.Top - Margins.Bottom;

    /// <summary>Throws when a dimension or margin is not finite, a margin is negative, or the content area is empty.</summary>
    internal void Validate(string parameterName)
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
        {
            throw new ArgumentException($"The page size shall be positive and finite; it is {Width} by {Height}.", parameterName);
        }

        if (!double.IsFinite(Margins.Left) || !double.IsFinite(Margins.Top) || !double.IsFinite(Margins.Right) || !double.IsFinite(Margins.Bottom)
            || Margins.Left < 0 || Margins.Top < 0 || Margins.Right < 0 || Margins.Bottom < 0)
        {
            throw new ArgumentException($"The margins shall be finite and not negative; they are {Margins}.", parameterName);
        }

        if (ContentWidth <= 0 || ContentHeight <= 0)
        {
            throw new ArgumentException($"The margins leave no content area on a {Width} by {Height} page.", parameterName);
        }
    }
}
