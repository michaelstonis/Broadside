using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Actions;

/// <summary>Actions that break a "shall" of ISO 32000-2 §12.6 read leniently with a diagnostic, and throw in strict mode only when read.</summary>
public sealed class MalformedActionTests
{
    private static readonly CosReference Owner = new(3, 0);

    public static TheoryData<string, string> MissingRequiredEntries => new()
    {
        { "/S /GoToR /D [0 /Fit]", "ActionInvalid" },
        { "/S /GoToE /D (x)", "ActionInvalid" },
        { "/S /GoToDp", "ActionInvalid" },
        { "/S /Launch", "ActionInvalid" },
        { "/S /Thread", "ActionInvalid" },
        { "/S /Movie", "ActionInvalid" },
        { "/S /Movie /T (a) /Annotation << >>", "ActionInvalid" },
        { "/S /Hide", "ActionInvalid" },
        { "/S /Named", "ActionInvalid" },
        { "/S /SubmitForm", "ActionInvalid" },
        { "/S /ImportData", "ActionInvalid" },
        { "/S /SetOCGState", "ActionInvalid" },
        { "/S /Rendition", "ActionInvalid" },
        { "/S /Rendition /OP 7", "ActionInvalid" },
        { "/S /Rendition /OP 1", "ActionInvalid" },
        { "/S /Rendition /OP 0 /AN << >>", "ActionInvalid" },
        { "/S /Trans", "ActionInvalid" },
        { "/S /GoTo3DView /TA << >>", "ActionInvalid" },
        { "/S /JavaScript", "ActionInvalid" },
        { "/S /RichMediaExecute /TA << >>", "ActionInvalid" },
        { "/S /Thread /D true", "ActionEntryInvalid" },
    };

    [Theory]
    [MemberData(nameof(MissingRequiredEntries))]
    public void An_action_missing_a_required_entry_is_read_with_a_warning(string entries, string code)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.NotNull(document.GetAction(Parse(entries), Owner));

        Assert.Equal([code], Warnings(document).Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_rendition_with_a_script_and_an_unknown_operation_is_valid()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        var rendition = (PdfRenditionAction)document.GetAction(Parse("/S /Rendition /OP 9 /JS (x\\(\\);)"), Owner)!;

        Assert.Equal((9, (PdfRenditionOperation?)null, "x();"), (rendition.OperationValue, rendition.Operation, rendition.Script));
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void Entries_of_the_wrong_type_read_as_absent_or_default_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        var sound = (PdfSoundAction)document.GetAction(Parse("/S /Sound /Sound 5 /Volume 3 /Mix 1"), Owner)!;
        Assert.Null(sound.Sound);
        Assert.Equal(1.0, sound.Volume);
        Assert.False(sound.Mixes);

        var movie = (PdfMovieAction)document.GetAction(Parse("/S /Movie /T (clip) /Operation /Rewind"), new CosReference(4, 0))!;
        Assert.Equal(PdfMovieOperation.Play, movie.Operation);

        var script = (PdfJavaScriptAction)document.GetAction(Parse("/S /JavaScript /JS 12"), new CosReference(5, 0))!;
        Assert.Null(script.Script);

        var remote = (PdfRemoteGoToAction)document.GetAction(Parse("/S /GoToR /F 1 /D [0 /Fit]"), new CosReference(6, 0))!;
        Assert.Null(remote.File);

        var state = (PdfSetOcgStateAction)document.GetAction(Parse("/S /SetOCGState /State [<< >> /Dim << >> 3 /ON << >>]"), new CosReference(7, 0))!;
        Assert.Equal([PdfOcgStateOperation.On], state.Changes.Select(change => change.Operation));

        Assert.Equal(
            ["ActionEntryInvalid 3", "ActionEntryInvalid 4", "ActionEntryInvalid 5", "ActionEntryInvalid 6", "ActionEntryInvalid 7"],
            Warnings(document).Select(diagnostic => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}"));
    }

    [Fact]
    public void A_uri_that_is_not_utf8_or_not_a_string_is_read_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        var utf8 = (PdfUriAction)document.GetAction(Parse("/S /URI /URI (https://example.com/\xC3\xA9t\xC3\xA9)"), new CosReference(3, 0))!;
        var latin1 = (PdfUriAction)document.GetAction(Parse("/S /URI /URI (https://example.com/caf\xE9)"), new CosReference(4, 0))!;
        var utf16 = (PdfUriAction)document.GetAction(Parse("/S /URI /URI <FEFF0061002F0062>"), new CosReference(5, 0))!;
        var name = (PdfUriAction)document.GetAction(Parse("/S /URI /URI /relative"), new CosReference(6, 0))!;

        Assert.Equal("https://example.com/été", utf8.Uri);
        Assert.Equal("https://example.com/café", latin1.Uri);
        Assert.Equal("a/b", utf16.Uri);
        Assert.Equal("relative", name.Uri);
        Assert.Null(name.ResolveUri());
        Assert.Equal(new Uri("https://example.com/%C3%A9t%C3%A9"), utf8.ResolveUri());
        Assert.Equal(
            ["UriInvalid 4", "UriInvalid 5", "UriInvalid 6"],
            Warnings(document).Select(diagnostic => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}"));
    }

    [Fact]
    public void A_cycle_of_embedded_target_dictionaries_is_refused_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        CosDictionary action = Parse("/S /GoToE /D (x) /T << /R /C /N (a) >>");
        var target = (CosDictionary)action[new CosName("T")];
        target[new CosName("T")] = target;

        var goTo = (PdfEmbeddedGoToAction)document.GetAction(action, Owner)!;

        Assert.Null(goTo.Target!.Target);
        Assert.Equal("ActionTargetCycle", Assert.Single(Warnings(document)).Code);
    }

    [Fact]
    public void Strict_mode_throws_when_an_invalid_action_is_read_not_when_the_document_opens()
    {
        byte[] file = TestPdfWithOpenAction("<< /S /GoTo >>");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.OpenAction);

        Assert.Equal("ActionInvalid", error.Diagnostic.Code);
    }

    [Fact]
    public void Strict_mode_records_out_of_scope_scripts_without_throwing()
    {
        byte[] file = TestPdfWithOpenAction("<< /S /JavaScript /JS (x\\(\\);) >>");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        Assert.Equal("x();", Assert.IsType<PdfJavaScriptAction>(document.OpenAction).Script);
        Assert.Equal(DiagnosticSeverity.Information, Assert.Single(document.Diagnostics).Severity);
    }

    private static CosDictionary Parse(string entries) =>
        (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes("<< " + entries + " >>"));

    private static IEnumerable<Diagnostic> Warnings(PdfDocument document) => ActionEntryTests.Warnings(document);

    private static byte[] TestPdfWithOpenAction(string action) =>
        Document.TestPdf.OnePage(pageEntries: "", catalogEntries: " /OpenAction " + action);
}
