using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Actions;

/// <summary>The entries of each action type (ISO 32000-2 §12.6.4.2 to §12.6.4.18, §12.7.6.2 to §12.7.6.4), read from <c>actions-all.pdf</c>.</summary>
public class ActionEntryTests
{
    [Fact]
    public void Go_to_actions_read_their_local_remote_and_embedded_destinations()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        IReadOnlyList<PdfAction> actions = ActionTypeTests.LinkActions(document);

        var goTo = (PdfGoToAction)actions[0];
        Assert.Equal(0, Assert.IsType<PdfExplicitDestination>(goTo.Destination).PageIndex);
        Assert.Equal(PdfDestinationTarget.StructureElement, goTo.StructureDestination!.TargetKind);

        var remote = (PdfRemoteGoToAction)actions[1];
        Assert.Equal("other.pdf", remote.File!.FileName);
        PdfExplicitDestination remoteDestination = Assert.IsType<PdfExplicitDestination>(remote.Destination);
        Assert.True(remoteDestination.IsRemote);
        Assert.Equal((0, (int?)null, PdfDestinationView.Fit), (remoteDestination.PageNumber, remoteDestination.PageIndex, remoteDestination.View));
        Assert.True(remote.NewWindow);
        Assert.Null(remote.StructureDestination);

        var embedded = (PdfEmbeddedGoToAction)actions[2];
        Assert.Null(embedded.File);
        Assert.False(embedded.NewWindow);
        PdfNamedDestination chapter = Assert.IsType<PdfNamedDestination>(embedded.Destination);
        Assert.Equal(("Chapter 1", true), (chapter.Text, chapter.IsRemote));
        PdfEmbeddedTarget parent = embedded.Target!;
        Assert.Equal(PdfEmbeddedTargetRelationship.Parent, parent.Relationship);
        PdfEmbeddedTarget child = parent.Target!;
        Assert.Equal((PdfEmbeddedTargetRelationship.Child, "embedded.pdf"), (child.Relationship, child.EmbeddedFileName!.DecodeText()));
        PdfEmbeddedTarget attachment = child.Target!;
        Assert.Equal((0, (int?)null, "attached"), (attachment.PageNumber, attachment.AnnotationIndex, attachment.AnnotationName));
        Assert.Null(attachment.PageName);
        Assert.Null(attachment.Target);

        var part = (PdfGoToDocumentPartAction)actions[3];
        Assert.Same(document.Resolve(new CosReference(82, 0)), part.DocumentPart);
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void Launch_and_thread_actions_read_their_files_platform_parameters_and_article_indexes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        IReadOnlyList<PdfAction> actions = ActionTypeTests.LinkActions(document);

        var launch = (PdfLaunchAction)actions[4];
        Assert.Equal("readme.txt", launch.File!.FileName);
        Assert.True(launch.NewWindow);
        PdfWindowsLaunchParameters windows = launch.Windows!;
        Assert.Equal(("notepad.exe", @"C:\Temp", "print", "readme.txt"),
            (windows.FileName!.DecodeText(), windows.DefaultDirectory!.DecodeText(), windows.Operation, windows.Parameters!.DecodeText()));
        Assert.Null(launch.Mac);
        Assert.Null(launch.Unix);

        var thread = (PdfThreadAction)actions[5];
        Assert.Equal((0, 0), (thread.ThreadIndex, thread.BeadIndex));
        Assert.Null(thread.ThreadDictionary);
        Assert.Null(thread.ThreadTitle);
        Assert.Null(thread.File);
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void A_relative_uri_is_resolved_against_the_catalog_base_without_being_fetched()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        var uri = (PdfUriAction)ActionTypeTests.LinkActions(document)[6];

        Assert.Equal("docs/index.html", uri.Uri);
        Assert.True(uri.IsMap);
        Assert.Equal(new Uri("https://example.com/"), document.UriBase);
        Assert.Equal(new Uri("https://example.com/docs/index.html"), uri.ResolveUri());
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void Multimedia_actions_are_exposed_as_data_with_their_defaults()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        IReadOnlyList<PdfAction> actions = ActionTypeTests.LinkActions(document);

        var sound = (PdfSoundAction)actions[7];
        Assert.Equal(4, sound.Sound!.EncodedData.Length);
        Assert.Equal((0.25, true, false, true), (sound.Volume, sound.IsSynchronous, sound.Repeats, sound.Mixes));

        var movie = (PdfMovieAction)actions[8];
        Assert.Same(document.Resolve(new CosReference(68, 0)), movie.Annotation);
        Assert.Null(movie.Title);
        Assert.Equal(PdfMovieOperation.Pause, movie.Operation);

        var rendition = (PdfRenditionAction)actions[15];
        Assert.Equal((PdfRenditionOperation.Play, 0, "play();"), (rendition.Operation, rendition.OperationValue, rendition.Script));
        Assert.Same(document.Resolve(new CosReference(72, 0)), rendition.ScreenAnnotation);
        Assert.Same(document.Resolve(new CosReference(73, 0)), rendition.Rendition);
        Assert.IsType<CosString>(rendition.ScriptObject);

