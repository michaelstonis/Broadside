using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

[Collection(HeavyTestCollection.Name)]
public sealed class AnnotationLayerTests
{
    private const DisplayLayers Overlay = DisplayLayers.Annotations | DisplayLayers.FormFields;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Toggling_annotations_redraws_only_the_overlay_tiles()
    {
        using var surface = new HeadlessSurface();
        var events = new List<TileSchedulerEvent>();
        await using DocumentSession session = Cooperative(AnnotationSamples.Grid());
        using TileScheduler scheduler = await SchedulerAsync(session, surface, events);
        RunToIdle(scheduler);
        int overlays = events.Count(e => e.Kind == TileEventKind.Presented && e.Tile.Layers == Overlay);
        Assert.Equal(12, overlays); // 612 x 792 at 1 px/pt in 256-px tiles
        Assert.Contains(events, e => e.Kind == TileEventKind.Presented && e.Tile.Layers == DisplayLayers.Content);

        events.Clear();
        scheduler.SetLayers(DisplayLayers.Content);
        RunToIdle(scheduler);
        Assert.All(events, e => Assert.Equal((TileEventKind.Evicted, Overlay), (e.Kind, e.Tile.Layers)));
        Assert.Equal(overlays, events.Count);
        Assert.All(surface.Tiles, t => Assert.Equal(DisplayLayers.Content, t.Tile.Layers));

        events.Clear();
        scheduler.SetLayers(DisplayLayers.All);
        RunToIdle(scheduler);
        Assert.All(events, e => Assert.Equal(Overlay, e.Tile.Layers));
        Assert.Equal(overlays, events.Count(e => e.Kind == TileEventKind.Started));
    }

