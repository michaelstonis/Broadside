using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Sanitizing a document before it is passed on (issue #648): after <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/>, a save
/// with <see cref="PdfSanitization.CreateSaveOptions"/> and a reopen, a walk over every object reachable from the trailer finds no
/// action of a removed kind, no embedded or associated file, no XFA, no metadata and no page-piece dictionary. ISO 32000-2 §12.6,
/// §7.7.4, §12.7.3, §7.11.4, §14.13, §14.3, §14.5 and §12.3.4.
/// </summary>
public sealed class SanitizeTests
{
    private static readonly HashSet<string> DefaultRemovedTypes =
        ["JavaScript", "Launch", "SubmitForm", "ImportData", "GoToR", "GoToE", "Movie", "Sound", "Rendition", "RichMediaExecute", "GoTo3DView"];

    private static readonly CosName S = new("S");
    private static readonly CosName A = new("A");
    private static readonly CosName Next = new("Next");

    public static TheoryData<string> Files => new(
        "actions-all.pdf",
        "actions-preserved.pdf",
        "embedded-files.pdf",
        "associated-files.pdf",
        "metadata-xmp.pdf",
        "metadata-xmp-forms.pdf",
        "acroform-xfa.pdf",
        "optional-content.pdf",
        "text-render-modes.pdf");

    [Theory]
    [MemberData(nameof(Files))]
    public void After_a_save_and_reopen_nothing_removed_is_reachable(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        PdfSanitization result = document.Sanitize();
        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));

        Assert.False(reopened.Trailer.ContainsKey(new CosName("Info")));
        foreach (CosDictionary dictionary in ReachableDictionaries(reopened))
        {
            if (dictionary.TryGetValue(S, out CosObject? type) && reopened.Resolve(type) is CosName name && LooksLikeAction(dictionary))
            {
                Assert.DoesNotContain(name.Value, DefaultRemovedTypes);
            }

            foreach (string key in (string[])["EmbeddedFiles", "AF", "EF", "XFA", "Metadata", "PieceInfo", "JavaScript", "Collection", "NeedsRendering", "Thumb"])
            {
                Assert.False(dictionary.ContainsKey(new CosName(key)), $"{fileName}: a {key} entry is still reachable.");
            }
        }
    }

    [Fact]
    public void Actions_all_loses_every_default_kind_and_keeps_the_rest()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("actions-all.pdf"));
        PdfSanitization result = document.Sanitize();

        Assert.Contains("JavaScript actions: 22", result.Removed);
        Assert.Contains("Launch actions: 1", result.Removed);
        Assert.Contains("GoToR actions: 1", result.Removed);
        Assert.Contains("GoToE actions: 1", result.Removed);
        Assert.Contains("SubmitForm actions: 1", result.Removed);
        Assert.Contains("RichMediaExecute actions: 1", result.Removed);
        Assert.Contains("document-level scripts (JavaScript name tree): 1", result.Removed);
        Assert.Contains("link annotations left without an action: 11", result.Removed);
        Assert.DoesNotContain(result.Removed, line => line.StartsWith("URI actions", StringComparison.Ordinal));

        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));
        string[] kept = [.. Actions(reopened).Select(action => action.ActionType.Value).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(["GoTo", "GoToDp", "Hide", "Named", "ResetForm", "SetOCGState", "Thread", "Trans", "URI"], kept);
        Assert.Equal(PdfActionKind.GoTo, reopened.OpenAction!.Kind);
        Assert.Null(reopened.Catalog.GetValueOrDefault(new CosName("AA")));
    }

    [Fact]
    public void All_removes_uri_actions_too()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("actions-all.pdf"));
        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithActions(PdfSanitizeActions.All));

        Assert.Contains(result.Removed, line => line.StartsWith("URI actions", StringComparison.Ordinal));
        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));
        Assert.DoesNotContain(Actions(reopened), action => action.Kind == PdfActionKind.Uri);
    }

    [Fact]
    public void A_widgets_additional_actions_keep_the_triggers_that_are_not_scripts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("actions-all.pdf"));
        CosDictionary widget = document.Pages[0].Annotations.Single(annotation => annotation.Kind == PdfAnnotationKind.Widget).Dictionary;
        var triggers = (CosDictionary)document.Resolve(widget[new CosName("AA")]);
        string[] scripts = [.. triggers.Where(entry => ((CosName)((CosDictionary)document.Resolve(entry.Value))[S]).Value == "JavaScript").Select(entry => entry.Key.Value)];
        string[] others = [.. triggers.Keys.Select(key => key.Value).Except(scripts)];
        Assert.NotEmpty(scripts);

        document.Sanitize();

        string[] left = widget.TryGetValue(new CosName("AA"), out CosObject? value) ? [.. ((CosDictionary)document.Resolve(value)).Keys.Select(key => key.Value)] : [];
        Assert.Equal(others.Order(StringComparer.Ordinal), left.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Actions_preserved_removes_scripts_media_and_rich_media_and_ends_the_next_cycle()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("actions-preserved.pdf"));
        PdfSanitization result = document.Sanitize();

        Assert.Contains(result.Removed, line => line.StartsWith("JavaScript actions", StringComparison.Ordinal));
        Assert.Contains("Sound actions: 1", result.Removed);
        Assert.Contains("Rendition actions: 1", result.Removed);
        Assert.Contains("Movie actions: 1", result.Removed);
        Assert.Contains("RichMediaExecute actions: 1", result.Removed);
        Assert.Null(document.OpenAction);
        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));
        Assert.All(Actions(reopened), action => Assert.Equal(PdfActionKind.GoTo, action.Kind));
    }

    [Fact]
    public void A_removed_action_is_replaced_by_the_actions_after_it()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfAnnotation link = document.Pages[0].AddLinkAnnotation(new PdfRectangle(10, 10, 50, 50), new Uri("https://example.com/"));
        CosDictionary first = GoTo(document), second = GoTo(document), third = GoTo(document);
        second[Next] = document.AddObject(Script("app.alert(2)"));
        var head = Script("app.alert(1)");
        head[Next] = new CosArray([document.AddObject(first), document.AddObject(second)]);
        ((CosDictionary)document.Resolve(second[Next]))[Next] = document.AddObject(third);
        link.Dictionary[A] = document.AddObject(head);

        PdfSanitization result = document.Sanitize();

        Assert.Equal(["JavaScript actions: 2"], result.Removed);
        Assert.Same(first, document.Resolve(link.Dictionary[A]));
        // The head's Next [first second] runs first, then second, then the script after second, then third: first keeps second
        // after it, and second's script gives way to third.
        Assert.Same(second, document.Resolve(first[Next]) is CosArray next ? document.Resolve(next[0]) : document.Resolve(first[Next]));
        Assert.Same(third, document.Resolve(second[Next]));

        static CosDictionary GoTo(PdfDocument document) => new()
        {
            [S] = new CosName("GoTo"),
            [new CosName("D")] = new CosArray([document.Pages[0].Reference!, new CosName("Fit")]),
        };
    }

    [Fact]
    public void A_damaged_action_is_removed_and_counted_and_its_link_goes_with_it()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfAnnotation link = document.Pages[0].AddLinkAnnotation(new PdfRectangle(10, 10, 50, 50), new Uri("https://example.com/"));
        link.Dictionary[A] = new CosDictionary { [new CosName("JS")] = new CosString("app.alert(1)"u8.ToArray()) };

        PdfSanitization result = document.Sanitize();

        Assert.Equal(["damaged actions (no S): 1", "link annotations left without an action: 1"], result.Removed);
        Assert.Empty(document.Pages[0].Annotations);
    }

    [Fact]
    public void Embedded_and_associated_files_are_removed_with_their_annotations()
    {
        using PdfDocument embedded = PdfDocument.Open(Corpus.Bytes("embedded-files.pdf"));
        PdfSanitization result = embedded.Sanitize();
        Assert.Contains(result.Removed, line => line.StartsWith("embedded files (EmbeddedFiles name tree)", StringComparison.Ordinal));
        Assert.Null(embedded.Names?.EmbeddedFiles);
        Assert.DoesNotContain(embedded.Pages.SelectMany(page => page.Annotations), annotation => annotation.Kind == PdfAnnotationKind.FileAttachment);

        using PdfDocument associated = PdfDocument.Open(Corpus.Bytes("associated-files.pdf"));
        Assert.Contains(associated.Sanitize().Removed, line => line.StartsWith("associated files (AF)", StringComparison.Ordinal));
        Assert.Empty(associated.AssociatedFiles);
    }

    [Fact]
    public void Files_metadata_and_xfa_stay_when_the_options_keep_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"));
        string? title = document.Properties.Title;
        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithRemoveMetadata(false));
        Assert.NotNull(document.Metadata);
        Assert.Equal(title, document.Properties.Title);
        Assert.True(result.CreateSaveOptions().UpdateModificationDate);

        using PdfDocument xfa = PdfDocument.Open(Corpus.Bytes("acroform-xfa.pdf"));
        xfa.Sanitize(new PdfSanitizeOptions().WithRemoveXfa(false));
        Assert.True(xfa.AcroForm!.Dictionary.ContainsKey(new CosName("XFA")));
    }

    [Fact]
    public void Metadata_is_removed_and_the_save_writes_none_again()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"));
        PdfSanitization result = document.Sanitize();
        Assert.Contains("metadata streams (Metadata): 1", result.Removed);
        PdfSaveOptions options = result.CreateSaveOptions();
        Assert.False(options.IncrementalUpdate);
        Assert.True(options.RemoveUnreachableObjects);
        Assert.False(options.UpdateModificationDate);

        using PdfDocument reopened = PdfDocument.Open(Save(document, options));
        Assert.Null(reopened.Metadata);
        Assert.Null(reopened.Information);
        Assert.Null(reopened.Properties.Producer);
    }

    [Fact]
    public void Xfa_is_removed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-xfa.pdf"));
        Assert.Contains("XFA forms: 1", document.Sanitize().Removed);
        Assert.False(document.AcroForm!.Dictionary.ContainsKey(new CosName("XFA")));
        Assert.NotEmpty(document.AcroForm.TerminalFields);
    }

    [Fact]
    public void Nothing_is_removed_when_every_option_is_off()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("actions-all.pdf"));
        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions()
            .WithActions(PdfSanitizeActions.None)
            .WithRemoveXfa(false)
            .WithRemoveEmbeddedFiles(false)
            .WithRemoveMetadata(false)
            .WithRemoveThumbnails(false)
            .WithOptionalContent(PdfSanitizeOptionalContent.Keep));

        Assert.Empty(result.Removed);
        Assert.False(document.Catalog.IsDirty);
    }

    [Fact]
    public void Comments_are_removed_only_when_asked()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].AddTextAnnotation(new PdfRectangle(10, 10, 30, 30), "note");
        document.Pages[0].AddLinkAnnotation(new PdfRectangle(40, 10, 60, 30), new Uri("https://example.com/"));

        Assert.Empty(document.Sanitize().Removed);
        Assert.Equal(2, document.Pages[0].Annotations.Count);

        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithRemoveComments(true));
        Assert.Equal(["comments (markup annotations and pop-ups): 1"], result.Removed);
        Assert.Equal(PdfAnnotationKind.Link, Assert.Single(document.Pages[0].Annotations).Kind);
    }

    [Fact]
    public void A_signed_document_is_noted_as_invalidated_and_keeps_its_signature_field()
    {
        using var certificate = Signatures.TestCertificates.SelfSigned("CN=Sanitize");
        using PdfDocument unsigned = PdfDocument.Open(Corpus.Bytes("actions-all.pdf"));
        using var output = new MemoryStream();
        unsigned.Sign(output, new Broadside.Signatures.PdfCertificateSigner(certificate));

        using PdfDocument signed = PdfDocument.Open(output.ToArray());
        PdfSanitization result = signed.Sanitize();

        Assert.Equal("signatures invalidated", result.Removed[^1]);
        Assert.Contains(signed.AcroForm!.TerminalFields, field => field is Broadside.Forms.PdfSignatureField);
    }

    [Theory]
    [MemberData(nameof(Corpus.PasswordProtectedFiles), MemberType = typeof(Corpus))]
    public void An_encrypted_document_opened_with_the_user_password_is_refused(string fileName, string userPassword)
    {
        using PdfDocument user = new PdfEngine().Open(Corpus.Path(fileName), new PdfPassword(userPassword));
        Assert.Throws<InvalidOperationException>(() => user.Sanitize());

        using PdfDocument owner = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().WithPassword("owner"));
        _ = owner.Sanitize();
    }

    [Fact]
    public void Options_refuse_values_that_are_not_defined()
    {
        var options = new PdfSanitizeOptions();
        Assert.Equal(PdfSanitizeActions.Default, options.Actions);
        Assert.Equal(PdfSanitizeOptionalContent.RemoveHidden, options.OptionalContent);
        Assert.True(options.RemoveXfa && options.RemoveEmbeddedFiles && options.RemoveMetadata && options.RemoveThumbnails);
        Assert.False(options.RemoveInvisibleText || options.RemoveComments);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithActions((PdfSanitizeActions)128));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithOptionalContent((PdfSanitizeOptionalContent)3));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Create().Sanitize(null!));
    }

    /// <summary>Every dictionary (a stream's dictionary for a stream) reachable from the trailer, each once.</summary>
    internal static IEnumerable<CosDictionary> ReachableDictionaries(PdfDocument document)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject>([document.Trailer]);
        while (pending.Count > 0)
        {
            CosObject current = document.Resolve(pending.Pop());
            if (!seen.Add(current))
            {
                continue;
            }

            CosDictionary? dictionary = current switch
            {
                CosStream stream => stream.Dictionary,
                CosDictionary plain => plain,
                _ => null,
            };
            if (dictionary is not null)
            {
                yield return dictionary;
                foreach (CosObject value in dictionary.Values)
                {
                    pending.Push(value);
                }
            }
            else if (current is CosArray array)
            {
                foreach (CosObject item in array)
                {
                    pending.Push(item);
                }
            }
        }
    }

    /// <summary>Every action reachable from the open action, the pages' and annotations' entries and their Next sequences.</summary>
    private static List<PdfAction> Actions(PdfDocument document)
    {
        var actions = new List<PdfAction>();
        foreach (CosDictionary dictionary in ReachableDictionaries(document))
        {
            if (LooksLikeAction(dictionary) && document.GetAction(dictionary) is { } action)
            {
                actions.Add(action);
            }
        }

        return actions;
    }

    /// <summary>An action dictionary, by its S naming a Table 201 type (a transition's or a structure element's S does not).</summary>
    private static bool LooksLikeAction(CosDictionary dictionary) =>
        dictionary.TryGetValue(S, out CosObject? type) && type is CosName name
        && name.Value is "GoTo" or "GoToR" or "GoToE" or "GoToDp" or "Launch" or "Thread" or "URI" or "Sound" or "Movie" or "Hide"
            or "Named" or "SubmitForm" or "ResetForm" or "ImportData" or "SetOCGState" or "Rendition" or "Trans" or "GoTo3DView"
            or "JavaScript" or "RichMediaExecute";

    private static CosDictionary Script(string script) => new()
    {
        [S] = new CosName("JavaScript"),
        [new CosName("JS")] = new CosString(System.Text.Encoding.ASCII.GetBytes(script)),
    };

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}
