using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>The viewer's undo and redo history (#638): app-defined steps, the annotation and form field steps, and leniency.</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class EditHistoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_defined_steps_undo_and_redo_in_order_and_name_the_menu_items()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(2, 1));
        var history = new EditHistory(session);
        var log = new List<string>();
        int changes = 0;
        history.Changed += (_, _) => changes++;
        Assert.False(history.CanUndo);
        Assert.False(history.Undo());
        Assert.Null(history.UndoName);

        history.Record(new LoggedStep("First", log));
        history.Record(new LoggedStep("Second", log));
        Assert.Equal(2, changes);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Second", history.UndoName);

        Assert.True(history.Undo());
        Assert.Equal(("First", "Second"), (history.UndoName, history.RedoName));
        Assert.True(history.Undo());
        Assert.False(history.Undo());
        Assert.True(history.Redo());
        Assert.Equal(["undo Second", "undo First", "redo First"], log);

        // A new step clears what could be redone.
        history.Record(new LoggedStep("Third", log));
        Assert.False(history.CanRedo);
        Assert.Equal("Third", history.UndoName);
    }

    [Fact]
    public async Task The_oldest_steps_drop_past_the_limit()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(2, 1));
        var history = new EditHistory(session, new EditHistoryOptions().WithLimit(2));
        var log = new List<string>();
        history.Record(new LoggedStep("1", log));
        history.Record(new LoggedStep("2", log));
        history.Record(new LoggedStep("3", log));
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.False(history.Undo());
        Assert.Equal(["undo 3", "undo 2"], log);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistoryOptions().WithLimit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistoryOptions().WithMergeWindow(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task A_group_undoes_and_redoes_as_one_step()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(2, 1));
        var history = new EditHistory(session);
        var log = new List<string>();
        using (history.BeginGroup("Draw ink"))
        {
            history.Record(new LoggedStep("Stroke 1", log));
            using (history.BeginGroup("Inner"))
            {
                history.Record(new LoggedStep("Stroke 2", log));
            }

            Assert.False(history.CanUndo); // the group is still open
        }

        using (history.BeginGroup("Nothing"))
        {
        }

        Assert.Equal("Draw ink", history.UndoName);
        Assert.True(history.Undo());
        Assert.False(history.CanUndo);
        Assert.Equal(["undo Stroke 2", "undo Stroke 1"], log);
        Assert.True(history.Redo());
        Assert.Equal(["undo Stroke 2", "undo Stroke 1", "redo Stroke 1", "redo Stroke 2"], log);
    }

    [Fact]
    public async Task IsModified_is_relative_to_the_last_save()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(2, 1));
        var history = new EditHistory(session);
        var log = new List<string>();
        Assert.False(history.IsModified);
        history.Record(new LoggedStep("1", log));
        Assert.True(history.IsModified);
        history.MarkSaved();
        Assert.False(history.IsModified);
        history.Undo();
        Assert.True(history.IsModified);
        history.Redo();
        Assert.False(history.IsModified);
        history.Undo();
        history.Record(new LoggedStep("2", log)); // the saved state cannot come back
        Assert.True(history.IsModified);
        history.Undo();
        Assert.True(history.IsModified);
        history.Clear();
        Assert.False(history.CanUndo || history.CanRedo);
        Assert.True(history.IsModified);
        history.MarkSaved();
        history.Clear();
        Assert.False(history.IsModified);
    }

    public static TheoryData<string> AnnotationEdits => ["add", "edit", "move", "remove"];

    [Theory]
    [MemberData(nameof(AnnotationEdits))]
    public async Task An_annotation_edit_is_undone_and_redone_to_the_same_saved_bytes(string edit)
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await layer.ReadPageAsync(0, Token);
        var invalidations = new List<PageInvalidatedEventArgs>();
        layer.Invalidated += (_, e) => invalidations.Add(e);
        AnnotationTarget square = Assert.NotNull(layer.HitTest(new PagePoint(0, 234, 30)));
        int count = layer.GetCount(0);
        byte[] before = await SaveAsync(session);

        string name;
        switch (edit)
        {
            case "add":
                Assert.NotNull(layer.Add(0, page => page.AddSquareAnnotation(new PdfRectangle(300, 300, 360, 340))));
                name = "Add annotation";
                break;
            case "edit":
                Assert.True(layer.Edit(square, a => a.Color = new PdfDeviceColor(1, 0, 0)));
                name = "Edit annotation";
                break;
            case "move":
                Assert.True(layer.Move(square, 20, 30));
                name = "Move annotation";
                break;
            default:
                Assert.True(layer.Remove(square));
                name = "Delete annotation";
                break;
        }

        byte[] after = await SaveAsync(session);
        Assert.NotEqual(before, after);
        EditHistory history = layer.History!;
        Assert.Equal(name, history.UndoName);

        invalidations.Clear();
        Assert.True(history.Undo());
        Assert.Equal(count, layer.GetCount(0));
        PageInvalidatedEventArgs invalidation = Assert.Single(invalidations);
        Assert.Equal((0, DisplayLayers.Annotations), (invalidation.PageIndex, invalidation.Layers));
        byte[] undone = await SaveAsync(session);
        await AssertSameObjectsAsync(before, undone); // rewritten rather than copied, so compared as objects

        Assert.True(history.Redo());
        Assert.Equal(after, await SaveAsync(session));
        Assert.True(history.Undo());
        await AssertSameObjectsAsync(undone, await SaveAsync(session));
        Assert.True(history.Redo());
        Assert.Equal(after, await SaveAsync(session));
        Assert.DoesNotContain(session.Document.Diagnostics, d => d.Code == "ViewerEditHistoryRestoreFailed");
    }

    [Fact]
    public async Task A_moved_annotation_comes_back_to_its_rectangle_and_appearance()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await layer.ReadPageAsync(0, Token);
        AnnotationTarget square = Assert.NotNull(layer.HitTest(new PagePoint(0, 234, 30)));
        PdfRectangle rect = layer.GetRectangle(square);
        PdfAnnotation annotation = layer.GetAnnotation(square);

        // A drag records one step on release.
        Assert.True(layer.PointerDown(new PagePoint(0, 234, 30)));
        layer.PointerMove(new PagePoint(0, 244, 30));
        Assert.True(layer.PointerUp(new PagePoint(0, 254, 30)));
        Assert.Equal(rect.Left + 20, layer.GetRectangle(square).Left, 6);

        Assert.True(layer.History!.Undo());
        Assert.Equal(rect, layer.GetRectangle(square));
        Assert.Same(annotation.Dictionary, layer.GetAnnotation(square).Dictionary); // the same live dictionary, written back
        Assert.True(layer.History.Redo());
        Assert.Equal(rect.Left + 20, layer.GetRectangle(square).Left, 6);
    }

    [Fact]
    public async Task A_field_value_and_a_reset_are_undone_and_redone_to_the_same_saved_bytes()
    {
        await using DocumentSession session = Open(Corpus.Bytes("acroform-fields.pdf"));
        var layer = new AnnotationLayer(session);
        await LoadAllAsync(layer);
        var filling = new FormFilling(layer) { History = new EditHistory(session) };
        Assert.Same(filling.History, layer.History);
        EditHistory history = filling.History!;
        var invalidations = new List<PageInvalidatedEventArgs>();
        layer.Invalidated += (_, e) => invalidations.Add(e);
        byte[] original = await SaveAsync(session);

        // A committed text edit is one step.
        AnnotationTarget name = FormFillingTests.Widget(layer, "name");
        layer.Focus(name);
        Assert.True(filling.SetText("Grace Hopper"));
        Assert.True(filling.Commit());
        Assert.Equal("Edit field", history.UndoName);
        var field = (PdfTextField)filling.Field!;
        byte[] filled = await SaveAsync(session);

        invalidations.Clear();
        Assert.True(history.Undo());
        Assert.Equal("Ada", field.Value);
        Assert.Equal("Ada", filling.Text); // the focused field's edit starts again from the restored value
        Assert.All(invalidations, e => Assert.Equal(DisplayLayers.FormFields, e.Layers));
        Assert.NotEmpty(invalidations);
        await AssertSameObjectsAsync(original, await SaveAsync(session));
        Assert.True(history.Redo());
        Assert.Equal("Grace Hopper", field.Value);
        Assert.Equal(filled, await SaveAsync(session));

        // A reset of several fields is one step too.
        PdfAcroForm form = session.Document.AcroForm!;
        Assert.True(layer.ResetFields(form.TerminalFields.Where(f => f is PdfTextField or PdfChoiceField)) > 0);
        Assert.Equal("Reset form", history.UndoName);
        byte[] reset = await SaveAsync(session);
        Assert.True(history.Undo());
        await AssertSameObjectsAsync(filled, await SaveAsync(session)); // the reset's new appearances stay, unreferenced
        Assert.True(history.Redo());
        Assert.Equal(reset, await SaveAsync(session));

        // A toggle, undone through the layer.
        layer.Focus(FormFillingTests.Widget(layer, "agree"));
        Assert.True(layer.ToggleField());
        Assert.Equal("Toggle field", history.UndoName);
        byte[] toggled = await SaveAsync(session);
        Assert.True(history.Undo());
        Assert.Equal(reset, await SaveAsync(session));
        Assert.True(history.Redo());
        Assert.Equal(toggled, await SaveAsync(session));
    }

    [Fact]
    public async Task Undoing_abandons_an_uncommitted_edit()
    {
        await using DocumentSession session = Open(Corpus.Bytes("acroform-fields.pdf"));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await LoadAllAsync(layer);
        var filling = new FormFilling(layer);
        layer.Focus(FormFillingTests.Widget(layer, "name"));
        Assert.True(filling.SetText("First"));
        Assert.True(filling.Commit());
        Assert.True(filling.SetText("Typed, not committed"));
        Assert.True(filling.IsDirty);

        Assert.True(layer.History!.Undo());
        Assert.False(filling.IsDirty);
        Assert.Equal("Ada", filling.Text);
        Assert.Equal("Ada", ((PdfTextField)filling.Field!).Value);
    }

    [Fact]
    public async Task Value_edits_of_one_field_merge_within_the_window()
    {
        var clock = new InputModelTests.ManualClock();
        await using DocumentSession session = Open(Corpus.Bytes("acroform-fields.pdf"));
        var layer = new AnnotationLayer(session)
        {
            History = new EditHistory(session, new EditHistoryOptions().WithMergeWindow(TimeSpan.FromSeconds(1)).UseTimeProvider(clock)),
        };
        await LoadAllAsync(layer);
        AnnotationTarget name = FormFillingTests.Widget(layer, "name");
        var field = (PdfTextField)((PdfWidgetAnnotation)layer.GetAnnotation(name)).Field!;

        Assert.True(layer.SetFieldText(name, "G"));
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(layer.SetFieldText(name, "Gr"));
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(layer.SetFieldText(name, "Gra"));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(layer.SetFieldText(name, "Grace"));

        EditHistory history = layer.History!;
        Assert.True(history.Undo());
        Assert.Equal("Gra", field.Value);
        Assert.True(history.Undo());
        Assert.Equal("Ada", field.Value);
        Assert.False(history.CanUndo);
        Assert.True(history.Redo());
        Assert.Equal("Gra", field.Value);
    }

    [Fact]
    public async Task A_step_whose_state_was_changed_outside_the_history_is_reported_and_dropped()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await layer.ReadPageAsync(0, Token);
        AnnotationTarget square = Assert.NotNull(layer.HitTest(new PagePoint(0, 234, 30)));
        Assert.True(layer.Move(square, 10, 0));
        Assert.True(layer.Move(square, 10, 0));
        Assert.True(layer.History!.Undo());
        Assert.True(layer.History.CanRedo);

        // An edit around the layer: the history cannot know of it.
        using (session.BeginEdit())
        {
            layer.GetAnnotation(square).Move(0, 50);
        }

        Assert.False(layer.History.Undo());
        Assert.False(layer.History.CanRedo);
        Diagnostic diagnostic = Assert.Single(session.Document.Diagnostics, d => d.Code == "ViewerEditHistoryRestoreFailed");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Move annotation", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_whose_page_was_removed_is_dropped_with_every_older_step()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await layer.ReadPageAsync(0, Token);
        AnnotationTarget square = Assert.NotNull(layer.HitTest(new PagePoint(0, 234, 30)));
        Assert.True(layer.Move(square, 10, 0));
        Assert.True(layer.Move(square, 10, 0));
        using (session.BeginEdit())
        {
            session.Document.Pages.RemoveAt(0);
        }

        Assert.False(layer.History!.Undo());
        Assert.False(layer.History.CanUndo);
    }

    [Fact]
    public async Task Closing_the_session_clears_the_history()
    {
        DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var history = new EditHistory(session);
        history.Record(new LoggedStep("1", []));
        await session.DisposeAsync();
        Assert.False(history.CanUndo);
        Assert.False(history.Undo());
        Assert.Null(history.UndoName);
    }

    [Fact]
    public async Task A_history_of_another_session_is_refused()
    {
        await using DocumentSession session = Open(AnnotationSamples.Grid(2, 1));
        await using DocumentSession other = Open(AnnotationSamples.Grid(2, 1));
        var layer = new AnnotationLayer(session);
        Assert.Throws<ArgumentException>(() => layer.History = new EditHistory(other));
    }

    private static async Task<byte[]> SaveAsync(DocumentSession session)
    {
        using var stream = new MemoryStream();
        await session.Document.SaveAsync(stream, Token);
        return stream.ToArray();
    }

    /// <summary>
    /// Compares two saves object by object from the catalog down (an object the undo wrote back is serialized afresh, where the save
    /// before the edit copied it): the same objects under the same numbers, with the same entries and stream data. The metadata
    /// stream is left out: a save of a changed document writes its modification date there, whatever the change was.
    /// </summary>
    private static async Task AssertSameObjectsAsync(byte[] expected, byte[] actual)
    {
        using PdfDocument left = await PdfDocument.OpenAsync(expected, Token);
        using PdfDocument right = await PdfDocument.OpenAsync(actual, Token);
        var seen = new HashSet<int>();
        Compare(Without(left.Catalog, "Metadata"), Without(right.Catalog, "Metadata"), "catalog");

        static CosDictionary Without(CosDictionary dictionary, string key)
        {
            var copy = new CosDictionary();
            foreach ((CosName name, CosObject value) in dictionary)
            {
                if (name.Value != key)
                {
                    copy.Add(name, value);
                }
            }

            return copy;
        }

        void Compare(CosObject a, CosObject b, string path)
        {
            switch (a)
            {
                case CosReference reference:
                    Assert.Equal(reference, b);
                    if (seen.Add(reference.ObjectNumber))
                    {
                        Compare(left.Resolve(a), right.Resolve(b), $"{reference.ObjectNumber} 0 R");
                    }

                    break;
                case CosDictionary dictionary:
                    var other = Assert.IsType<CosDictionary>(b);
                    Assert.Equal(dictionary.Keys.Select(k => k.Value).Order(StringComparer.Ordinal), other.Keys.Select(k => k.Value).Order(StringComparer.Ordinal));
                    foreach ((CosName key, CosObject value) in dictionary)
                    {
                        Compare(value, other[key], $"{path}/{key}");
                    }

                    break;
                case CosArray array:
                    var items = Assert.IsType<CosArray>(b);
                    Assert.Equal(array.Count, items.Count);
                    for (int i = 0; i < array.Count; i++)
                    {
                        Compare(array[i], items[i], $"{path}[{i}]");
                    }

                    break;
                case CosStream stream:
                    var data = Assert.IsType<CosStream>(b);
                    Compare(stream.Dictionary, data.Dictionary, path);
                    Assert.True(stream.EncodedData.Span.SequenceEqual(data.EncodedData.Span), path);
                    break;
                default:
                    Assert.True(CosObject.DeepEquals(a, b), path);
                    break;
            }
        }
    }

    private static async Task LoadAllAsync(AnnotationLayer layer)
    {
        for (int page = 0; page < layer.Session.PageCount; page++)
        {
            await layer.ReadPageAsync(page, Token);
        }
    }

    // Saves are compared byte for byte, so the documents write their dates from a fixed clock.
    private static DocumentSession Open(byte[] file) =>
        DocumentSession.Open(file, new ViewerOptions().WithDocumentOptions(new PdfOptions().UseTimeProvider(FixedTimeProvider.Instance)).WithThreading(ViewerThreading.Cooperative));

    private sealed class LoggedStep(string name, List<string> log) : IEditStep
    {
        public string Name => name;

        public IReadOnlyList<PageInvalidation> Undo(PdfDocument document)
        {
            log.Add("undo " + name);
            return [];
        }

        public IReadOnlyList<PageInvalidation> Redo(PdfDocument document)
        {
            log.Add("redo " + name);
            return [];
        }
    }
}