    [Fact]
    public async Task Moving_an_annotation_by_dragging_redraws_only_the_overlay_tiles_it_touched()
    {
        using var surface = new HeadlessSurface();
        var events = new List<TileSchedulerEvent>();
        await using DocumentSession session = Cooperative(AnnotationSamples.Grid());
        using TileScheduler scheduler = await SchedulerAsync(session, surface, events);
        RunToIdle(scheduler);
        var layer = new AnnotationLayer(session, scheduler);
        await layer.LoadPageAsync(0, Token);
        var invalidations = new List<PageInvalidatedEventArgs>();
        layer.Invalidated += (_, e) => invalidations.Add(e);
        events.Clear();

        // The grid's second annotation: a plain Square at [86.5 10 126.5 50].
        Assert.True(layer.PointerDown(new PagePoint(0, 100, 30)));
        AnnotationTarget square = Assert.NotNull(layer.Selected);
        Assert.Equal(AnnotationRole.Annotation, layer.GetRole(square));
        layer.PointerMove(new PagePoint(0, 110, 30));
        Assert.True(layer.IsDragging);
        Assert.Empty(events);
        Assert.True(layer.PointerUp(new PagePoint(0, 120, 30)));

        PdfRectangle moved = layer.GetRectangle(Assert.NotNull(layer.Selected));
        Assert.Equal((106.5, 10, 146.5, 50), (moved.Left, moved.Bottom, moved.Right, moved.Top));
        PageInvalidatedEventArgs invalidation = Assert.Single(invalidations);
        Assert.Equal((DisplayLayers.Annotations, 86.5, 146.5), (invalidation.Layers, invalidation.Area!.Value.Left, invalidation.Area.Value.Right));

        // Device rows 742-782: tiles (0, 512) and (0, 768) of column 0, overlay only; they stay on the surface until redrawn (#347).
        Assert.All(events, e => Assert.Equal((TileEventKind.Invalidated, Overlay, 0), (e.Kind, e.Tile.Layers, e.Tile.Region.X)));
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Contains(surface.Tiles, t => t.Tile == e.Tile));
        events.Clear();
        RunToIdle(scheduler);
        Assert.All(events, e => Assert.Equal(Overlay, e.Tile.Layers));
        Assert.Equal(2, events.Count(e => e.Kind == TileEventKind.Presented));
    }

    [Fact]
    public async Task Choosing_a_form_value_keeps_every_tile_in_the_frame_until_its_redraw_replaces_it()
    {
        // Issue #347: the old tiles of the field stay drawn while the new ones render, so no frame shows blank paper.
        using var surface = new HeadlessSurface();
        var events = new List<TileSchedulerEvent>();
        await using DocumentSession session = Cooperative(Corpus.Bytes("acroform-fields.pdf"));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithRenderAhead(0).WithNearMargin(0).WithPlaceholderScale(0).UseObserver(events.Add));
        var viewport = new Viewport(0, 0, 612, 792, 1);
        scheduler.Update(viewport);
        RunToIdle(scheduler);
        var frame = new FramePart[64];
        int before = scheduler.GetFrame(viewport, frame);
        int tiles = surface.Tiles.Count;

        var layer = new AnnotationLayer(session, scheduler);
        int count = await layer.LoadPageAsync(0, Token);
        AnnotationTarget box = Enumerable.Range(0, count).Select(i => new AnnotationTarget(0, i))
            .First(t => layer.GetRole(t) == AnnotationRole.Field && ((PdfWidgetAnnotation)layer.GetAnnotation(t)).Field is PdfCheckBoxField { IsReadOnly: false });
        events.Clear();
        layer.Activate(box);
        Assert.Contains(events, e => e.Kind == TileEventKind.Invalidated);
        do
        {
            Assert.Equal(before, scheduler.GetFrame(viewport, frame));
            Assert.All(frame.AsSpan(0, before).ToArray(), part => Assert.NotEqual(FramePartKind.Scaled, part.Kind));
            Assert.Equal(tiles, surface.Tiles.Count);
        }
        while (scheduler.RunFrame(TimeSpan.FromTicks(1)));

        Assert.DoesNotContain(events, e => e.Kind == TileEventKind.Evicted);
        Assert.Equal(events.Count(e => e.Kind == TileEventKind.Invalidated), events.Count(e => e.Kind == TileEventKind.Presented));
        Assert.Equal(tiles, surface.Tiles.Count);
    }

    [Fact]
    public async Task A_link_activates_inside_its_quadrilaterals_and_raises_its_uri_without_opening_it()
    {
        await using DocumentSession session = Open("annotations-link.pdf");
        var layer = new AnnotationLayer(session);
        Assert.Equal(1, await layer.LoadPageAsync(0, Token));
        var requested = new List<ViewerActionEventArgs>();
        layer.ActionRequested += (_, e) => requested.Add(e);
        int changes = 0;
        layer.Changed += (_, _) => changes++;

        // Inside Rect [72 700 300 724] but outside both quadrilaterals (the lower one ends at x 150).
        Assert.Null(layer.PointerMove(new PagePoint(0, 200, 705)));
        AnnotationTarget link = Assert.NotNull(layer.PointerMove(new PagePoint(0, 100, 705)));
        layer.PointerMove(new PagePoint(0, 101, 705));
        Assert.Equal(1, changes);
        Assert.Equal(AnnotationRole.Link, layer.GetRole(link));

        Assert.True(layer.PointerDown(new PagePoint(0, 100, 705)));
        Assert.True(layer.PointerUp(new PagePoint(0, 101, 706)));
        ViewerActionEventArgs action = Assert.Single(requested);
        Assert.Equal(PdfActionKind.Uri, action.Kind);
        Assert.Equal(new Uri("https://example.com/"), action.Uri);
        Assert.Null(action.PageIndex);
        Assert.True(layer.Remove(link));
        Assert.Equal(0, layer.GetCount(0));
    }

    [Fact]
    public async Task Every_link_of_actions_all_raises_each_action_of_its_tree_and_scripts_only_inform()
    {
        await using DocumentSession session = Open("actions-all.pdf");
        var layer = new AnnotationLayer(session);
        int count = await layer.LoadPageAsync(0, Token);
        var requested = new List<ViewerActionEventArgs>();
        layer.ActionRequested += (_, e) => requested.Add(e);

        int links = 0;
        var kinds = new HashSet<PdfActionKind>();
        for (int i = 0; i < count; i++)
        {
            var target = new AnnotationTarget(0, i);
            if (layer.GetAnnotation(target) is not PdfLinkAnnotation link)
            {
                continue;
            }

            links++;
            requested.Clear();
            layer.Activate(target);
            PdfAction[] expected = link.Action is { } action ? [.. action.EnumerateActionTree()] : [];
            Assert.Equal(expected.Length, requested.Count);
            Assert.Equal(expected.Select(a => a.Dictionary), requested.Select(e => e.Action!.Dictionary));
            Assert.All(requested, e => Assert.Same(link, e.Annotation));
            foreach (ViewerActionEventArgs e in requested.Take(1))
            {
                Check(e);
                kinds.Add(e.Kind);
            }
        }

        Assert.True(links >= 20);
        Assert.Superset(new HashSet<PdfActionKind> { PdfActionKind.GoTo, PdfActionKind.GoToR, PdfActionKind.Uri, PdfActionKind.Named, PdfActionKind.JavaScript, PdfActionKind.Launch }, kinds);

        static void Check(ViewerActionEventArgs e)
        {
            switch (e.Kind)
            {
                case PdfActionKind.GoTo:
                    Assert.Equal(0, e.PageIndex);
                    break;
                case PdfActionKind.GoToR:
                    Assert.Null(e.Destination);
                    break;
                case PdfActionKind.Uri:
                    Assert.Equal(new Uri("https://example.com/docs/index.html"), e.Uri);
                    break;
                case PdfActionKind.Named:
                    Assert.Equal(PdfNamedOperation.NextPage, e.NamedOperation);
                    break;
                case PdfActionKind.JavaScript:
                    Assert.True(e.IsScript);
                    break;
                default:
                    Assert.False(e.IsScript);
                    break;
            }
        }
    }

    [Fact]
    public async Task A_link_added_to_another_page_navigates_there_through_the_layout()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(3));
        var layer = new AnnotationLayer(session);
        await layer.LoadPageAsync(0, Token);
        AnnotationTarget link = Assert.NotNull(layer.Add(0, page => page.AddLinkAnnotation(new PdfRectangle(100, 100, 200, 150), session.Document.Pages[2])));
        var requested = new List<ViewerActionEventArgs>();
        layer.ActionRequested += (_, e) => requested.Add(e);

        layer.Activate(link);

        Assert.Equal(2, Assert.Single(requested).PageIndex);
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        Viewport viewport = AnnotationLayer.GetDestinationViewport(requested[0].Destination!, layout, new Viewport(0, 0, 600, 400, 1));
        Assert.Equal(2, layout.GetCurrentPage(viewport));
        Assert.Equal(link, layer.Focused);
    }

    [Fact]
    public async Task Annotations_are_added_edited_and_removed_and_locked_ones_stay()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(1));
        var layer = new AnnotationLayer(session);
        await layer.LoadPageAsync(0, Token);
        var invalidations = new List<PageInvalidatedEventArgs>();
        layer.Invalidated += (_, e) => invalidations.Add(e);

        AnnotationTarget square = Assert.NotNull(layer.Add(0, page => page.AddSquareAnnotation(new PdfRectangle(10, 10, 60, 60))));
        Assert.Equal(square, layer.Selected);
        Assert.Equal(1, layer.GetCount(0));
        Assert.True(layer.Edit(square, a => a.Color = new PdfDeviceColor(1, 0, 0)));
        Assert.True(layer.Move(square, 5, 5));
        Assert.Equal(15, layer.GetRectangle(square).Left);
        Assert.Equal(3, invalidations.Count);

        Assert.True(layer.Edit(square, a => a.Flags |= PdfAnnotationFlags.Locked, regenerateAppearance: false));
        Assert.False(layer.Move(square, 5, 5));
        Assert.False(layer.Remove(square));
        layer.GetAnnotation(square).Flags &= ~PdfAnnotationFlags.Locked;
        Assert.True(layer.Remove(square));
        Assert.Equal(0, layer.GetCount(0));
        Assert.Null(layer.Selected);
        Assert.Null(invalidations[^1].Area);
    }

    [Fact]
    public async Task A_text_note_exposes_its_popup_and_popups_take_no_part()
    {
        await using DocumentSession session = Open("annotations-subtypes.pdf");
        var layer = new AnnotationLayer(session);
        int count = await layer.LoadPageAsync(0, Token);
        AnnotationTarget[] targets = [.. Enumerable.Range(0, count).Select(i => new AnnotationTarget(0, i))];
        Assert.DoesNotContain(targets, t => layer.GetAnnotation(t).Kind == PdfAnnotationKind.Popup);

        AnnotationTarget text = targets.First(t => layer.GetAnnotation(t).Kind == PdfAnnotationKind.Text);
        AnnotationNote note = layer.GetNote(text);
        Assert.NotNull(note.PopupRectangle);
        Assert.Same(layer.GetAnnotation(text), note.Annotation);
    }

    [Fact]
    public async Task Form_fields_take_focus_and_their_values_write_through_the_form_api()
    {
        await using DocumentSession session = Open("acroform-fields.pdf");
        var layer = new AnnotationLayer(session);
        var widgets = new List<AnnotationTarget>();
        for (int page = 0; page < session.PageCount; page++)
        {
            int count = await layer.LoadPageAsync(page, Token);
            widgets.AddRange(Enumerable.Range(0, count).Select(i => new AnnotationTarget(page, i)).Where(t => layer.GetRole(t) == AnnotationRole.Field));
        }

        var invalidations = new List<PageInvalidatedEventArgs>();
        layer.Invalidated += (_, e) => invalidations.Add(e);
        Assert.False(layer.SetFieldText("nothing focused"));

        AnnotationTarget text = widgets.First(t => Field(layer, t) is PdfTextField { IsReadOnly: false });
        PdfRectangle rect = layer.GetRectangle(text);
        Assert.True(layer.PointerDown(new PagePoint(text.PageIndex, (rect.Left + rect.Right) / 2, (rect.Bottom + rect.Top) / 2)));
        layer.PointerUp(new PagePoint(text.PageIndex, (rect.Left + rect.Right) / 2, (rect.Bottom + rect.Top) / 2));
        Assert.Equal(text, layer.Focused);
        Assert.True(layer.SetFieldText("Ada"));
        Assert.Equal("Ada", ((PdfTextField)layer.FocusedField!).Value);
        Assert.Contains(invalidations, e => e.Layers == DisplayLayers.FormFields && e.PageIndex == text.PageIndex);
        Assert.False(layer.SetFieldChoice("x"));

        AnnotationTarget box = widgets.First(t => Field(layer, t) is PdfCheckBoxField { IsReadOnly: false });
        var checkBox = (PdfCheckBoxField)Field(layer, box)!;
        bool before = checkBox.IsChecked;
        layer.Activate(box);
        Assert.NotEqual(before, checkBox.IsChecked);
        Assert.True(layer.ToggleField());
        Assert.Equal(before, checkBox.IsChecked);

        AnnotationTarget combo = widgets.First(t => Field(layer, t) is PdfComboBoxField { IsReadOnly: false, Options.Count: > 1 });
        layer.Focus(combo);
        var choice = (PdfComboBoxField)layer.FocusedField!;
        Assert.True(layer.SetFieldChoice(choice.Options[1].ExportValue));
        Assert.Equal([1], choice.SelectedIndices);
        Assert.True(layer.SetFieldSelection([0]));
        Assert.Equal([0], choice.SelectedIndices);

        Assert.True(layer.PointerDown(new PagePoint(text.PageIndex, rect.Left, rect.Bottom)));
        Assert.False(layer.Move(text, 1, 1)); // widgets move through the field API
        Assert.False(string.IsNullOrEmpty(layer.GetAccessibleName(text)));

        static PdfTerminalField? Field(AnnotationLayer layer, AnnotationTarget target) => ((PdfWidgetAnnotation)layer.GetAnnotation(target)).Field;
    }

    [Fact]
    public async Task Keyboard_focus_follows_the_page_tab_order()
    {
        byte[] file = AnnotationSamples.Grid(4, 3);
        await using DocumentSession session = Open(file);
        session.Document.Pages[0].Dictionary[new CosName("Tabs")] = new CosName("C");
        var layer = new AnnotationLayer(session);
        int count = await layer.LoadPageAsync(0, Token);
        var order = new AnnotationTarget[count];
        Assert.Equal(count, layer.GetFocusOrder(0, order));

        // Column order: left to right, each column top to bottom.
        for (int i = 1; i < count; i++)
        {
            PdfRectangle a = layer.GetRectangle(order[i - 1]), b = layer.GetRectangle(order[i]);
            Assert.True(a.Left < b.Left || (a.Left == b.Left && a.Top > b.Top));
        }

        foreach (AnnotationTarget expected in order)
        {
            Assert.True(layer.FocusNext());
            Assert.Equal(expected, layer.Focused);
        }

        Assert.False(layer.FocusNext());
        Assert.Null(layer.Focused);
        Assert.True(layer.FocusPrevious());
        Assert.Equal(order[^1], layer.Focused);
    }

    [Fact]
    public async Task Pointer_moves_and_drags_allocate_nothing()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid());
        var layer = new AnnotationLayer(session);
        await layer.LoadPageAsync(0, Token);
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        var viewport = new Viewport(0, 0, 612, 792, 1);
        int changes = 0;
        layer.Changed += (_, _) => changes++;
        double sum = 0;

        Action moves = () =>
        {
            for (int i = 0; i < 64; i++)
            {
                if (layer.PointerMove(new PagePoint(0, i * 9.5, i * 12.3)) is { } hovered)
                {
                    sum += layer.GetScreenRectangle(hovered, layout, viewport).Width;
                }
            }
        };
        moves();
        Assert.Equal(0, Allocations.Measure(moves));

        layer.PointerDown(new PagePoint(0, 100, 30));
        Action drag = () =>
        {
            for (int i = 0; i < 64; i++)
            {
                layer.PointerMove(new PagePoint(0, 100 + i, 30));
                sum += layer.GetScreenRectangle(layer.Selected!.Value, layout, viewport).X;
            }
        };
        drag();
        Assert.Equal(0, Allocations.Measure(drag));
        layer.PointerCancel();
        Assert.True(changes > 0 && sum > 0);
    }

    [Fact]
    public async Task Links_and_form_fields_are_accessible_nodes_in_keyboard_focus_order()
    {
        await using DocumentSession session = Open("acroform-fields.pdf");
        var layer = new AnnotationLayer(session);
        await layer.LoadPageAsync(0, Token);
        AnnotationTarget link = Assert.NotNull(layer.Add(0, page => page.AddLinkAnnotation(new PdfRectangle(10, 10, 60, 30), new Uri("https://example.com/a"))));
        var bridge = new AccessibilityBridge(session, layer);
        layer.Accessibility = bridge;

        AccessibleNode pageNode = await bridge.LoadPageAsync(0, Token);
        AccessibleNode[] elements = [.. pageNode.Children.Where(n => n.Source is CosDictionary)];
        var order = new AnnotationTarget[layer.GetCount(0)];
        int count = layer.GetFocusOrder(0, order);
        object[] expected = [.. order[..count].Select(t => layer.GetAnnotation(t)).Where(a => a is PdfLinkAnnotation or PdfWidgetAnnotation or PdfMarkupAnnotation).Select(a => (object)a.Dictionary)];
        Assert.Equal(expected, elements.Select(n => n.Source!));

        AccessibleNode linkNode = Assert.Single(elements, n => n.Role == AccessibleRole.Link);
        Assert.Equal("https://example.com/a", linkNode.Name);
        AccessibleNode textNode = elements.First(n => n.Role == AccessibleRole.TextBox);
        Assert.True(bridge.GetStates(textNode).HasFlag(AccessibleStates.Focusable));

        // The layer's keyboard focus moves the bridge's, and a value edit rebuilds the page's nodes with focus kept on the field.
        while (layer.Focused is not { } focused || !ReferenceEquals(layer.GetAnnotation(focused).Dictionary, textNode.Source))
        {
            Assert.True(layer.FocusNext());
            Assert.Same(layer.GetAnnotation(layer.Focused!.Value).Dictionary, bridge.Focus?.Source);
        }

        Assert.True(layer.SetFieldText("Grace"));
        Assert.Equal("Grace", bridge.Focus?.Value);
        Assert.Same(textNode.Source, bridge.Focus?.Source);
        layer.Focus(link);
        Assert.Equal(AccessibleRole.Link, bridge.Focus?.Role);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(1));
        Assert.Throws<ArgumentNullException>(() => new AnnotationLayer(null!));
        var layer = new AnnotationLayer(session);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await layer.LoadPageAsync(1, Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.GetAnnotation(new AnnotationTarget(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.Add(0, page => page.AddSquareAnnotation(new PdfRectangle(0, 0, 1, 1))));
        Assert.Null(layer.HitTest(new PagePoint(0, 1, 1)));
        Assert.Equal(0, layer.GetCount(5));
    }

    private static DocumentSession Open(string fileName) => TextSelectionTests.Open(fileName);

    private static DocumentSession Open(byte[] file) => DocumentSession.Open(file, new ViewerOptions().WithThreading(ViewerThreading.Background));

    private static DocumentSession Cooperative(byte[] file) => DocumentSession.Open(file, new ViewerOptions().WithThreading(ViewerThreading.Cooperative));

    private static async Task<TileScheduler> SchedulerAsync(DocumentSession session, HeadlessSurface surface, List<TileSchedulerEvent> events)
    {
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        var scheduler = new TileScheduler(
            session,
            layout,
            surface,
            new TileSchedulerOptions().WithSeparateOverlays().WithRenderAhead(0).WithNearMargin(0).WithPlaceholderScale(0).UseObserver(events.Add));
        scheduler.Update(new Viewport(0, 0, 612, 792, 1));
        return scheduler;
    }

    private static void RunToIdle(TileScheduler scheduler)
    {
        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }
    }
}
