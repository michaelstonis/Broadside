using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.OptionalContent;

/// <summary>Optional content groups, membership dictionaries, configurations and visibility. ISO 32000-2 §8.11.</summary>
public class OptionalContentTests
{
    private static PdfDocument OpenCorpus() => PdfDocument.Open(Corpus.Path("optional-content.pdf"));

    private static CosObject Property(PdfDocument document, string name) =>
        ((CosDictionary)document.Resolve(document.Pages[0].Resources![new CosName("Properties")]))[new CosName(name)];

    [Fact]
    public void Groups_are_read_from_OCGs_in_order_with_names_intents_and_usage()
    {
        using PdfDocument document = OpenCorpus();

        PdfOptionalContentProperties properties = document.OptionalContent!;

        Assert.Equal(["A", "B", "C", "D2"], properties.Groups.Select(group => group.Name));
        Assert.Equal(new CosReference(5, 0), properties.Groups[0].Reference);
        Assert.Equal(["View"], properties.Groups[0].Intents.Select(intent => intent.Value));
        Assert.Equal(["Design"], properties.Groups[2].Intents.Select(intent => intent.Value));
        Assert.Equal("Broadside", properties.Groups[2].Usage!.Creator);
        Assert.Equal("Technical", properties.Groups[2].Usage!.CreatorSubtype!.Value);
        Assert.False(properties.Groups[3].Usage!.ViewState);
        Assert.Equal(1.5, properties.Groups[3].Usage!.ZoomMin);
        Assert.Null(properties.Groups[3].Usage!.ZoomMax);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_default_configuration_is_typed()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentConfiguration configuration = properties.DefaultConfiguration;

        Assert.Equal("Default", configuration.Name);
        Assert.Equal("Broadside corpus", configuration.Creator);
        Assert.Equal(PdfOptionalContentBaseState.On, configuration.BaseState);
        Assert.Empty(configuration.On);
        Assert.Equal(["B"], configuration.Off.Select(group => group.Name));
        Assert.Equal(["View"], configuration.Intents.Select(intent => intent.Value));
        Assert.Equal(PdfOptionalContentListMode.AllPages, configuration.ListMode);
        Assert.Equal(["A", "B"], Assert.Single(configuration.RadioButtonGroups).Select(group => group.Name));
        Assert.Equal(["C"], configuration.Locked.Select(group => group.Name));
        Assert.Empty(configuration.AutoStates);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_order_tree_keeps_labels_and_unlabelled_nesting_as_written()
    {
        using PdfDocument document = OpenCorpus();

        IReadOnlyList<PdfOptionalContentOrderItem> order = document.OptionalContent!.DefaultConfiguration.Order;

        Assert.Equal(3, order.Count);
        Assert.Equal("A", order[0].Group!.Name);
        Assert.Empty(order[0].Children);
        Assert.Null(order[1].Group);
        Assert.Equal("Labelled", order[1].Label);
        Assert.Equal(["B", "C"], order[1].Children.Select(child => child.Group!.Name));
        Assert.Null(order[2].Group);
        Assert.Null(order[2].Label);
        Assert.Equal("D2", Assert.Single(order[2].Children).Group!.Name);
    }

    [Fact]
    public void A_layer_followed_by_an_unlabelled_array_has_sublayers()
    {
        byte[] file = OptionalContentPdf.Build(
            "/OCGs [4 0 R 5 0 R 6 0 R] /D << /Order [4 0 R [5 0 R 6 0 R]] >>",
            "<< /Type /OCG /Name (Layer 1) >>", "<< /Type /OCG /Name (Sublayer A) >>", "<< /Type /OCG /Name (Sublayer B) >>");
        using PdfDocument document = PdfDocument.Open(file);

        PdfOptionalContentOrderItem layer = Assert.Single(document.OptionalContent!.DefaultConfiguration.Order);

        Assert.Equal("Layer 1", layer.Group!.Name);
        Assert.Equal(["Sublayer A", "Sublayer B"], layer.Children.Select(child => child.Group!.Name));
    }

    [Fact]
    public void Alternate_configurations_are_typed_and_inherit_order_and_radio_buttons_from_D()
    {
        using PdfDocument document = OpenCorpus();

        PdfOptionalContentConfiguration alternate = Assert.Single(document.OptionalContent!.Configurations);

        Assert.Equal("Only C", alternate.Name);
        Assert.Equal(PdfOptionalContentBaseState.Off, alternate.BaseState);
        Assert.Equal(["C"], alternate.On.Select(group => group.Name));
        Assert.Equal(["All"], alternate.Intents.Select(intent => intent.Value));
        Assert.Equal(PdfOptionalContentListMode.VisiblePages, alternate.ListMode);
        Assert.Equal(3, alternate.Order.Count);
        Assert.Single(alternate.RadioButtonGroups);
        Assert.Empty(alternate.Locked);
    }

    [Fact]
    public void Default_states_follow_BaseState_then_ON_then_OFF_and_ignore_usage_without_AS()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentState state = properties.GetDefaultStates();

        Assert.Equal([true, false, true, true], properties.Groups.Select(state.IsOn));
    }

    [Fact]
    public void Visibility_of_each_section_XObject_and_annotation_follows_the_default_state_and_intent()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState state = properties.GetDefaultStates();
        CosDictionary form = (CosDictionary)((CosStream)document.Resolve(new CosReference(11, 0))).Dictionary;
        var annotation = (CosDictionary)document.Resolve(new CosReference(12, 0));

