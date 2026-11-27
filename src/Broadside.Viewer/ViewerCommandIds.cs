namespace Broadside.Viewer;

/// <summary>The identifiers of the built-in <see cref="ViewerToolbar"/> commands, in their default order.</summary>
/// <remarks>Not a spec feature, except <see cref="PageNumber"/>, which accepts page labels (ISO 32000-2 §12.4.2).</remarks>
public static class ViewerCommandIds
{
    /// <summary>Shows the previous page or spread (a <see cref="ViewerButtonCommand"/>).</summary>
    public const string PagePrevious = "page.previous";

    /// <summary>Shows the next page or spread (a <see cref="ViewerButtonCommand"/>).</summary>
    public const string PageNext = "page.next";

    /// <summary>The page box: shows the current page's label and goes to a typed number or label (a <see cref="ViewerPageCommand"/>).</summary>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public const string PageNumber = "page.number";

    /// <summary>Zooms out by one step (a <see cref="ViewerButtonCommand"/>).</summary>
    public const string ZoomOut = "zoom.out";

    /// <summary>Zooms in by one step (a <see cref="ViewerButtonCommand"/>).</summary>
    public const string ZoomIn = "zoom.in";

    /// <summary>Fit width, fit page, actual size and the zoom presets (a <see cref="ViewerChoiceCommand"/>).</summary>
    public const string ZoomMode = "zoom.mode";

    /// <summary>The page layout: one of the <see cref="PageLayoutMode"/> values (a <see cref="ViewerChoiceCommand"/>).</summary>
    public const string LayoutMode = "layout.mode";

    /// <summary>Turns the view a quarter turn clockwise; the file is not changed (a <see cref="ViewerButtonCommand"/>).</summary>
    public const string Rotate = "rotate";

    /// <summary>Find in the document (a <see cref="ViewerSearchCommand"/>).</summary>
    public const string Search = "search";

    /// <summary>Smart dark mode on or off (a <see cref="ViewerToggleCommand"/>).</summary>
    public const string DarkMode = "darkMode";

    /// <summary>Gets every built-in identifier, in the default order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [PagePrevious, PageNext, PageNumber, ZoomOut, ZoomIn, ZoomMode, LayoutMode, Rotate, Search, DarkMode];
}
