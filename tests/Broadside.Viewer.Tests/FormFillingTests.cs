using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Form filling in the viewer (spec #33 viewer capability 12, issue #218), driven by synthetic pointer and key input.</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class FormFillingTests
{
    private const DisplayLayers Overlay = DisplayLayers.Annotations | DisplayLayers.FormFields;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filling_acroform_fields_through_synthetic_input_saves_the_same_file_as_the_form_api()
    {
        byte[] input;
        await using (DocumentSession session = Open("acroform-fields.pdf"))
        {
            var layer = new AnnotationLayer(session);
            await LoadAllAsync(layer);
            var filling = new FormFilling(layer);

            // A text field: a click focuses it with its value selected, typing replaces it, Backspace and more typing edit it, Enter commits once.
            Click(layer, Widget(layer, "name"));
            Assert.Equal((FieldEditKind.Text, "Ada", 0, 3), (filling.Kind, filling.Text, filling.SelectionStart, filling.SelectionLength));
            Assert.True(filling.TextInput("Adx"));
            Assert.True(filling.KeyDown(InputKey.Backspace, InputModifiers.None));
            Assert.True(filling.TextInput("a Lovelace"));
            Assert.Equal("Ada", ((PdfTextField)filling.Field!).Value); // nothing written while typing
            Assert.True(filling.KeyDown(InputKey.Enter, InputModifiers.None));
            Assert.Equal("Ada Lovelace", ((PdfTextField)filling.Field!).Value);

            // An inherited, required text field committed by Tab, which moves focus on.
            Click(layer, Widget(layer, "person.first"));
            Assert.True(filling.IsRequired);
            filling.TextInput("Mary");
            Assert.True(filling.KeyDown(InputKey.Tab, InputModifiers.None));
            Assert.NotEqual("person.first", filling.Field?.FullyQualifiedName);

            // A read-only field takes focus but no edit.
            Click(layer, Widget(layer, "person.last"));
            Assert.True(filling.IsReadOnly);
            Assert.False(filling.TextInput("nope"));
            Assert.False(filling.KeyDown(InputKey.Backspace, InputModifiers.None));

            // A check box toggled by Space, then a radio button turned on by a click.
            layer.Focus(Widget(layer, "agree"));
            Assert.Equal(FieldEditKind.Button, filling.Kind);
            Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));
            Click(layer, Widget(layer, "color", 0));

            // A combo box: Arrow Down picks the next option, Enter commits.
            Click(layer, Widget(layer, "country"));
            Assert.True(filling.KeyDown(InputKey.ArrowDown, InputModifiers.None));
            if (!filling.IsDirty)
            {
                Assert.True(filling.KeyDown(InputKey.ArrowUp, InputModifiers.None)); // the value was the last option
            }

            Assert.True(filling.IsDirty);
            int country = Assert.Single(filling.SelectedOptions);
            Assert.True(filling.KeyDown(InputKey.Enter, InputModifiers.None));

            // A multi-select list box: Space toggles the highlighted option, arrows move the highlight, Tab commits.
            Click(layer, Widget(layer, "toppings"));
            Assert.Equal([0, 2], filling.SelectedOptions);
            Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));
            Assert.True(filling.KeyDown(InputKey.ArrowDown, InputModifiers.None));
            Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));
            Assert.Equal([1, 2], filling.SelectedOptions);
            Assert.True(filling.KeyDown(InputKey.Tab, InputModifiers.None));

            using var stream = new MemoryStream();
            await session.Document.SaveAsync(stream, Token);
            input = stream.ToArray();

            // The API, in the same order with the same values.
            using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes("acroform-fields.pdf"), new PdfOptions().UseTimeProvider(FixedTimeProvider.Instance), Token);
            PdfAcroForm form = document.AcroForm!;
            ((PdfTextField)form.FindField("name")!).SetValue("Ada Lovelace");
            ((PdfTextField)form.FindField("person.first")!).SetValue("Mary");
            ((PdfCheckBoxField)form.FindField("agree")!).SetChecked(false);
            var color = (PdfRadioButtonField)form.FindField("color")!;
            color.Select(color.Widgets[0]);
            ((PdfChoiceField)form.FindField("country")!).SetSelectedIndices([country]);
            ((PdfChoiceField)form.FindField("toppings")!).SetSelectedIndices([1, 2]);
            using var expected = new MemoryStream();
            await document.SaveAsync(expected, Token);
            Assert.Equal(expected.ToArray(), input);
        }

        using PdfDocument reopened = await PdfDocument.OpenAsync(input, Token);
        Assert.Equal("Ada Lovelace", ((PdfTextField)reopened.AcroForm!.FindField("name")!).Value);
        Assert.Equal([1, 2], ((PdfChoiceField)reopened.AcroForm.FindField("toppings")!).SelectedIndices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_shared_field_set_fills_the_same_through_keys_or_a_native_editor_as_through_the_form_api(bool nativeEditor)
    {
        // The field set every shell fills (#543): the WPF, WinUI and Avalonia shells feed their native editors through SetText and
        // SetSelectedOptions; Blazor and MAUI also send keys. Either way the saved file is the form API's.
        byte[] filled;
        await using (DocumentSession session = Open(FormFillingScenario.FileName))
        {
            var layer = new AnnotationLayer(session);
            await LoadAllAsync(layer);
            var filling = new FormFilling(layer);

            void Type(string field, string text)
            {
                layer.Focus(Widget(layer, field));
                Assert.Equal(FieldEditKind.Text, filling.Kind);
                Assert.True(nativeEditor ? filling.SetText(text) : filling.TextInput(text));
                Assert.True(filling.Commit());
            }

            void Pick(string field, int index, Action keys)
            {
                layer.Focus(Widget(layer, field));
                Assert.Equal(FieldEditKind.Choice, filling.Kind);
                if (nativeEditor)
                {
                    Assert.True(filling.SetSelectedOptions([index]));
                }
                else
                {
                    keys();
                }

                Assert.Equal([index], filling.SelectedOptions);
                Assert.True(filling.Commit());
            }

            // Text cut at MaxLen, and a comb field at its comb count, whether typed or set whole.
            Type("name", FormFillingScenario.NameTyped);
            Type("code", FormFillingScenario.CodeTyped);

            // A read-only field takes focus and no edit.
            layer.Focus(Widget(layer, "ref"));
            Assert.True(filling.IsReadOnly);
            Assert.False(filling.SetText("R-2"));
            Assert.False(filling.TextInput("R-2"));

            // A check box and a radio button, toggled by Space in every shell.
            layer.Focus(Widget(layer, "agree"));
            Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));
            layer.Focus(Widget(layer, "size", FormFillingScenario.SizeWidget));
            Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));

            // A list box picked by typing ahead, a combo box by Arrow Down, an editable combo box by typing a value it does not list.
            Pick("fruit", FormFillingScenario.Fruit, () => Assert.True(filling.TextInput("C")));
            Pick("country", FormFillingScenario.Country, () => Assert.True(filling.KeyDown(InputKey.ArrowDown, InputModifiers.None)));
            layer.Focus(Widget(layer, "city"));
            Assert.True(nativeEditor ? filling.SetText(FormFillingScenario.City) : filling.TextInput(FormFillingScenario.City));
            Assert.Empty(filling.SelectedOptions);
            Assert.True(filling.Commit());

            // A multi-select list box: two options toggled (#560), by the native list's whole selection after each click or by
            // End/Home and Space; the field is written once, at the commit.
            layer.Focus(Widget(layer, "colors"));
            Assert.Equal([0], filling.SelectedOptions);
            InputKey[] keys = [InputKey.End, InputKey.Home];
            for (int i = 0; i < FormFillingScenario.ColorsToggled.Count; i++)
            {
                if (nativeEditor)
                {
                    Assert.True(filling.SetSelectedOptions(FormFillingScenario.ColorsAfterEachToggle[i]));
                }
                else
                {
                    Assert.True(filling.KeyDown(keys[i], InputModifiers.None));
                    Assert.Equal(FormFillingScenario.ColorsToggled[i], filling.HighlightedOption);
                    Assert.True(filling.KeyDown(InputKey.Space, InputModifiers.None));
                }

                Assert.Equal(FormFillingScenario.ColorsAfterEachToggle[i], filling.SelectedOptions);
            }

            Assert.True(filling.Commit());
            layer.Focus(null);

            Assert.Equal(await FormFillingScenario.ExpectedAsync(Token), FormFillingScenario.Describe(session.Document.AcroForm!));
            using var stream = new MemoryStream();
            await session.Document.SaveAsync(stream, Token);
            filled = stream.ToArray();
        }

        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(FormFillingScenario.FileName), new PdfOptions().UseTimeProvider(FixedTimeProvider.Instance), Token);
        FormFillingScenario.FillThroughApi(document.AcroForm!);
        using var expected = new MemoryStream();
        await document.SaveAsync(expected, Token);
        Assert.Equal(expected.ToArray(), filled);
    }

    [Fact]
    public async Task Tab_moves_through_the_fields_in_the_order_the_page_Tabs_entry_gives()
    {
        foreach (string tabs in new[] { "R", "C" })
        {
            await using DocumentSession session = Open("acroform-fields.pdf");
            session.Document.Pages[0].Dictionary[new CosName("Tabs")] = new CosName(tabs);
            var layer = new AnnotationLayer(session);
            int count = await layer.ReadPageAsync(0, Token);
            var filling = new FormFilling(layer);

            AnnotationTarget[] all = [.. Enumerable.Range(0, count).Select(i => new AnnotationTarget(0, i))];
            AnnotationTarget[] expected = tabs == "R"
                ? [.. all.OrderByDescending(t => layer.GetRectangle(t).Top).ThenBy(t => layer.GetRectangle(t).Left)]
                : [.. all.OrderBy(t => layer.GetRectangle(t).Left).ThenByDescending(t => layer.GetRectangle(t).Top)];
            var visited = new List<AnnotationTarget>();
            while (filling.KeyDown(InputKey.Tab, InputModifiers.None))
            {
                visited.Add(layer.Focused!.Value);
            }

            Assert.Equal(expected, visited);
            Assert.Null(layer.Focused); // past the last field, focus leaves the page for the shell

            // Shift+Tab walks back.
            Assert.True(filling.KeyDown(InputKey.Tab, InputModifiers.Shift));
            Assert.Equal(expected[^1], layer.Focused);
        }
    }

    [Fact]
    public async Task An_XFA_form_raises_the_explanation_event_once()
    {
        await using DocumentSession session = Open("acroform-xfa.pdf");
        var layer = new AnnotationLayer(session);
        var notices = new List<XfaFormEventArgs>();
        layer.XfaFormDetected += (_, e) => notices.Add(e);
        await layer.ReadPageAsync(0, Token);
        await layer.ReadPageAsync(0, Token);

        XfaFormEventArgs notice = Assert.Single(notices);
        Assert.Same(notice, layer.XfaForm);
        Assert.False(notice.IsDynamic);
        Assert.Equal(4, notice.PacketCount);
        Assert.Contains("XFA", notice.Explanation, StringComparison.Ordinal);

        // Its AcroForm field is still filled through the viewer.
        var filling = new FormFilling(layer);
        Assert.True(filling.KeyDown(InputKey.Tab, InputModifiers.None));
        Assert.Equal(FieldEditKind.Text, filling.Kind);
        filling.TextInput("filled");
        Assert.True(filling.Commit());

        await using DocumentSession plain = Open("acroform-fields.pdf");
        var other = new AnnotationLayer(plain);
        other.XfaFormDetected += (_, e) => notices.Add(e);
        await other.ReadPageAsync(0, Token);
        Assert.Single(notices);
        Assert.Null(other.XfaForm);
    }

    [Fact]
    public async Task Committing_an_edit_redraws_only_the_fields_overlay_tiles()
    {
        using var surface = new HeadlessSurface();
        var events = new List<TileSchedulerEvent>();
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("acroform-fields.pdf"), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var scheduler = new TileScheduler(
            session,
            layout,
            surface,
            new TileSchedulerOptions().WithSeparateOverlays().WithRenderAhead(0).WithNearMargin(0).WithPlaceholderScale(0).UseObserver(events.Add));
        scheduler.Update(new Viewport(0, 0, 612, 792, 1));
        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        var layer = new AnnotationLayer(session, scheduler);
        await layer.ReadPageAsync(0, Token);
        var filling = new FormFilling(layer);
        Click(layer, Widget(layer, "name"));
        events.Clear();
        filling.TextInput("Typed");
        Assert.Empty(events); // typing draws nothing until the edit commits

        Assert.True(filling.Commit());
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal((TileEventKind.Invalidated, Overlay), (e.Kind, e.Tile.Layers)));
        PdfRectangle rect = layer.GetRectangle(Widget(layer, "name"));
        Assert.All(events, e => Assert.True(e.Tile.Region.Y < 792 - rect.Bottom + 1 && e.Tile.Region.Y + e.Tile.Region.Height > 792 - rect.Top - 1));
        Assert.False(filling.Commit()); // nothing left to write
    }

    [Fact]
    public async Task A_reset_form_action_resets_every_descendant_of_the_named_non_terminal_field()
    {
        await using DocumentSession session = Open("acroform-fields.pdf");
        var layer = new AnnotationLayer(session);
        await LoadAllAsync(layer);
        var filling = new FormFilling(layer);
        var actions = new List<ViewerActionEventArgs>();
        layer.ActionRequested += (_, e) => actions.Add(e);
        PdfAcroForm form = session.Document.AcroForm!;
        var first = (PdfTextField)form.FindField("person.first")!;
        var last = (PdfTextField)form.FindField("person.last")!;
        var name = (PdfTextField)form.FindField("name")!;
        Assert.NotNull(last.Value);

        // The push button's action names (person) and the check box: person.first and person.last are its descendants.
        Click(layer, Widget(layer, "submit"));
        ViewerActionEventArgs reset = Assert.Single(actions);
        Assert.Equal(PdfActionKind.ResetForm, reset.Kind);
        Assert.Equal(["person.first", "person.last", "agree"], ((PdfResetFormAction)reset.Action!).SelectFields().Select(f => f.FullyQualifiedName));
        Assert.Equal(first.DefaultValue, first.Value);
        Assert.Equal(last.DefaultValue, last.Value); // read-only fields reset too
        Assert.False(((PdfCheckBoxField)form.FindField("agree")!).IsChecked);
        Assert.Equal("Ada", name.Value); // not named

        // Required: person.first now has no value.
        Assert.Contains(filling.GetMissingRequiredFields(), f => f.FullyQualifiedName == "person.first");
    }

    [Fact]
    public async Task Text_edits_honor_carets_selection_max_length_single_lines_and_cancel()
    {
        await using DocumentSession session = Open("acroform-fields.pdf");
        var layer = new AnnotationLayer(session);
        await LoadAllAsync(layer);
        var filling = new FormFilling(layer);
        Click(layer, Widget(layer, "name"));
        Assert.Equal(40, filling.MaxLength);

        filling.TextInput("ab\ncd");
        Assert.Equal("ab cd", filling.Text); // a single-line field takes no line break
        filling.KeyDown(InputKey.Home, InputModifiers.None);
        filling.KeyDown(InputKey.ArrowRight, InputModifiers.Shift);
        filling.KeyDown(InputKey.ArrowRight, InputModifiers.Shift);
        Assert.Equal((0, 2), (filling.SelectionStart, filling.SelectionLength));
        filling.KeyDown(InputKey.Delete, InputModifiers.None);
        Assert.Equal(" cd", filling.Text);
        filling.KeyDown(InputKey.End, InputModifiers.None);
        filling.TextInput(new string('x', 100));
        Assert.Equal(40, filling.Text.Length);
        Assert.True(filling.IsDirty);

        Assert.True(filling.KeyDown(InputKey.Escape, InputModifiers.None));
        Assert.Equal("Ada", filling.Text);
        Assert.False(filling.IsDirty);
        Assert.False(filling.KeyDown(InputKey.Escape, InputModifiers.None)); // nothing to abandon: the shell's Escape

        // A native text box sends its whole text; leaving the field (focus elsewhere) commits it.
        Assert.True(filling.SetText("From a text box"));
        layer.Focus(null);
        Assert.Equal("From a text box", ((PdfTextField)session.Document.AcroForm!.FindField("name")!).Value);
        Assert.Equal(FieldEditKind.None, filling.Kind);
    }

    [Fact]
    public async Task Fields_without_appearances_draw_transient_ones_and_the_document_stays_unchanged()
    {
        byte[] file = Corpus.Bytes("acroform-fields.pdf");
        await using DocumentSession session = Open(file);
        await using DocumentSession plain = DocumentSession.Open(file, new ViewerOptions().WithDocumentOptions(new PdfOptions().UseTimeProvider(FixedTimeProvider.Instance)).WithThreading(ViewerThreading.Background).WithTransientFieldAppearances(false));
        foreach (DocumentSession s in new[] { session, plain })
        {
            ((PdfTextField)s.Document.AcroForm!.FindField("name")!).Widgets[0].Dictionary.Remove(new CosName("AP"));
        }

        using var before = new MemoryStream();
        await session.Document.SaveAsync(before, Token);

        DisplayList transient = await session.GetDisplayListAsync(0, DisplayLayers.FormFields, Token);
        DisplayList without = await plain.GetDisplayListAsync(0, DisplayLayers.FormFields, Token);
        Assert.True(transient.OperationCount > without.OperationCount);
        Assert.Null(((PdfTextField)session.Document.AcroForm!.FindField("name")!).Widgets[0].AppearanceDictionary);

        using var after = new MemoryStream();
        await session.Document.SaveAsync(after, Token);
        Assert.Equal(before.ToArray(), after.ToArray());
    }

    internal static AnnotationTarget Widget(AnnotationLayer layer, string name, int widget = 0)
    {
        PdfTerminalField field = (PdfTerminalField)layer.Session.Document.AcroForm!.FindField(name)!;
        CosDictionary dictionary = field.Widgets[widget].Dictionary;
        for (int page = 0; page < layer.Session.PageCount; page++)
        {
            for (int i = 0; i < layer.GetCount(page); i++)
            {
                var target = new AnnotationTarget(page, i);
                if (ReferenceEquals(layer.GetAnnotation(target).Dictionary, dictionary))
                {
                    return target;
                }
            }
        }

        throw new InvalidOperationException($"No widget of {name} is loaded.");
    }

    private static void Click(AnnotationLayer layer, AnnotationTarget target)
    {
        PdfRectangle rect = layer.GetRectangle(target);
        var point = new PagePoint(target.PageIndex, (rect.Left + rect.Right) / 2, (rect.Bottom + rect.Top) / 2);
        Assert.True(layer.PointerDown(point));
        Assert.True(layer.PointerUp(point));
    }

    private static async Task LoadAllAsync(AnnotationLayer layer)
    {
        for (int page = 0; page < layer.Session.PageCount; page++)
        {
            await layer.ReadPageAsync(page, Token);
        }
    }

    // Saves are compared byte for byte, so the documents write their dates from a fixed clock.
    private static DocumentSession Open(string fileName)
    {
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background);
        options.DocumentOptions!.UseTimeProvider(FixedTimeProvider.Instance);
        return DocumentSession.Open(Corpus.Bytes(fileName), options);
    }

    private static DocumentSession Open(byte[] file) =>
        DocumentSession.Open(file, new ViewerOptions().WithDocumentOptions(new PdfOptions().UseTimeProvider(FixedTimeProvider.Instance)).WithThreading(ViewerThreading.Background));
}
