using System.Diagnostics.CodeAnalysis;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The viewer's model of the annotations, links and form fields on its pages, independent of any UI framework: hit-testing, hover,
/// selection and keyboard focus with page rectangles for the shell to draw; link activation (raised as
/// <see cref="ActionRequested"/>, never performed); notes and popups; field editing through the form API; and annotation creation,
/// moving and deletion through the annotation API. Every edit redraws only the tiles it touches (<see cref="TileScheduler.Invalidate(int, PdfRectangle, DisplayLayers)"/>).
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capability 6; ISO 32000-2 §12.5 (annotations), §12.6 (actions), §12.7 (interactive forms). The shell maps pointer
/// positions with <see cref="PageLayout.TryGetPageAt"/>, sends <see cref="PointerDown"/>, <see cref="PointerMove"/> and
/// <see cref="PointerUp"/> before its own panning or text selection (a press that returns <see langword="true"/> is the layer's), and
/// draws hover, selection and focus from <see cref="GetScreenRectangle"/> when <see cref="Changed"/> fires.
/// </para>
/// <para>
/// Like the document, the layer is single-threaded: call it from the shell's UI thread. A page takes part once
/// <see cref="LoadPageAsync"/> has loaded it. Pointer moves allocate nothing.
/// </para>
/// </remarks>
public sealed class AnnotationLayer
{
    /// <summary>How far, in default user space units, a press must travel on a selected annotation before it drags it.</summary>
    public const double DragThreshold = 2;

    private static readonly CosName TabsKey = new("Tabs");
    private static readonly CosName RowOrder = new("R");
    private static readonly CosName ColumnOrder = new("C");

    private readonly TileScheduler? _scheduler;
    private readonly AnnotationPage?[] _pages;
    private AnnotationTarget? _pressed;
    private PagePoint _pressPoint;
    private bool _dragging;
    private double _dragX;
    private double _dragY;

    /// <summary>Initializes a new instance of the <see cref="AnnotationLayer"/> class.</summary>
    /// <param name="session">The session whose document the layer works on.</param>
    /// <param name="scheduler">The scheduler whose tiles edits invalidate, or <see langword="null"/> (then only <see cref="Invalidated"/> says so).</param>
    /// <remarks>Spec #33 viewer capability 6.</remarks>
    public AnnotationLayer(DocumentSession session, TileScheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        _scheduler = scheduler;
        _pages = new AnnotationPage?[session.PageCount];
    }

    /// <summary>Raised when the hovered, selected or focused annotation changes, or a dragged annotation moves: redraw the chrome.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised for each action of an activated link or widget, in execution order. The viewer performs none: go-tos are navigated by the
    /// shell, URIs and other files opened only after asking the user, and scripts never run (<see cref="ViewerActionEventArgs.IsScript"/>).
    /// </summary>
    public event EventHandler<ViewerActionEventArgs>? ActionRequested;

    /// <summary>Raised after an edit changed what part of a page draws (also passed to the scheduler, when there is one).</summary>
    public event EventHandler<PageInvalidatedEventArgs>? Invalidated;

    /// <summary>Gets the session.</summary>
    public DocumentSession Session { get; }

    /// <summary>Gets the annotation under the pointer, or <see langword="null"/>.</summary>
    public AnnotationTarget? Hovered { get; private set; }

    /// <summary>Gets the selected annotation (one of role <see cref="AnnotationRole.Annotation"/>), or <see langword="null"/>.</summary>
    public AnnotationTarget? Selected { get; private set; }

    /// <summary>Gets the annotation with keyboard focus (any role), or <see langword="null"/>.</summary>
    public AnnotationTarget? Focused { get; private set; }

    /// <summary>Gets the field of the focused widget, or <see langword="null"/>: the shell opens its editor over the widget.</summary>
    public PdfTerminalField? FocusedField => Focused is { } focused && TryGet(focused, out Entry entry) && entry.Annotation is PdfWidgetAnnotation widget ? widget.Field : null;

