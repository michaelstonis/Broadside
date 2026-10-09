using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Actions;

/// <summary>
/// Actions the library keeps as data (ISO 32000-2 §12.6.4.9, §12.6.4.10, §12.6.4.14, §12.6.4.17, §12.6.4.18): reading them through
/// every typed view changes nothing, and saving the document copies them byte for byte (<c>actions-preserved.pdf</c>).
/// </summary>
public class PreservedActionTests
{
    private static readonly CosName A = new("A");

    private static readonly int[] ActionObjects = [10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 23, 24, 27];

    private static readonly int[] OwnerObjects = [1, 3, 19, 21, 22, 30, 31, 32, 33, 34, 35, 36, 37];

    [Fact]
    public void Scripts_sounds_movies_renditions_and_rich_media_read_as_data_in_their_stored_forms()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-preserved.pdf"));

        Assert.Equal(Expected, Read(document));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic is { Code: "ActionCycle", Severity: DiagnosticSeverity.Information });
    }

    [Fact]
    public void Reading_every_action_changes_no_object()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-preserved.pdf"));

        _ = Read(document);

        Assert.All(ActionObjects.Concat(OwnerObjects), number => Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty, $"object {number}"));
    }

    [Fact]
    public void Saving_after_reading_every_action_keeps_their_bytes_and_their_values()
    {
        byte[] source = Corpus.Bytes("actions-preserved.pdf");
        using var saved = new MemoryStream();
        using (PdfDocument document = PdfDocument.Open(source))
        {
            _ = Read(document);
            document.Save(saved);
        }

        Dictionary<(int Number, int Generation), byte[]> before = ObjectBodies.Read(source);
        Dictionary<(int Number, int Generation), byte[]> after = ObjectBodies.Read(saved.ToArray());
        Assert.All(ActionObjects.Concat(OwnerObjects), number => Assert.Equal(before[(number, 0)], after[(number, 0)]));
        Assert.True(after[(10, 0)].AsSpan().IndexOf("\\053\\r\\n// one \\\nline"u8) >= 0);
        Assert.True(after[(13, 0)].AsSpan().IndexOf("/Java#53cript"u8) >= 0);
        Assert.True(after[(14, 0)].AsSpan().IndexOf("/Volume .50"u8) >= 0);

        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(Expected, Read(reopened));
        Assert.Equal(
            ["ActionCycle"],
            reopened.Diagnostics.Where(diagnostic => diagnostic.Code != "ActionOutOfScope").Select(diagnostic => diagnostic.Code));
    }

    private static IReadOnlyList<string> Expected =>
    [
        "open JavaScript app.alert('flate');",
        "page-open JavaScript app.alert('hex') ",
        "doc JavaScript var x = 1;",
        "30 JavaScript app.alert(\"hi\");+\r\n// one line",
        "31 JavaScript app.alert('hex') ",
        "32 JavaScript var x = 1;",
        "33 Sound 0.5 False False True 4",
        "34 Rendition PlayOrResume play(); MR 21",
        "35 Movie Play 19",
        "36 RichMediaExecute rewind 0 22 26",
        "37 GoTo,GoTo",
    ];

    /// <summary>Reads every preserved action through the public model and describes what it read.</summary>
    private static List<string> Read(PdfDocument document)
    {
        var read = new List<string>
        {
            $"open {Describe(document.OpenAction!)}",
            $"page-open {Describe(document.Pages[0].AdditionalActions!.Open!)}",
            $"doc {Describe(document.Names!.JavaScriptActions.Single().Value)}",
        };
        for (int number = 30; number <= 37; number++)
        {
            var link = (CosDictionary)document.Resolve(new CosReference(number, 0));
            PdfAction action = document.GetAction(link[A], new CosReference(number, 0))!;
            read.Add($"{number} {(action is PdfGoToAction ? string.Join(",", action.EnumerateActionTree().Select(next => next.Kind)) : Describe(action))}");
        }

        return read;
    }

    private static string Describe(PdfAction action) => action switch
    {
        PdfJavaScriptAction script => $"JavaScript {script.Script}",
        PdfSoundAction sound => $"Sound {sound.Volume} {sound.IsSynchronous} {sound.Repeats} {sound.Mixes} {sound.Sound!.EncodedData.Length}",
        PdfRenditionAction rendition =>
            $"Rendition {rendition.Operation} {rendition.Script} {((CosName)rendition.Rendition![new CosName("S")]).Value} {Number(rendition.Dictionary, "AN")}",
        PdfMovieAction movie => $"Movie {movie.Operation} {Number(movie.Dictionary, "Annotation")}",
        PdfRichMediaExecuteAction richMedia =>
            $"RichMediaExecute {richMedia.Command!.Name} {string.Join(" ", richMedia.Command.Arguments.Select(argument => ((CosNumber)argument).ToDouble()))} {Number(richMedia.Dictionary, "TA")} {Number(richMedia.Dictionary, "TI")}",
        _ => action.Kind.ToString(),
    };

    private static int Number(CosDictionary dictionary, string key) => ((CosReference)dictionary[new CosName(key)]).ObjectNumber;

}
