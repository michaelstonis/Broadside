using System.Globalization;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Annotation and form-field layers in the display list (ISO 32000-2 §12.5.3, §12.5.5, §12.7; ADR 0006): each annotation's normal
/// appearance for its state, placed on its rectangle, between <see cref="IRenderingBackend.BeginAnnotation"/> and
/// <see cref="IRenderingBackend.EndAnnotation"/>; widgets in a layer of their own; flags, intent and optional content decide what
/// is drawn.
/// </summary>
public sealed class DisplayListAnnotationTests
{
    private static readonly CosName OC = new("OC");

    [Fact]
    public void The_annotation_layer_places_each_appearance_on_its_rectangle_in_annots_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));

        // The README's matrices AA (§12.5.5): [0 1 -1 0 150 100], [2 0 0 1 10 10]; the third shows its On state (AS), green.
        Assert.Equal(
            [
                "annotation Square [100 100 150 200]",
                "transform [0 1 -1 0 150 100]",
                "clip nonzero M 0 0 L 100 0 L 100 50 L 0 50 Z",
                "fill nonzero M 0 0 L 100 0 L 100 50 L 0 50 Z | DeviceRgb 1 0 0",
                "end annotation",
                "annotation Circle [10 10 50 30]",
                "transform [2 0 0 1 10 10]",
                "clip nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z",
                "fill nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z | DeviceRgb 0 0 1",
                "end annotation",
                "annotation Square [300 300 340 340]",
                "transform [4 0 0 4 300 300]",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceRgb 0 1 0",
                "end annotation",
            ],
            Record(document.Pages[0].CreateDisplayList(DisplayLayers.Annotations)));
    }

    [Fact]
    public void The_appearance_follows_the_appearance_state()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        document.Pages[0].Annotations[2].Dictionary[new CosName("AS")] = new CosName("Off");

        List<string> operations = Record(document.Pages[0].CreateDisplayList(DisplayLayers.Annotations));

        Assert.Equal("fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0.5", operations[^2]);
    }

    [Fact]
    public void The_content_layer_is_the_page_content_and_all_layers_are_content_then_annotations_then_fields()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-nozoom.pdf"));
        PdfPage page = document.Pages[0];
        List<string> content = Record(page.CreateDisplayList(DisplayLayers.Content));
        List<string> annotations = Record(page.CreateDisplayList(DisplayLayers.Annotations));
        List<string> fields = Record(page.CreateDisplayList(DisplayLayers.FormFields));

        Assert.Equal(Record(page.CreateDisplayList()), content);
        Assert.NotEmpty(content);
        Assert.Equal(5, annotations.Count(line => line.StartsWith("annotation ", StringComparison.Ordinal)));
        Assert.Empty(fields);

        // The content's own state is saved around it, so nothing it sets reaches the annotations drawn after it.
        Assert.Equal(["save", .. content, "restore", .. annotations, .. fields], Record(page.CreateDisplayList(DisplayLayers.All)));
        Assert.Empty(Record(page.CreateDisplayList(DisplayLayers.None)));
    }

    [Fact]
    public void Widgets_are_the_form_field_layer_and_every_other_annotation_the_annotation_layer()
    {
        using PdfDocument fields = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        Assert.Empty(Begun(fields.Pages[0].CreateDisplayList(DisplayLayers.Annotations)));
        Assert.All(Begun(fields.Pages[0].CreateDisplayList(DisplayLayers.FormFields)), line => Assert.StartsWith("Widget ", line, StringComparison.Ordinal));
        Assert.Contains("Widget 10", Begun(fields.Pages[0].CreateDisplayList(DisplayLayers.FormFields)));

        using PdfDocument subtypes = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        List<string> annotations = Begun(subtypes.Pages[0].CreateDisplayList(DisplayLayers.Annotations));
        Assert.DoesNotContain(annotations, line => line.StartsWith("Widget ", StringComparison.Ordinal));
        Assert.Equal(["Widget 34"], Begun(subtypes.Pages[0].CreateDisplayList(DisplayLayers.FormFields)));
    }

    [Fact]
    public void Hidden_and_NoView_annotations_are_not_drawn_on_screen_and_only_Print_annotations_are_printed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        PdfPage page = document.Pages[0];
        PdfAnnotation line = page.Annotations.Single(annotation => annotation.Reference?.ObjectNumber == 15);
        line.Dictionary[new CosName("F")] = new CosInteger(32 | 4);

        List<string> view = Begun(page.CreateDisplayList(DisplayLayers.Annotations));
        List<string> print = Begun(page.CreateDisplayList(DisplayLayers.Annotations, DisplayIntent.Print, new ContentOptions()));

        // XBroadsideTest 42 sets every flag, Hidden among them; Line 15 is now NoView and Print; popups are the viewer's (§12.5.6.14).
        Assert.DoesNotContain("Unknown 42", view);
        Assert.DoesNotContain("Line 15", view);
        Assert.DoesNotContain(view, line => line.StartsWith("Popup ", StringComparison.Ordinal));
        Assert.Contains("TrapNet 43", view);
        Assert.Contains("Square 16", view);

        // Printing draws only annotations with Print: Text 10 (F 28), Line 15 (F 36), PrinterMark 35 (F 68) and TrapNet 43 (F 68: Print and ReadOnly).
        Assert.Equal(["Text 10", "Line 15", "PrinterMark 35", "TrapNet 43"], print);
    }

    [Fact]
    public void An_annotation_without_an_appearance_draws_nothing_and_says_so()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-malformed.pdf"));
        int before = document.Diagnostics.Count;

        List<string> drawn = Begun(document.Pages[0].CreateDisplayList(DisplayLayers.Annotations));

        // 10 (listed twice), 11, 13, 16 and the direct square (reported on page 3) have no AP; 14's states have no AS; 12's Rect is
        // invalid, so it has no size and needs none; 15's appearance (no BBox) is drawn on its rectangle.
        Assert.Equal(["Square 15"], drawn);
        Assert.Equal(
            ["AppearanceNotRendered 10", "AppearanceNotRendered 11", "AppearanceNotRendered 13", "AppearanceNotRendered 14", "AppearanceNotRendered 16", "AppearanceNotRendered 3"],
            document.Diagnostics.Skip(before).Where(d => d.Code == "AppearanceNotRendered").Select(Describe).Order(StringComparer.Ordinal));
        Assert.All(document.Diagnostics.Where(d => d.Code == "AppearanceNotRendered"), d => Assert.Equal(DiagnosticSeverity.Information, d.Severity));
    }

    [Fact]
    public void An_annotation_in_hidden_optional_content_is_skipped_until_its_group_is_on()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        CosReference group = document.AddObject(new CosDictionary { [KnownNames.Type] = new CosName("OCG"), [new CosName("Name")] = new CosString("Notes"u8) });
        document.Catalog[new CosName("OCProperties")] = new CosDictionary
        {
            [new CosName("OCGs")] = new CosArray([group]),
            [new CosName("D")] = new CosDictionary { [new CosName("OFF")] = new CosArray([group]) },
        };
        document.Pages[0].Annotations[0].Dictionary[OC] = group;

        Assert.Equal(["Circle 11", "Square 12"], Begun(document.Pages[0].CreateDisplayList(DisplayLayers.Annotations)));

        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState on = properties.GetDefaultStates().With(properties.Groups[0], on: true);
        DisplayList shown = document.Pages[0].CreateDisplayList(DisplayLayers.Annotations, DisplayIntent.View, new ContentOptions().WithOptionalContentState(on));
        Assert.Equal(["Square 10", "Circle 11", "Square 12"], Begun(shown));
    }

    [Fact]
    public void The_annotation_carries_its_opacity_blend_mode_and_group_per_the_appearance_streams_algorithm()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfAnnotation square = document.Pages[0].Annotations[0];
        square.Dictionary[new CosName("CA")] = new CosReal(0.25);
        square.Dictionary[new CosName("ca")] = new CosReal(0.5);
        square.Dictionary[new CosName("BM")] = new CosName("Multiply");
        square.GetAppearance()!.Dictionary[new CosName("Group")] = new CosDictionary
        {
            [new CosName("S")] = new CosName("Transparency"),
            [new CosName("I")] = CosBoolean.True,
        };

        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList(DisplayLayers.Annotations).Replay(recorder);

        // §12.5.5 composites the group with BM and ca; §11.6.4.4 applies the nonstroking alpha to a group's result.
        Assert.Equal("annotation Square [100 100 150 200] opacity 0.5 blend Multiply isolated", recorder.Operations[0]);
        Assert.Equal(square.Reference, recorder.Annotations[0].Reference);
    }

    [Fact]
    public void NoZoom_and_NoRotate_reach_the_backend_on_screen_only_and_text_annotations_always_have_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-nozoom.pdf"));
        PdfPage page = document.Pages[0];

        Assert.Equal(
            [
                "annotation Square [40 220 80 260] nozoom",
                "annotation Square [180 220 220 260] norotate",
                "annotation Square [40 80 80 120] nozoom norotate",
                "annotation Square [180 80 220 120]",
                "annotation Text [120 150 140 170] nozoom norotate",
            ],
            Record(page.CreateDisplayList(DisplayLayers.Annotations)).Where(line => line.StartsWith("annotation ", StringComparison.Ordinal)));
        Assert.All(
            Record(page.CreateDisplayList(DisplayLayers.Annotations, DisplayIntent.Print, new ContentOptions())).Where(line => line.StartsWith("annotation ", StringComparison.Ordinal)),
            line => Assert.DoesNotContain(" no", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Each_layer_starts_from_the_alpha_constants_and_blend_mode_it_saved()
    {
        // The content sets ca 0.5 outside any q, and so does the appearance: the layer's restore resets the backend, so the
        // appearance's own SetCompositing must reach it (§11.6.4.4, §12.5.5).
        const string resources = "/ExtGState << /H << /ca 0.5 >> >>";
        byte[] file = ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << {resources} >> /Contents 4 0 R /Annots [6 0 R] >>",
            ContentSamples.Stream("/H gs 0 0 50 50 re f"),
            ContentSamples.Stream("/H gs 0 0 10 10 re f", $"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << {resources} >>"),
            "<< /Type /Annot /Subtype /Square /Rect [100 100 110 110] /F 4 /AP << /N 5 0 R >> >>",
        ]);
        using PdfDocument document = PdfDocument.Open(file);

        List<string> operations = Record(document.Pages[0].CreateDisplayList(DisplayLayers.All));
        int begin = operations.FindIndex(line => line.StartsWith("annotation ", StringComparison.Ordinal));
        Assert.Equal("compositing 1 0.5 Normal", operations.First(line => line.StartsWith("compositing", StringComparison.Ordinal)));
        Assert.Equal("restore", operations[begin - 1]);
        Assert.Contains("compositing 1 0.5 Normal", operations.Skip(begin));
    }

    private static List<string> Record(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return recorder.Operations;
    }

    /// <summary>The annotations a list draws, as kind and object number.</summary>
    private static List<string> Begun(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return [.. recorder.Annotations.Select(annotation => $"{annotation.Kind} {annotation.Reference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}")];
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