        var view = (PdfGoTo3DViewAction)actions[17];
        Assert.Same(document.Resolve(new CosReference(74, 0)), view.Annotation);
        Assert.Equal(new CosName("F"), view.ViewKeyword);
        Assert.Null(view.ViewIndex);
        Assert.Null(view.ViewName);
        Assert.Null(view.ViewDictionary);

        var richMedia = (PdfRichMediaExecuteAction)actions[19];
        Assert.Same(document.Resolve(new CosReference(77, 0)), richMedia.Annotation);
        Assert.Same(document.Resolve(new CosReference(78, 0)), richMedia.Instance);
        PdfRichMediaCommand command = richMedia.Command!;
        Assert.Equal("play", command.Name);
        Assert.Equal([new CosString("intro"u8), new CosInteger(2), CosBoolean.True], command.Arguments);
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void Scripts_and_multimedia_are_recorded_as_out_of_scope_information_and_never_as_warnings()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));

        _ = ActionTypeTests.LinkActions(document);

        Assert.Equal(
            ["ActionOutOfScope 47", "ActionOutOfScope 48", "ActionOutOfScope 55", "ActionOutOfScope 57", "ActionOutOfScope 58", "ActionOutOfScope 59"],
            document.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}"));
        Assert.All(document.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
    }

    [Fact]
    public void Hide_named_and_transition_actions_read_their_targets_names_and_transitions()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        IReadOnlyList<PdfAction> actions = ActionTypeTests.LinkActions(document);

        var hide = (PdfHideAction)actions[9];
        Assert.False(hide.Hide);
        Assert.Equal(2, hide.Targets.Count);
        Assert.Equal((new CosReference(40, 0), (string?)null), (hide.Targets[0].Reference, hide.Targets[0].FieldName));
        Assert.Same(document.Resolve(new CosReference(40, 0)), hide.Targets[0].Dictionary);
        Assert.Equal(("email", (CosDictionary?)null), (hide.Targets[1].FieldName, hide.Targets[1].Dictionary));

        var named = (PdfNamedAction)actions[10];
        Assert.Equal((PdfNamedOperation.NextPage, new CosName("NextPage")), (named.Operation, named.Name));
        var print = (PdfNamedAction)Assert.Single(named.Next);
        Assert.Equal((PdfNamedOperation.Other, new CosName("Print")), (print.Operation, print.Name));

        var transition = (PdfTransitionAction)actions[16];
        Assert.Equal(new CosName("Dissolve"), transition.Transition![new CosName("S")]);
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void Form_actions_read_their_url_fields_flags_and_character_set()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        IReadOnlyList<PdfAction> actions = ActionTypeTests.LinkActions(document);

        var submit = (PdfSubmitFormAction)actions[11];
        Assert.True(submit.File!.IsUrl);
        Assert.Equal("https://example.com/submit", submit.File.FileName);
        Assert.Equal(PdfSubmitFormFlags.Exclude | PdfSubmitFormFlags.ExportFormat | PdfSubmitFormFlags.GetMethod, submit.Flags);
        Assert.Equal("utf-8", submit.CharacterSet);
        Assert.Equal([new CosReference(69, 0), null], submit.Fields!.Select(field => field.Reference));
        Assert.Equal([null, "name.first"], submit.Fields!.Select(field => field.FieldName));

        var reset = (PdfResetFormAction)actions[12];
        Assert.Equal(PdfResetFormFlags.Exclude, reset.Flags);
        Assert.Equal("email", Assert.Single(reset.Fields!).FieldName);

        var import = (PdfImportDataAction)actions[13];
        Assert.Equal("data.fdf", import.File!.FileName);
        Assert.False(import.File.IsUrl);
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void A_set_ocg_state_action_lists_its_changes_and_applies_them_to_a_state_snapshot()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        var action = (PdfSetOcgStateAction)ActionTypeTests.LinkActions(document)[14];
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentGroup one = properties.Groups[0];
        PdfOptionalContentGroup two = properties.Groups[1];
        PdfOptionalContentState before = properties.GetDefaultStates();

        PdfOptionalContentState after = action.ApplyTo(before);

        Assert.False(action.PreserveRadioButtons);
        Assert.Equal(
            ["Off 70", "Toggle 71", "Toggle 70"],
            action.Changes.Select(change => $"{change.Operation} {change.GroupReference?.ObjectNumber}"));
        Assert.Same(document.Resolve(new CosReference(70, 0)), action.Changes[0].Group);
        Assert.True(before.IsOn(one) && before.IsOn(two));
        Assert.True(after.IsOn(one));
        Assert.False(after.IsOn(two));
        Assert.Empty(Warnings(document));
    }

    [Fact]
    public void A_javascript_action_reads_its_script_from_a_stream_and_is_never_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("actions-all.pdf"));
        var script = (PdfJavaScriptAction)ActionTypeTests.LinkActions(document)[18];

        Assert.Equal("app.alert(\"stream\");", script.Script);
        Assert.Same(document.Resolve(new CosReference(76, 0)), script.ScriptObject);
        Assert.Equal(new CosName("JavaScript"), script.ActionType);
    }

    internal static IEnumerable<Diagnostic> Warnings(PdfDocument document) =>
        document.Diagnostics.Where(diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
}