        Assert.True(properties.IsVisible(Property(document, "a"), state));
        Assert.False(properties.IsVisible(Property(document, "m1"), state));
        Assert.True(properties.IsVisible(Property(document, "m2"), state));
        Assert.False(properties.IsVisible(form[new CosName("OC")], state));
        Assert.True(properties.IsVisible(annotation[new CosName("OC")], state));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_tracker_hides_content_nested_inside_a_hidden_section()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;
        var tracker = new PdfOptionalContentTracker(properties, properties.GetDefaultStates());
        var oc = new CosName("OC");

        tracker.BeginMarkedContent(oc, Property(document, "a"));
        Assert.True(tracker.IsVisible);
        tracker.EndMarkedContent();

        tracker.BeginMarkedContent(oc, Property(document, "b"));
        Assert.False(tracker.IsVisible);
        tracker.BeginMarkedContent(new CosName("Span"), null);
        tracker.BeginMarkedContent(oc, Property(document, "a"));
        Assert.False(tracker.IsVisible);
        tracker.EndMarkedContent();
        tracker.EndMarkedContent();
        tracker.EndMarkedContent();
        Assert.True(tracker.IsVisible);
        Assert.Equal(0, tracker.Depth);

        Assert.False(tracker.IsObjectVisible(((CosStream)document.Resolve(new CosReference(11, 0))).Dictionary));
        Assert.True(tracker.IsObjectVisible((CosDictionary)document.Resolve(new CosReference(12, 0))));
    }

    [Fact]
    public void An_unbalanced_EMC_is_ignored_by_the_tracker()
    {
        var tracker = new PdfOptionalContentTracker(null, null);

        tracker.EndMarkedContent();

        Assert.True(tracker.IsVisible);
        Assert.Equal(0, tracker.Depth);
    }

    [Fact]
    public void An_alternate_configuration_with_intent_All_makes_the_design_group_count()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentState state = properties.GetStates(properties.Configurations[0]);

        Assert.Equal([false, false, true, false], properties.Groups.Select(state.IsOn));
        Assert.True(properties.IsVisible(new CosReference(7, 0), state));
        Assert.False(properties.IsVisible(new CosReference(5, 0), state));
        Assert.True(properties.IsVisible(new CosReference(10, 0), state));
        Assert.False(properties.IsVisible(new CosReference(9, 0), properties.GetDefaultStates()));
    }

    [Fact]
    public void An_Unchanged_base_state_keeps_the_current_states()
    {
        byte[] file = OptionalContentPdf.Build(
            "/OCGs [4 0 R 5 0 R] /D << /OFF [5 0 R] >> /Configs [<< /BaseState /Unchanged /ON [5 0 R] >>]",
            "<< /Type /OCG /Name (A) >>", "<< /Type /OCG /Name (B) >>");
        using PdfDocument document = PdfDocument.Open(file);
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState current = properties.GetDefaultStates().With(properties.Groups[0], on: false);

        PdfOptionalContentState state = properties.GetStates(properties.Configurations[0], current);

        Assert.Equal([false, true], properties.Groups.Select(state.IsOn));
    }

    [Fact]
    public void A_state_is_an_immutable_snapshot()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState original = properties.GetDefaultStates();

        PdfOptionalContentState changed = original.With(properties.Groups[1], on: true);

        Assert.False(original.IsOn(properties.Groups[1]));
        Assert.True(changed.IsOn(properties.Groups[1]));
        Assert.True(properties.IsVisible(Property(document, "b"), changed));
        Assert.False(properties.IsVisible(Property(document, "b"), original));
    }

    [Fact]
    public void A_set_OCG_state_array_turns_groups_on_off_and_toggles_honouring_radio_buttons()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState state = properties.GetDefaultStates();
        var array = new CosArray([new CosName("ON"), new CosReference(6, 0), new CosName("Toggle"), new CosReference(8, 0)]);

        PdfOptionalContentState applied = state.Apply(array);
        PdfOptionalContentState withoutRadio = state.Apply(array, preserveRadioButtons: false);

        Assert.Equal([false, true, true, false], properties.Groups.Select(applied.IsOn));
        Assert.Equal([true, true, true, false], properties.Groups.Select(withoutRadio.IsOn));
    }

    [Fact]
    public void An_empty_configuration_intent_makes_all_content_visible()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentState state = properties.GetDefaultStates().WithIntents([]);

        Assert.True(properties.IsVisible(Property(document, "b"), state));
        Assert.True(properties.IsVisible(Property(document, "m1"), state));
    }

    [Fact]
    public void Usage_states_can_be_applied_for_engine_parity_without_an_AS_entry()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentState state = properties.ApplyUsageStates(properties.GetDefaultStates(), PdfOptionalContentEvent.View);

        Assert.Equal([true, false, true, false], properties.Groups.Select(state.IsOn));
    }

    [Fact]
    public void A_document_without_OCProperties_has_no_optional_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Null(document.OptionalContent);
    }

    [Fact]
    public void The_properties_view_is_shared_while_OCProperties_is_the_same_dictionary()
    {
        using PdfDocument document = OpenCorpus();

        Assert.Same(document.OptionalContent, document.OptionalContent);
    }

    [Fact]
    public void Reading_optional_content_leaves_every_object_clean()
    {
        using PdfDocument document = OpenCorpus();
        PdfOptionalContentProperties properties = document.OptionalContent!;

        PdfOptionalContentState state = properties.GetStates(properties.Configurations[0], properties.GetDefaultStates());
        _ = properties.DefaultConfiguration.Order;
        foreach (PdfOptionalContentGroup group in properties.Groups)
        {
            _ = (group.Name, group.Intents, group.Usage?.ViewState);
        }

        for (int number = 1; number <= 12; number++)
        {
            properties.IsVisible(new CosReference(number, 0), state);
            Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty);
        }

        Assert.False(document.Catalog.IsDirty);
    }
}
