namespace Broadside.Viewer;

/// <summary>Which built-in commands a <see cref="ViewerToolbar"/> starts with, in what order, its zoom presets and its layout.</summary>
/// <remarks>
/// Not a spec feature, except <see cref="HonorHideToolbar"/> (ISO 32000-2 §12.2, Table 147, <c>HideToolbar</c>). Mutable and fluent
/// like the other options objects: <c>With*</c> sets a value. A toolbar reads the options when it is created.
/// </remarks>
public sealed class ViewerToolbarOptions
{
    private IReadOnlyList<string> _items = ViewerCommandIds.All;
    private IReadOnlyList<double> _zoomPresets = DefaultZoomPresets;

    /// <summary>Gets the zoom presets a toolbar offers unless told otherwise: 50 %, 75 %, 100 %, 125 %, 150 %, 200 % and 400 %.</summary>
    public static IReadOnlyList<double> DefaultZoomPresets { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2, 4];

    /// <summary>Gets the built-in commands, in order (<see cref="ViewerCommandIds.All"/> by default).</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets the zoom presets of the zoom picker, as factors of actual size (<see cref="DefaultZoomPresets"/> by default).</summary>
    public IReadOnlyList<double> ZoomPresets => _zoomPresets;

    /// <summary>
    /// Gets or sets whether the toolbar lays out for a narrow (phone-width) window: only the page buttons, the page box and find stay in
    /// the bar, and the other built-in commands start in the overflow menu. <see langword="false"/> by default.
    /// </summary>
    public bool Compact { get; set; }

    /// <summary>
    /// Gets or sets whether a document that asks the viewer to hide its tool bars (<c>HideToolbar</c>) starts with the toolbar hidden
    /// (<see cref="ViewerToolbar.IsVisible"/>). <see langword="true"/> by default.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool HonorHideToolbar { get; set; } = true;

    /// <summary>Sets <see cref="Items"/>: the built-in commands to show, in order; the others are left out.</summary>
    /// <param name="ids">Identifiers from <see cref="ViewerCommandIds"/> (<see cref="ViewerCommandIds.All"/> and <see cref="ViewerCommandIds.Editing"/>), each once.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">An identifier is not a built-in one, or appears twice.</exception>
    public ViewerToolbarOptions WithItems(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!ViewerCommandIds.All.Contains(id, StringComparer.Ordinal) && !ViewerCommandIds.Editing.Contains(id, StringComparer.Ordinal))
            {
                throw new ArgumentException($"\"{id}\" is not a built-in toolbar command; add an app's own with ViewerToolbar.Insert.", nameof(ids));
            }

            if (!seen.Add(id))
            {
                throw new ArgumentException($"\"{id}\" appears twice.", nameof(ids));
            }
        }

        _items = [.. ids];
        return this;
    }

    /// <summary>Sets <see cref="ZoomPresets"/>.</summary>
    /// <param name="zooms">The zooms, as factors of actual size (1 = 100 %), in the order offered.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A zoom is not a finite number above 0.</exception>
    public ViewerToolbarOptions WithZoomPresets(params double[] zooms)
    {
        ArgumentNullException.ThrowIfNull(zooms);
        foreach (double zoom in zooms)
        {
            if (!double.IsFinite(zoom) || zoom <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(zooms), zoom, "A zoom is a finite factor above 0.");
            }
        }

        _zoomPresets = [.. zooms];
        return this;
    }

    /// <summary>Sets <see cref="Compact"/>.</summary>
    /// <param name="compact">Whether to lay out for a narrow window.</param>
    /// <returns>These options.</returns>
    public ViewerToolbarOptions WithCompact(bool compact)
    {
        Compact = compact;
        return this;
    }

    /// <summary>Sets <see cref="HonorHideToolbar"/>.</summary>
    /// <param name="honor">Whether a document's <c>HideToolbar</c> hides the toolbar.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public ViewerToolbarOptions WithHonorHideToolbar(bool honor)
    {
        HonorHideToolbar = honor;
        return this;
    }
}