    /// <summary>Gets whether a press is dragging the selected annotation.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Loads (once) a page's annotations into the layer.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="cancellationToken">Cancels the load (a remote page's fetch).</param>
    /// <returns>How many annotations of the page take part (see <see cref="AnnotationTarget.Index"/>).</returns>
    public async ValueTask<int> LoadPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pages.Length);
        if (_pages[pageIndex] is { } loaded)
        {
            return loaded.Count;
        }

        PdfPage page = await Session.LoadPageAsync(pageIndex, cancellationToken).ConfigureAwait(true);
        AnnotationPage model = _pages[pageIndex] ??= AnnotationPage.Create(Session.Document, page);
        return model.Count;
    }

    /// <summary>Gets how many annotations of a page take part: 0 until <see cref="LoadPageAsync"/> loaded it.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The count.</returns>
    public int GetCount(int pageIndex) => (uint)pageIndex < (uint)_pages.Length && _pages[pageIndex] is { } page ? page.Count : 0;

    /// <summary>Gets an annotation.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The annotation's view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    public PdfAnnotation GetAnnotation(AnnotationTarget target) => Get(target).Annotation;

    /// <summary>Gets what an annotation does when activated.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    public AnnotationRole GetRole(AnnotationTarget target) => Get(target).Role;

    /// <summary>Gets an annotation's rectangle (<c>Rect</c>) in its page's default user space.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public PdfRectangle GetRectangle(AnnotationTarget target) => Get(target).Rect;

    /// <summary>
    /// Gets where to draw an annotation's hover, selection or focus chrome, in viewport pixels: its rectangle, moved along while it is
    /// dragged.
    /// </summary>
    /// <param name="target">The annotation.</param>
    /// <param name="layout">The layout the viewport looks at.</param>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The rectangle in viewport pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    public LayoutRectangle GetScreenRectangle(AnnotationTarget target, PageLayout layout, in Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(layout);
        PdfRectangle rect = Get(target).Rect;
        if (_dragging && Selected == target)
        {
            rect = new PdfRectangle(rect.Left + _dragX, rect.Bottom + _dragY, rect.Right + _dragX, rect.Top + _dragY);
        }

        return layout.PageToViewport(viewport, target.PageIndex, rect);
    }

    /// <summary>
    /// Finds the topmost annotation at a point: links by their activation region (<c>QuadPoints</c>, else <c>Rect</c>), others by
    /// <c>Rect</c>, edges included.
    /// </summary>
    /// <param name="point">The point, from <see cref="PageLayout.TryGetPageAt"/>.</param>
    /// <returns>The annotation, or <see langword="null"/> (also when the page is not loaded).</returns>
    /// <remarks>ISO 32000-2 §12.5.2 and §12.5.6.5 (Table 176, QuadPoints). Allocates nothing.</remarks>
    public AnnotationTarget? HitTest(in PagePoint point)
    {
        if ((uint)point.PageIndex >= (uint)_pages.Length || _pages[point.PageIndex] is not { } page)
        {
            return null;
        }

        int index = page.HitTest(point.X, point.Y);
        return index < 0 ? null : new AnnotationTarget(point.PageIndex, index);
    }

    // ---- Pointer ----

    /// <summary>
    /// A press: on an annotation, selects it (and may start dragging it); on a widget, focuses its field. Links and widgets activate on
    /// <see cref="PointerUp"/> over the same annotation.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>Whether the press is the layer's (the shell then neither pans nor selects text).</returns>
    public bool PointerDown(in PagePoint point)
    {
        _dragging = false;
        _dragX = _dragY = 0;
        _pressPoint = point;
        _pressed = HitTest(point);
        if (_pressed is not { } target)
        {
            Select(null);
            return false;
        }

        switch (Get(target).Role)
        {
            case AnnotationRole.Annotation:
                Select(target);
                Focus(target);
                break;
            case AnnotationRole.Field:
                Select(null);
                Focus(target);
                break;
            default:
                Select(null);
                break;
        }

        return true;
    }

    /// <summary>
    /// A pointer move: drags the selected annotation while a press on it lasts, otherwise updates <see cref="Hovered"/>.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The hovered annotation (the shell picks its cursor from <see cref="GetRole"/>), or <see langword="null"/>.</returns>
    /// <remarks>Allocates nothing.</remarks>
    public AnnotationTarget? PointerMove(in PagePoint point)
    {
        if (_pressed is { } pressed && Selected == pressed && point.PageIndex == pressed.PageIndex && CanEdit(Get(pressed)))
        {
            double dx = point.X - _pressPoint.X;
            double dy = point.Y - _pressPoint.Y;
            if (_dragging || Math.Abs(dx) >= DragThreshold || Math.Abs(dy) >= DragThreshold)
            {
                _dragging = true;
                _dragX = dx;
                _dragY = dy;
                Changed?.Invoke(this, EventArgs.Empty);
                return pressed;
            }
        }

        AnnotationTarget? hovered = HitTest(point);
        if (hovered != Hovered)
        {
            Hovered = hovered;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return hovered;
    }

    /// <summary>
    /// A release: ends a drag by moving the annotation (<see cref="PdfAnnotation.Move"/>), or activates the pressed link or widget when
    /// released over it (<see cref="Activate"/>).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>Whether the press was the layer's.</returns>
    public bool PointerUp(in PagePoint point)
    {
        AnnotationTarget? pressed = _pressed;
        _pressed = null;
        if (pressed is not { } target)
        {
            return false;
        }

        if (_dragging)
        {
            _dragging = false;
            (double dx, double dy) = point.PageIndex == target.PageIndex ? (point.X - _pressPoint.X, point.Y - _pressPoint.Y) : (_dragX, _dragY);
            _dragX = _dragY = 0;
            Move(target, dx, dy);
            return true;
        }

        if (HitTest(point) == target && Get(target).Role != AnnotationRole.Annotation)
        {
            Activate(target);
        }

        return true;
    }

    /// <summary>Abandons a press: a drag in progress snaps back.</summary>
    public void PointerCancel()
    {
        _pressed = null;
        if (_dragging)
        {
            _dragging = false;
            _dragX = _dragY = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Selection, focus and activation ----

    /// <summary>Selects an annotation, or clears the selection.</summary>
    /// <param name="target">The annotation, or <see langword="null"/>.</param>
    public void Select(AnnotationTarget? target)
    {
        if (target is { } t)
        {
            _ = Get(t);
        }

        if (Selected != target)
        {
            Selected = target;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gives an annotation keyboard focus, or clears it.</summary>
    /// <param name="target">The annotation, or <see langword="null"/>.</param>
    public void Focus(AnnotationTarget? target)
    {
        if (target is { } t)
        {
            _ = Get(t);
        }

        if (Focused != target)
        {
            Focused = target;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Moves keyboard focus to the next annotation in tab order (Tab): within a page in its <c>Tabs</c> order, then on to the next loaded
    /// page. The accessibility seam: a screen-reader shell follows <see cref="Focused"/> and reads <see cref="GetAccessibleName"/>.
    /// </summary>
    /// <returns>Whether focus moved (false past the last annotation of the last loaded page; focus is then cleared).</returns>
    /// <remarks>ISO 32000-2 §12.5.1 and Table 31 (<c>Tabs</c>: R row order, C column order, A annotation order; S structure order
    /// falls back to annotation order).</remarks>
    public bool FocusNext() => MoveFocus(forward: true);

    /// <summary>Moves keyboard focus to the previous annotation in tab order (Shift+Tab), as <see cref="FocusNext"/> describes.</summary>
    /// <returns>Whether focus moved.</returns>
    public bool FocusPrevious() => MoveFocus(forward: false);

    /// <summary>Lists a loaded page's annotations in tab order.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="destination">Receives the annotations.</param>
    /// <returns>How many there are; when more than fit, call again with a larger span.</returns>
    /// <remarks>ISO 32000-2 §12.5.1 and Table 31 (<c>Tabs</c>).</remarks>
    public int GetFocusOrder(int pageIndex, Span<AnnotationTarget> destination)
    {
        if ((uint)pageIndex >= (uint)_pages.Length || _pages[pageIndex] is not { } page)
        {
            return 0;
        }

        for (int i = 0; i < page.Order.Length && i < destination.Length; i++)
        {
            destination[i] = new AnnotationTarget(pageIndex, page.Order[i]);
        }

        return page.Order.Length;
    }

    /// <summary>
    /// Gets a short text naming an annotation for assistive technology: a field's alternate name (<c>TU</c>) or name, else the
    /// annotation's <c>Contents</c>, else a link's URI, else its subtype.
    /// </summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The name.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1 (Table 226, TU), §12.5.2 (Contents), §14.9.3 (alternate descriptions).</remarks>
    public string GetAccessibleName(AnnotationTarget target)
    {
        PdfAnnotation annotation = Get(target).Annotation;
        if (annotation is PdfWidgetAnnotation { Field: { } field })
        {
            return field.AlternateName ?? field.PartialName;
        }

        if (!string.IsNullOrEmpty(annotation.Contents))
        {
            return annotation.Contents;
        }

        if (annotation is PdfLinkAnnotation { Action: PdfUriAction { Uri: { } uri } })
        {
            return uri;
        }

        return annotation.Subtype?.Value ?? annotation.Kind.ToString();
    }

    /// <summary>
    /// Activates an annotation (a click, or Enter on the focused one): a link raises <see cref="ActionRequested"/> for its destination
    /// or each action of its action tree; a widget gets focus, a check box toggles and a radio button turns on (unless read-only), and
    /// its own action, if any, is raised; any other annotation is selected.
    /// </summary>
    /// <param name="target">The annotation.</param>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.5 (Dest, A), §12.6.2 (Next), §12.7.5.2.3-12.7.5.2.4 (check boxes, radio buttons).</remarks>
    public void Activate(AnnotationTarget target)
    {
        Entry entry = Get(target);
        switch (entry.Annotation)
        {
            case PdfLinkAnnotation link:
                Focus(target);
                if (link.Action is { } action)
                {
                    RaiseActions(link, action);
                }
                else if (link.Destination is { } destination)
                {
                    ActionRequested?.Invoke(this, new ViewerActionEventArgs(link, null, Resolve(destination)));
                }

                break;
            case PdfWidgetAnnotation widget:
                Focus(target);
                if (widget.Field is PdfToggleButtonField { IsReadOnly: false } toggle)
                {
                    Toggle(toggle, widget);
                }

                if (widget.Action is { } widgetAction)
                {
                    RaiseActions(widget, widgetAction);
                }

                break;
            default:
                Select(target);
                Focus(target);
                break;
        }
    }

    /// <summary>
    /// Works out the viewport that shows a destination: its page scrolled to, then its left and top edge (<c>XYZ</c>, <c>FitH</c>,
    /// <c>FitV</c>, <c>FitR</c> and the bounding-box forms) at the viewport's top-left corner when it has them. The zoom is kept.
    /// </summary>
    /// <param name="destination">The destination, from <see cref="ViewerActionEventArgs.Destination"/>.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="viewport">The current viewport.</param>
    /// <returns>The viewport to set (<see cref="ProgressiveZoom.SetViewport"/>); <paramref name="viewport"/> when the destination has no page here.</returns>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public static Viewport GetDestinationViewport(PdfExplicitDestination destination, PageLayout layout, in Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(layout);
        if (destination.PageIndex is not int pageIndex || pageIndex >= layout.PageCount)
        {
            return viewport;
        }

        Viewport scrolled = layout.ScrollTo(viewport, pageIndex);
        double? left = destination.Left;
        double? top = destination.Top;
        if (left is null && top is null)
        {
            return scrolled;
        }

        PathPoint corner = layout.PageToViewport(scrolled, pageIndex, left ?? 0, top ?? 0);
        return layout.Clamp(scrolled with
        {
            X = left is null ? scrolled.X : scrolled.X + corner.X,
            Y = top is null ? scrolled.Y : scrolled.Y + corner.Y,
        });
    }

    /// <summary>Gets an annotation's note: its text, author, popup and replies.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The note.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target is not on a loaded page.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.2 (Table 172) and §12.5.6.14.</remarks>
    public AnnotationNote GetNote(AnnotationTarget target)
    {
        PdfAnnotation annotation = Get(target).Annotation;
        if (annotation is not PdfMarkupAnnotation markup)
        {
            return new AnnotationNote(annotation, annotation.Contents, null, null, null, annotation.ModifiedText, null, false, []);
        }

        List<PdfMarkupAnnotation> replies = [];
        if (annotation.Page is { } page)
        {
            foreach (PdfAnnotation other in page.Annotations)
            {
                if (other is PdfMarkupAnnotation { ReplyType: PdfReplyType.Reply, InReplyTo: { } parent } reply
                    && ReferenceEquals(parent.Dictionary, annotation.Dictionary))
                {
                    replies.Add(reply);
                }
            }
        }

        PdfPopupAnnotation? popup = markup.Popup;
        return new AnnotationNote(
            annotation,
            annotation.Contents,
            markup.RichText,
            markup.Title,
            markup.Subject,
            annotation.ModifiedText,
            popup?.Rect,
            popup?.IsOpen ?? false,
            replies);
    }

    // ---- Form fields ----

    /// <summary>Sets the focused text field's value and redraws its widgets.</summary>
    /// <param name="value">The text, or <see langword="null"/> to clear it.</param>
    /// <returns>Whether a writable text field has focus (nothing changes otherwise).</returns>
    /// <remarks>ISO 32000-2 §12.7.5.3, through <see cref="PdfTextField.SetValue"/> (which regenerates the appearances).</remarks>
    public bool SetFieldText(string? value)
    {
        if (FocusedField is not PdfTextField { IsReadOnly: false } field)
        {
            return false;
        }

        field.SetValue(value);
        InvalidateField(field);
        return true;
    }

    /// <summary>Sets the focused choice field's value (an option's export value or text; any text in an editable combo box).</summary>
    /// <param name="value">The value, or <see langword="null"/> to select nothing.</param>
    /// <returns>Whether a writable choice field has focus.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.4, through <see cref="PdfChoiceField.SetValue"/>.</remarks>
    public bool SetFieldChoice(string? value)
    {
        if (FocusedField is not PdfChoiceField { IsReadOnly: false } field)
        {
            return false;
        }

        field.SetValue(value);
        InvalidateField(field);
        return true;
    }

    /// <summary>Selects options of the focused choice field by index (several in a multi-select list box).</summary>
    /// <param name="indices">The option indices.</param>
    /// <returns>Whether a writable choice field has focus.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.4, through <see cref="PdfChoiceField.SetSelectedIndices"/>.</remarks>
    public bool SetFieldSelection(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        if (FocusedField is not PdfChoiceField { IsReadOnly: false } field)
        {
            return false;
        }

        field.SetSelectedIndices(indices);
        InvalidateField(field);
        return true;
    }

    /// <summary>Toggles the focused check box, or turns on the focused radio button (Space).</summary>
    /// <returns>Whether a writable check box or radio button has focus.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.2.3 and §12.7.5.2.4.</remarks>
    public bool ToggleField()
    {
        if (Focused is not { } focused || Get(focused).Annotation is not PdfWidgetAnnotation { Field: PdfToggleButtonField { IsReadOnly: false } field } widget)
        {
            return false;
        }

        Toggle(field, widget);
        return true;
    }

    // ---- Annotation editing ----

    /// <summary>
    /// Adds an annotation to a loaded page through the annotation API (for example
    /// <c>layer.Add(0, page =&gt; page.AddSquareAnnotation(rect, options))</c>), redraws its area and selects it.
    /// </summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="create">Creates the annotation on the page it is given.</param>
    /// <returns>The new annotation, or <see langword="null"/> when it does not take part (a popup, or hidden).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page is not loaded.</exception>
    /// <remarks>ISO 32000-2 §12.5; spec #33 viewer capability 6.</remarks>
    public AnnotationTarget? Add(int pageIndex, Func<PdfPage, PdfAnnotation> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        PdfPage page = PageOf(pageIndex);
        PdfAnnotation annotation = create(page);
        Reload(pageIndex, page);
        InvalidateAnnotation(pageIndex, annotation, annotation.Rect);
        AnnotationTarget? added = Find(pageIndex, annotation.Dictionary);
        Select(added);
        return added;
    }

    /// <summary>Moves an annotation (not a widget or a locked one) and redraws where it was and where it is.</summary>
    /// <param name="target">The annotation.</param>
    /// <param name="dx">The horizontal distance in default user space.</param>
    /// <param name="dy">The vertical distance in default user space.</param>
    /// <returns>Whether it moved.</returns>
    /// <remarks>ISO 32000-2 §12.5.2, through <see cref="PdfAnnotation.Move"/> (the appearance is kept).</remarks>
    public bool Move(AnnotationTarget target, double dx, double dy) =>
        Edit(target, annotation => annotation.Move(dx, dy), regenerateAppearance: false);

    /// <summary>
    /// Edits an annotation (not a widget or a locked one) through its setters and redraws it; the appearance is regenerated after a
    /// visual edit (colour, border width, interior colour) unless <paramref name="regenerateAppearance"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="target">The annotation.</param>
    /// <param name="edit">The edit.</param>
    /// <param name="regenerateAppearance">Whether to call <see cref="PdfAnnotation.RegenerateAppearance"/> afterwards.</param>
    /// <returns>Whether the annotation could be edited.</returns>
    /// <remarks>ISO 32000-2 §12.5.2 and §12.5.5.</remarks>
    public bool Edit(AnnotationTarget target, Action<PdfAnnotation> edit, bool regenerateAppearance = true)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Entry entry = Get(target);
        if (!CanEdit(entry))
        {
            return false;
        }

        PdfAnnotation annotation = entry.Annotation;
        PdfRectangle before = entry.Rect;
        edit(annotation);
        if (regenerateAppearance)
        {
            annotation.RegenerateAppearance();
        }

        Reload(target.PageIndex, PageOf(target.PageIndex));
        PdfRectangle after = annotation.Rect;
        InvalidateAnnotation(target.PageIndex, annotation, Union(before, after));
        return true;
    }

    /// <summary>Removes an annotation (not a widget or a locked one), with its popup and replies, and redraws the page's annotations.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>Whether it was removed.</returns>
    /// <remarks>ISO 32000-2 §12.5, through <see cref="PdfPage.RemoveAnnotation"/>.</remarks>
    public bool Remove(AnnotationTarget target)
    {
        Entry entry = Get(target);
        if (!CanEdit(entry))
        {
            return false;
        }

        PdfPage page = PageOf(target.PageIndex);
        page.RemoveAnnotation(entry.Annotation);
        Reload(target.PageIndex, page);
        Invalidate(new PageInvalidatedEventArgs(target.PageIndex, null, DisplayLayers.Annotations));
        return true;
    }

    // ---- Internals ----

    private static bool CanEdit(in Entry entry) =>
        entry.Role != AnnotationRole.Field && (entry.Annotation.Flags & PdfAnnotationFlags.Locked) == 0;

    private static PdfRectangle Union(PdfRectangle a, PdfRectangle b) =>
        new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));

    private static PdfExplicitDestination? Resolve(PdfDestination? destination) => destination switch
    {
        PdfExplicitDestination { IsRemote: false } explicitDestination => explicitDestination,
        PdfNamedDestination named => named.Resolve(),
        _ => null,
    };

    private void RaiseActions(PdfAnnotation annotation, PdfAction action)
    {
        foreach (PdfAction step in action.EnumerateActionTree())
        {
            PdfExplicitDestination? destination = step is PdfGoToAction goTo ? Resolve(goTo.Destination) : null;
            ActionRequested?.Invoke(this, new ViewerActionEventArgs(annotation, step, destination));
        }
    }

    private void Toggle(PdfToggleButtonField field, PdfWidgetAnnotation widget)
    {
        if (field is PdfCheckBoxField checkBox && checkBox.IsChecked)
        {
            checkBox.SetChecked(false);
        }
        else
        {
            field.Select(widget);
        }

        InvalidateField(field);
    }

    private void InvalidateField(PdfTerminalField field)
    {
        foreach (PdfWidgetAnnotation widget in field.Widgets)
        {
            if (widget.Page is { } page && Session.Document.Pages.IndexOf(page) is int index and >= 0)
            {
                InvalidateAnnotation(index, widget, widget.Rect, DisplayLayers.FormFields);
            }
        }
    }

    private void InvalidateAnnotation(int pageIndex, PdfAnnotation annotation, PdfRectangle area, DisplayLayers layers = DisplayLayers.Annotations)
    {
        // NoZoom and NoRotate appearances leave their rectangle (ISO 32000-2 §12.5.3): the whole page's layer redraws.
        bool placedElsewhere = (annotation.Flags & (PdfAnnotationFlags.NoZoom | PdfAnnotationFlags.NoRotate)) != 0 || annotation.Kind == PdfAnnotationKind.Text;
        Invalidate(new PageInvalidatedEventArgs(pageIndex, placedElsewhere ? null : area, layers));
    }

    private void Invalidate(PageInvalidatedEventArgs invalidation)
    {
        if (_scheduler is not null)
        {
            if (invalidation.Area is { } area)
            {
                _scheduler.Invalidate(invalidation.PageIndex, area, invalidation.Layers);
            }
            else
            {
                _scheduler.Invalidate(invalidation.PageIndex, invalidation.Layers);
            }
        }

        Invalidated?.Invoke(this, invalidation);
    }

    private bool MoveFocus(bool forward)
    {
        int pageIndex = Focused?.PageIndex ?? (forward ? -1 : _pages.Length);
        int position = -1;
        if (Focused is { } focused && _pages[focused.PageIndex] is { } current)
        {
            position = Array.IndexOf(current.Order, focused.Index);
            int next = position + (forward ? 1 : -1);
            if (next >= 0 && next < current.Order.Length)
            {
                Focus(new AnnotationTarget(focused.PageIndex, current.Order[next]));
                return true;
            }
        }

        for (int p = pageIndex + (forward ? 1 : -1); p >= 0 && p < _pages.Length; p += forward ? 1 : -1)
        {
            if (_pages[p] is { Order.Length: > 0 } page)
            {
                Focus(new AnnotationTarget(p, page.Order[forward ? 0 : page.Order.Length - 1]));
                return true;
            }
        }

        Focus(null);
        return false;
    }

    private PdfPage PageOf(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pages.Length);
        if (_pages[pageIndex] is null)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The page is not loaded (LoadPageAsync).");
        }

        return Session.Document.Pages[pageIndex];
    }

    private void Reload(int pageIndex, PdfPage page)
    {
        AnnotationPage old = _pages[pageIndex]!;
        AnnotationPage model = AnnotationPage.Create(Session.Document, page);
        _pages[pageIndex] = model;
        Hovered = Remap(Hovered);
        Selected = Remap(Selected);
        Focused = Remap(Focused);
        _pressed = null;
        Changed?.Invoke(this, EventArgs.Empty);

        AnnotationTarget? Remap(AnnotationTarget? target) =>
            target is { } t && t.PageIndex == pageIndex
                ? Find(pageIndex, old.Entries[t.Index].Annotation.Dictionary)
                : target;
    }

    private AnnotationTarget? Find(int pageIndex, CosDictionary dictionary)
    {
        AnnotationPage page = _pages[pageIndex]!;
        for (int i = 0; i < page.Count; i++)
        {
            if (ReferenceEquals(page.Entries[i].Annotation.Dictionary, dictionary))
            {
                return new AnnotationTarget(pageIndex, i);
            }
        }

        return null;
    }

    private Entry Get(AnnotationTarget target)
    {
        if (!TryGet(target, out Entry entry))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "The annotation is not on a loaded page.");
        }

        return entry;
    }

    private bool TryGet(AnnotationTarget target, out Entry entry)
    {
        if ((uint)target.PageIndex < (uint)_pages.Length && _pages[target.PageIndex] is { } page && (uint)target.Index < (uint)page.Count)
        {
            entry = page.Entries[target.Index];
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>One annotation of a page model: its view, rectangle, role, and activation quadrilaterals for a link.</summary>
    private readonly record struct Entry(PdfAnnotation Annotation, PdfRectangle Rect, AnnotationRole Role, int QuadStart, int QuadCount, int AnnotsIndex);

    /// <summary>A page's annotations in drawing order, their link quadrilaterals and their tab order.</summary>
    private sealed class AnnotationPage(Entry[] entries, PdfQuadrilateral[] quads, int[] order)
    {
        public Entry[] Entries { get; } = entries;

        public int Count => Entries.Length;

        public int[] Order { get; } = order;

        public static AnnotationPage Create(PdfDocument document, PdfPage page)
        {
            IReadOnlyList<PdfAnnotation> annotations = page.Annotations;
            PdfOptionalContentState? state = document.OptionalContent?.GetDefaultStates();
            var entries = new List<Entry>(annotations.Count);
            var quads = new List<PdfQuadrilateral>();

            // Drawing order (as the annotation layers draw): the other annotations, then the widgets, each in Annots order.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < annotations.Count; i++)
                {
                    PdfAnnotation annotation = annotations[i];
                    if ((annotation.Kind == PdfAnnotationKind.Widget) != (pass == 1) || !Shown(annotation, state))
                    {
                        continue;
                    }

                    int quadStart = quads.Count;
                    if (annotation is PdfLinkAnnotation link)
                    {
                        quads.AddRange(link.ActivationRegion);
                    }

                    AnnotationRole role = annotation.Kind switch
                    {
                        PdfAnnotationKind.Link => AnnotationRole.Link,
                        PdfAnnotationKind.Widget => AnnotationRole.Field,
                        _ => AnnotationRole.Annotation,
                    };
                    entries.Add(new Entry(annotation, annotation.Rect, role, quadStart, quads.Count - quadStart, i));
                }
            }

            Entry[] array = [.. entries];
            return new AnnotationPage(array, [.. quads], TabOrder(document, page, array));
        }

        /// <summary>The topmost annotation containing the point, or -1.</summary>
        public int HitTest(double x, double y)
        {
            Entry[] entries = Entries;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                ref readonly Entry entry = ref entries[i];
                if (entry.QuadCount > 0)
                {
                    for (int q = entry.QuadStart; q < entry.QuadStart + entry.QuadCount; q++)
                    {
                        if (Contains(quads[q], x, y))
                        {
                            return i;
                        }
                    }
                }
                else if (x >= entry.Rect.Left && x <= entry.Rect.Right && y >= entry.Rect.Bottom && y <= entry.Rect.Top
                    && entry.Rect.Width > 0 && entry.Rect.Height > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Shown(PdfAnnotation annotation, PdfOptionalContentState? state)
        {
            PdfAnnotationFlags flags = annotation.Flags;
            return annotation.Kind != PdfAnnotationKind.Popup
                && (flags & (PdfAnnotationFlags.Hidden | PdfAnnotationFlags.NoView)) == 0
                && !((flags & PdfAnnotationFlags.Invisible) != 0 && annotation.Kind == PdfAnnotationKind.Unknown)
                && (state is null || annotation.IsVisible(state));
        }

        // Any corner order: the quadrilateral is the convex hull of its points, the union of the four triangles they make.
        private static bool Contains(in PdfQuadrilateral quad, double x, double y) =>
            InTriangle(quad.Point1, quad.Point2, quad.Point3, x, y)
            || InTriangle(quad.Point1, quad.Point2, quad.Point4, x, y)
            || InTriangle(quad.Point1, quad.Point3, quad.Point4, x, y)
            || InTriangle(quad.Point2, quad.Point3, quad.Point4, x, y);

        private static bool InTriangle(PathPoint a, PathPoint b, PathPoint c, double x, double y)
        {
            double d1 = Cross(a, b, x, y);
            double d2 = Cross(b, c, x, y);
            double d3 = Cross(c, a, x, y);
            bool negative = d1 < 0 || d2 < 0 || d3 < 0;
            bool positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive) && (d1 != 0 || d2 != 0 || d3 != 0 || (x == a.X && y == a.Y));

            static double Cross(PathPoint p, PathPoint q, double x, double y) => ((q.X - p.X) * (y - p.Y)) - ((q.Y - p.Y) * (x - p.X));
        }

        private static int[] TabOrder(PdfDocument document, PdfPage page, Entry[] entries)
        {
            int[] order = new int[entries.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            CosObject tabs = document.Resolve(page.Dictionary.GetValueOrDefault(TabsKey));
            Comparison<int> comparison = RowOrder.Equals(tabs)
                ? (a, b) => entries[b].Rect.Top.CompareTo(entries[a].Rect.Top) is int byTop and not 0 ? byTop : entries[a].Rect.Left.CompareTo(entries[b].Rect.Left) is int byLeft and not 0 ? byLeft : entries[a].AnnotsIndex.CompareTo(entries[b].AnnotsIndex)
                : ColumnOrder.Equals(tabs)
                    ? (a, b) => entries[a].Rect.Left.CompareTo(entries[b].Rect.Left) is int byLeft and not 0 ? byLeft : entries[b].Rect.Top.CompareTo(entries[a].Rect.Top) is int byTop and not 0 ? byTop : entries[a].AnnotsIndex.CompareTo(entries[b].AnnotsIndex)
                    : (a, b) => entries[a].AnnotsIndex.CompareTo(entries[b].AnnotsIndex);
            Array.Sort(order, comparison);
            return order;
        }
    }
}
