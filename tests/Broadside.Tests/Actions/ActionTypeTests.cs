using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Actions;

/// <summary>Every action type of ISO 32000-2 Table 201 (§12.6.4, §12.7.6), read from <c>actions-all.pdf</c>.</summary>
public class ActionTypeTests
{
    private static readonly CosName A = new("A");

    [Fact]
    public void Actions_all_pdf_types_each_table_201_action_in_table_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));

        IReadOnlyList<PdfAction> actions = LinkActions(document);

        Assert.Equal(
            [
                PdfActionKind.GoTo, PdfActionKind.GoToR, PdfActionKind.GoToE, PdfActionKind.GoToDp, PdfActionKind.Launch,
                PdfActionKind.Thread, PdfActionKind.Uri, PdfActionKind.Sound, PdfActionKind.Movie, PdfActionKind.Hide,
                PdfActionKind.Named, PdfActionKind.SubmitForm, PdfActionKind.ResetForm, PdfActionKind.ImportData,
                PdfActionKind.SetOcgState, PdfActionKind.Rendition, PdfActionKind.Transition, PdfActionKind.GoTo3DView,
                PdfActionKind.JavaScript, PdfActionKind.RichMediaExecute,
            ],
            actions.Select(action => action.Kind));
        Assert.Equal(
            [
                typeof(PdfGoToAction), typeof(PdfRemoteGoToAction), typeof(PdfEmbeddedGoToAction), typeof(PdfGoToDocumentPartAction),
                typeof(PdfLaunchAction), typeof(PdfThreadAction), typeof(PdfUriAction), typeof(PdfSoundAction), typeof(PdfMovieAction),
                typeof(PdfHideAction), typeof(PdfNamedAction), typeof(PdfSubmitFormAction), typeof(PdfResetFormAction),
                typeof(PdfImportDataAction), typeof(PdfSetOcgStateAction), typeof(PdfRenditionAction), typeof(PdfTransitionAction),
                typeof(PdfGoTo3DViewAction), typeof(PdfJavaScriptAction), typeof(PdfRichMediaExecuteAction),
            ],
            actions.Select(action => action.GetType()));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > Diagnostics.DiagnosticSeverity.Information);
    }

    [Fact]
    public void An_action_type_the_table_does_not_list_is_kept_as_an_unknown_action()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        var dictionary = new CosDictionary { [new CosName("S")] = new CosName("SetAppearance"), [new CosName("X")] = new CosInteger(1) };

        PdfAction action = Assert.IsType<PdfUnknownAction>(document.GetAction(dictionary));

        Assert.Equal(PdfActionKind.Unknown, action.Kind);
        Assert.Equal(new CosName("SetAppearance"), action.ActionType);
        Assert.Same(dictionary, action.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    /// <summary>The actions of the link annotations 40 to 59, in page order.</summary>
    internal static IReadOnlyList<PdfAction> LinkActions(PdfDocument document) =>
        [.. Enumerable.Range(40, 20).Select(number => document.GetAction(((CosDictionary)document.Resolve(new CosReference(number, 0)))[A])!)];
}
