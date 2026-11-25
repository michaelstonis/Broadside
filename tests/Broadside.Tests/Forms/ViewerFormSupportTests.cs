using Broadside.Content;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>
/// What form filling in a viewer needs from the core: the fields a submit-form or reset-form action selects, descendants included
/// (ISO 32000-2 §12.7.6.2-12.7.6.3), and transient appearances for widgets that lack one (§12.7.3, §12.7.4.3).
/// </summary>
[Collection(Document.HeavyTestCollection.Name)]
public sealed class ViewerFormSupportTests
{
    private static readonly CosName AP = new("AP");

    [Fact]
    public void A_reset_form_action_selects_the_descendants_of_a_named_non_terminal_field()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        var submit = (PdfPushButtonField)form.FindField("submit")!;
        var reset = (PdfResetFormAction)submit.Widgets[0].Action!;

        Assert.Equal(["person.first", "person.last", "agree"], Names(reset.SelectFields()));

        // Include/Exclude set: every other terminal field.
        IReadOnlyList<PdfTerminalField> excluded = form.SelectFields(reset.Fields, exclude: true);
        Assert.Equal(form.TerminalFields.Count - 3, excluded.Count);
        Assert.DoesNotContain(excluded, field => field.FullyQualifiedName.StartsWith("person.", StringComparison.Ordinal));

        // No Fields entry: every terminal field.
        Assert.Equal(Names(form.TerminalFields), Names(form.SelectFields(null, exclude: true)));

        // A name that is no field selects nothing; a terminal field's name selects that field once.
        Assert.Equal(["name"], Names(form.SelectFields([new PdfActionTarget(null, null, "name"), new PdfActionTarget(null, null, "name"), new PdfActionTarget(null, null, "nothing")], exclude: false)));
    }

    [Fact]
    public void The_form_field_layer_draws_a_transient_appearance_for_a_widget_without_one_and_leaves_the_document_unchanged()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        var name = (PdfTextField)document.AcroForm!.FindField("name")!;
        name.Widgets[0].Dictionary.Remove(AP);
        PdfPage page = document.Pages[0];
        using var before = new MemoryStream();
        document.Save(before);

        DisplayList plain = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions());
        DisplayList transient = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions().WithTransientFieldAppearances(true));
        Assert.True(transient.OperationCount > plain.OperationCount);
        Assert.NotSame(plain, transient); // cached apart
        Assert.Null(name.Widgets[0].AppearanceDictionary);

        using var after = new MemoryStream();
        document.Save(after);
        Assert.Equal(before.ToArray(), after.ToArray());

        // The appearance drawn is the one filling generates from the value.
        name.WriteAppearances();
        DisplayList generated = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions());
        Assert.Equal(generated.OperationCount, transient.OperationCount);
    }

    [Fact]
    public void NeedAppearances_draws_text_and_choice_fields_from_their_values()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        form.Dictionary[new CosName("NeedAppearances")] = CosBoolean.True;

        // A value changed in the file without new appearances (as an older filler leaves it): only a transient appearance shows it.
        var name = (PdfTextField)form.FindField("name")!;
        name.Dictionary[new CosName("V")] = new CosString("A much longer name than before"u8.ToArray());
        PdfPage page = document.Pages[0];
        DisplayList stale = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions());
        DisplayList transient = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions().WithTransientFieldAppearances(true));
        Assert.NotEqual(stale.OperationCount, transient.OperationCount);
    }

    private static string[] Names(IEnumerable<PdfField> fields) => [.. fields.Select(field => field.FullyQualifiedName)];
}

/// <summary>Transient appearances keep display lists cacheable (run alone: the change epoch is process-wide).</summary>
[Collection(Document.HeavyTestCollection.Name)]
public sealed class TransientAppearanceCachingTests
{
    [Fact]
    public void Building_a_transient_appearance_does_not_make_the_display_list_stale()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        ((PdfTextField)document.AcroForm!.FindField("name")!).Widgets[0].Dictionary.Remove(new CosName("AP"));
        PdfPage page = document.Pages[0];
        DisplayList first = page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions().WithTransientFieldAppearances(true));
        Assert.Same(first, page.CreateDisplayList(DisplayLayers.FormFields, DisplayIntent.View, new ContentOptions().WithTransientFieldAppearances(true)));
    }
}
