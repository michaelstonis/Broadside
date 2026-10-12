using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Actions;

/// <summary>Trigger events (ISO 32000-2 §12.6.3, Tables 197-200), the catalog's OpenAction and the document-level scripts (§7.7.2, §7.7.4), and Next chains (§12.6.2).</summary>
public sealed class TriggerEventTests
{
    private static readonly CosName AA = new("AA");

    [Fact]
    public void The_open_action_is_a_go_to_action_and_the_document_has_no_open_destination()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));

        PdfGoToAction open = Assert.IsType<PdfGoToAction>(document.OpenAction);

        Assert.Equal(PdfDestinationView.Fit, Assert.IsType<PdfExplicitDestination>(open.Destination).View);
        Assert.Null(document.OpenDestination);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_open_action_array_is_the_open_destination()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"));
        document.Catalog[new CosName("OpenAction")] = new CosArray([new CosReference(3, 0), new CosName("FitH"), new CosInteger(700)]);

        PdfExplicitDestination destination = Assert.IsType<PdfExplicitDestination>(document.OpenDestination);

        Assert.Equal((0, PdfDestinationView.FitH, (double?)700), (destination.PageIndex, destination.View, destination.Top));
        Assert.Null(document.OpenAction);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_open_action_that_is_a_name_is_read_as_a_named_destination_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        document.Catalog[new CosName("OpenAction")] = new CosName("Start");

        Assert.Equal("Start", Assert.IsType<PdfNamedDestination>(document.OpenDestination).Text);
        Assert.Null(document.OpenAction);
        Assert.Equal("OpenActionInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Documents_without_actions_have_no_open_action_and_no_additional_actions()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Null(document.OpenAction);
        Assert.Null(document.OpenDestination);
        Assert.Null(document.AdditionalActions);
        Assert.Null(document.Pages[0].AdditionalActions);
        Assert.Null(document.UriBase);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_catalog_page_annotation_and_field_triggers_are_typed_by_their_owner()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));

        PdfDocumentAdditionalActions catalog = document.AdditionalActions!;
        Assert.Equal(
            ["WC();", "WS();", "DS();", "WP();", "DP();"],
            new[] { catalog.WillClose, catalog.WillSave, catalog.DidSave, catalog.WillPrint, catalog.DidPrint }.Select(Script));

        PdfPageAdditionalActions page = document.Pages[0].AdditionalActions!;
        Assert.Equal(PdfNamedOperation.FirstPage, Assert.IsType<PdfNamedAction>(page.Open).Operation);
        Assert.Equal("pageClosed();", Script(page.Close));

        var link = (CosDictionary)document.Resolve(new CosReference(60, 0));
        PdfAnnotationAdditionalActions annotation = document.GetAnnotationAdditionalActions(link[AA], new CosReference(60, 0))!;
        Assert.Equal(
            ["E();", "X();", "D();", "U();", "PO();", "PC();", "PV();", "PI();"],
            new[]
            {
                annotation.CursorEnter, annotation.CursorExit, annotation.MouseDown, annotation.MouseUp,
                annotation.PageOpen, annotation.PageClose, annotation.PageVisible, annotation.PageInvisible,
            }.Select(Script));
        Assert.Null(annotation.Focus);

        // A widget merged with its field: one AA holds both sets, and C means calculate for the field.
        var widget = (CosDictionary)document.Resolve(new CosReference(69, 0));
        PdfFieldAdditionalActions field = document.GetFieldAdditionalActions(widget[AA], new CosReference(69, 0))!;
        Assert.Equal(["K();", "F();", "V();", "C();"], new[] { field.Keystroke, field.Format, field.Validate, field.Calculate }.Select(Script));
        PdfAnnotationAdditionalActions widgetAnnotation = document.GetAnnotationAdditionalActions(widget[AA], new CosReference(69, 0))!;
        Assert.Equal(["Fo();", "Bl();"], new[] { widgetAnnotation.Focus, widgetAnnotation.Blur }.Select(Script));
        Assert.Equal(["K", "F", "V", "C", "Fo", "Bl"], widgetAnnotation.Actions.Select(entry => entry.Key.Value));
        Assert.Empty(ActionEntryTests.Warnings(document));
    }

    [Fact]
    public void An_additional_actions_entry_that_is_not_a_dictionary_reads_as_none_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        document.Pages[0].Dictionary[AA] = new CosInteger(1);

        Assert.Null(document.Pages[0].AdditionalActions);
        Assert.Equal("AdditionalActionsInvalid 3", Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void The_name_dictionary_holds_one_document_level_script()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));

        KeyValuePair<CosString, PdfJavaScriptAction> script = Assert.Single(document.Names!.JavaScriptActions);

        Assert.Equal("init", script.Key.DecodeText());
        Assert.Equal("var initialised = true;", script.Value.Script);
        Assert.NotNull(document.Names.JavaScript);
    }

    [Fact]
    public void The_action_tree_is_walked_depth_first_in_execution_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        PdfAction goTo = ActionTypeTests.LinkActions(document)[0];

        Assert.Equal([PdfActionKind.Named, PdfActionKind.Uri], goTo.Next.Select(action => action.Kind));
        Assert.Equal(
            [PdfActionKind.GoTo, PdfActionKind.Named, PdfActionKind.JavaScript, PdfActionKind.Uri],
            goTo.EnumerateActionTree().Select(action => action.Kind));
        Assert.Equal([PdfActionKind.GoTo], goTo.EnumerateActionTree(maxDepth: 0).Select(action => action.Kind));
        Assert.Equal("ActionTreeTooDeep 40", Describe(Assert.Single(ActionEntryTests.Warnings(document))));
    }

    [Fact]
    public void A_cycle_of_next_actions_is_cut_with_information_and_a_shared_action_is_yielded_once()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        var shared = new CosDictionary { [new CosName("S")] = new CosName("Named"), [new CosName("N")] = new CosName("LastPage") };
        var first = new CosDictionary { [new CosName("S")] = new CosName("Named"), [new CosName("N")] = new CosName("NextPage") };
        var root = new CosDictionary
        {
            [new CosName("S")] = new CosName("Named"),
            [new CosName("N")] = new CosName("FirstPage"),
            [new CosName("Next")] = new CosArray([first, shared, shared]),
        };
        first[new CosName("Next")] = new CosArray([shared, root]);

        PdfAction action = document.GetAction(root)!;

        Assert.Equal(
            [PdfNamedOperation.FirstPage, PdfNamedOperation.NextPage, PdfNamedOperation.LastPage],
            action.EnumerateActionTree().Select(next => ((PdfNamedAction)next).Operation));
        Diagnostics.Diagnostic cycle = Assert.Single(document.Diagnostics);
        Assert.Equal(("ActionCycle", Diagnostics.DiagnosticSeverity.Information), (cycle.Code, cycle.Severity));
    }

    [Fact]
    public void A_next_entry_with_elements_that_are_not_actions_skips_them_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        var root = new CosDictionary
        {
            [new CosName("S")] = new CosName("Named"),
            [new CosName("N")] = new CosName("FirstPage"),
            [new CosName("Next")] = new CosArray([new CosInteger(1), new CosDictionary { [new CosName("S")] = new CosName("Trans"), [new CosName("Trans")] = new CosDictionary() }]),
        };

        PdfAction action = document.GetAction(root, new CosReference(3, 0))!;

        Assert.Equal(PdfActionKind.Transition, Assert.Single(action.Next).Kind);
        Assert.Equal("ActionNextInvalid 3", Describe(Assert.Single(document.Diagnostics)));
    }

    private static string? Script(PdfAction? action) => Assert.IsType<PdfJavaScriptAction>(action).Script;

    private static string Describe(Diagnostics.Diagnostic diagnostic) => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}";
}
