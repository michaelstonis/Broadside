using Broadside.Graphics;

namespace Broadside.Viewer;

/// <summary>
/// A destination resolved to a place a viewer can show: the page it names, the view it asks for (Table 149) and that view's
/// parameters in the page's default user space. An outline item, a named destination and a link all resolve to one, and
/// <see cref="GetViewport"/> turns it into the scroll position and zoom that show it.
/// </summary>
/// <param name="PageIndex">The zero-based page of this document.</param>
/// <param name="View">The view the destination asks for.</param>
/// <param name="Left">The left coordinate (<c>XYZ</c>, <c>FitV</c>, <c>FitBV</c>, <c>FitR</c>), or <see langword="null"/> for "keep the current value".</param>
/// <param name="Top">The top coordinate (<c>XYZ</c>, <c>FitH</c>, <c>FitBH</c>, <c>FitR</c>), or <see langword="null"/> for "keep the current value".</param>
/// <param name="Right">The right coordinate (<c>FitR</c>), or <see langword="null"/>.</param>
/// <param name="Bottom">The bottom coordinate (<c>FitR</c>), or <see langword="null"/>.</param>
/// <param name="Zoom">The <c>XYZ</c> zoom as a factor of actual size, or <see langword="null"/> (also for 0) to keep the current zoom.</param>
/// <remarks>
/// ISO 32000-2 §12.3.2.2, Table 149, and §12.3.2.3. Spec #33 viewer capability 10. The bounding-box views (<c>FitB</c>, <c>FitBH</c>,
/// <c>FitBV</c>) fit the page's crop box, the region a viewer displays (§14.11.2), not the bounds of its contents.
/// </remarks>
public readonly record struct ViewTarget(int PageIndex, PdfDestinationView View, double? Left, double? Top, double? Right, double? Bottom, double? Zoom)
{
    /// <summary>Resolves a destination of this document: an explicit one, or a named one looked up in the document.</summary>
    /// <param name="destination">The destination, or <see langword="null"/>.</param>
    /// <returns>The target, or <see langword="null"/> when the destination is null, remote, not found, not valid, or its page is not in the page tree.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.3.2. Reads the document (the live views of the destination), so call it where the document may be read:
    /// <see cref="DocumentOutline.LoadAsync"/> and <see cref="FindNamedAsync"/> do. Diagnostics of repairs are recorded on the document.
    /// </remarks>
    public static ViewTarget? Resolve(PdfDestination? destination)
    {
        PdfExplicitDestination? explicitDestination = destination switch
        {
            PdfExplicitDestination { IsRemote: false } local => local,
            PdfNamedDestination { IsRemote: false } named => named.Resolve(),
            _ => null,
        };
        if (explicitDestination is not { IsRemote: false, IsValid: true } || explicitDestination.PageIndex is not int pageIndex)
        {
            return null;
        }

        return new ViewTarget(
            pageIndex,
            explicitDestination.View,
            explicitDestination.Left,
            explicitDestination.Top,
            explicitDestination.Right,
            explicitDestination.Bottom,
            explicitDestination.Zoom);
    }

    /// <summary>Looks a named destination up in a session's document.</summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The destination's name (the <c>Names</c> tree or the catalog's <c>Dests</c> dictionary).</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The target, or <see langword="null"/> when the name is not defined or its destination has no page here.</returns>
    /// <remarks>ISO 32000-2 §12.3.2.3. The read runs under the session's read lease (off the UI thread in background mode).</remarks>
    public static async ValueTask<ViewTarget?> FindNamedAsync(DocumentSession session, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(name);
        return await session.ReadAsync(
            _ => session.Document.GetNamedDestination(name) is { } found ? Resolve(found) : null,
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Works out the viewport that shows the target: its page scrolled to, then the view's zoom (<c>Fit</c> and the bounding-box forms
    /// fit the page, <c>FitH</c> its width, <c>FitV</c> its height, <c>FitR</c> the rectangle, <c>XYZ</c> the given zoom) and its left
    /// and top edge at the viewport's top-left corner when it has them.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="viewport">The current viewport (its size is kept, and its zoom when the view does not set one).</param>
    /// <param name="actualSize">Pixels per point at actual size, which an <c>XYZ</c> zoom multiplies: 1 for 72 DPI pixels, or the display's pixel ratio.</param>
    /// <param name="minZoom">The smallest zoom the viewer allows.</param>
    /// <param name="maxZoom">The largest zoom the viewer allows.</param>
    /// <returns>
    /// The viewport to set (<see cref="ProgressiveZoom.SetViewport"/>), kept inside the content; <paramref name="viewport"/> when the page is not
    /// in the layout. A coordinate the destination leaves null keeps the page's own position along that axis (the page centred across
    /// the strip, the spread's leading edge along it).
    /// </returns>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public Viewport GetViewport(
        PageLayout layout,
        in Viewport viewport,
        double actualSize = 1,
        double minZoom = ProgressiveZoomOptions.DefaultMinZoom,
        double maxZoom = ProgressiveZoomOptions.DefaultMaxZoom)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if ((uint)PageIndex >= (uint)layout.PageCount)
        {
            return viewport;
        }

        double zoom = View switch
        {
            PdfDestinationView.Fit or PdfDestinationView.FitB => layout.GetZoom(ZoomMode.FitPage, viewport.Width, viewport.Height, PageIndex),
            PdfDestinationView.FitH or PdfDestinationView.FitBH => layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, PageIndex),
            PdfDestinationView.FitV or PdfDestinationView.FitBV => FitHeight(layout, viewport),
            PdfDestinationView.FitR => FitRectangle(layout, viewport) ?? viewport.Zoom,
            PdfDestinationView.Xyz when Zoom is > 0 => Zoom.Value * actualSize,
            _ => viewport.Zoom,
        };
        zoom = Math.Clamp(zoom, minZoom, maxZoom);

        Viewport scrolled = layout.ScrollTo(viewport with { Zoom = zoom }, PageIndex);
        if (View == PdfDestinationView.FitR && Rectangle is { } rectangle)
        {
            // Centred: the spec only asks that the rectangle fits.
            LayoutRectangle box = layout.PageToViewport(scrolled, PageIndex, rectangle);
            return layout.Clamp(scrolled with
            {
                X = scrolled.X + box.X + (box.Width / 2) - (scrolled.Width / 2),
                Y = scrolled.Y + box.Y + (box.Height / 2) - (scrolled.Height / 2),
            });
        }

        bool takesLeft = View is PdfDestinationView.Xyz or PdfDestinationView.FitV or PdfDestinationView.FitBV;
        bool takesTop = View is PdfDestinationView.Xyz or PdfDestinationView.FitH or PdfDestinationView.FitBH;
        double? left = takesLeft ? Left : null;
        double? top = takesTop ? Top : null;
        if (left is null && top is null)
        {
            return scrolled;
        }

        PathPoint corner = layout.PageToViewport(scrolled, PageIndex, left ?? 0, top ?? 0);
        return layout.Clamp(scrolled with
        {
            X = left is null ? scrolled.X : scrolled.X + corner.X,
            Y = top is null ? scrolled.Y : scrolled.Y + corner.Y,
        });
    }

    private PdfRectangle? Rectangle => Left is { } l && Bottom is { } b && Right is { } r && Top is { } t ? new PdfRectangle(l, b, r, t) : null;

    // The zoom at which the page's spread, with the padding around it, is as tall as the viewport.
    private double FitHeight(PageLayout layout, in Viewport viewport)
    {
        LayoutRectangle spread = layout.GetSpreadBounds(layout.GetSpread(PageIndex));
        double height = spread.Height + (2 * layout.Padding);
        return height > 0 ? viewport.Height / height : viewport.Zoom;
    }

    private double? FitRectangle(PageLayout layout, in Viewport viewport)
    {
        if (Rectangle is not { } rectangle)
        {
            return null;
        }

        LayoutRectangle size = layout.PageToViewport(viewport with { Zoom = 1 }, PageIndex, rectangle);
        double byWidth = size.Width > 0 ? viewport.Width / size.Width : double.PositiveInfinity;
        double byHeight = size.Height > 0 ? viewport.Height / size.Height : double.PositiveInfinity;
        double zoom = Math.Min(byWidth, byHeight);
        return double.IsFinite(zoom) ? zoom : null;
    }
}
