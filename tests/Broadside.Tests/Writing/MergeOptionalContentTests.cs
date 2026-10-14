using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// The optional content configuration of copied pages through merge and split (issue #307): the groups the pages use stay listed in
/// <c>OCProperties</c> with their default states, presentation order, radio-button collections, locks, auto-states and alternate
/// configurations. ISO 32000-2 §8.11.4 (Tables 98, 99 and 101) and §7.7.2, Table 29.
/// </summary>
public sealed class MergeOptionalContentTests
{
    private const string OptionalContent =
        "groups: A on, B off, C on, D2 on | order: A, (Labelled)[B, C], [D2] | rb: [A B] | locked: C | as: none"
        + " | configs: Only C: Off on [C] off [] locked []";

    private const string OptionalContentPages =
        "groups: Base on, Notes off, Draft on, Unused off | order: Base[Notes, Draft], (Extras)[Unused] | rb: [Notes Draft] | locked: Draft"
        + " | as: View [Base Notes] Zoom | configs: Review: Off on [Notes Draft] off [] locked []";

    [Fact]
    public void Merging_a_layered_document_into_one_without_layers_keeps_its_groups_default_states_and_order()
    {
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using PdfDocument layered = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        Assert.Equal(OptionalContent, Describe(layered.OptionalContent));

        using PdfDocument merged = PdfDocument.Merge(plain, layered);

        using PdfDocument reopened = Reopen(merged);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        Assert.Equal(OptionalContent, Describe(properties));
        Assert.Equal("Default", properties.DefaultConfiguration.Name);
        Assert.Equal("Broadside corpus", properties.DefaultConfiguration.Creator);

        // The copied page's content marked with B (hidden by default) stays hidden; A's stays visible.
        PdfOptionalContentState states = properties.GetDefaultStates();
        Assert.True(properties.IsVisible(Property(reopened.Pages[1], "a"), states));
        Assert.False(properties.IsVisible(Property(reopened.Pages[1], "b"), states));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Merging_two_layered_documents_keeps_each_groups_default_state_and_lists_the_second_after_the_first()
    {
        using PdfDocument pages = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));
        using PdfDocument layered = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        Assert.Equal(OptionalContentPages, Describe(pages.OptionalContent));

        using PdfDocument merged = PdfDocument.Merge(pages, layered);

        using PdfDocument reopened = Reopen(merged);
        Assert.Equal(
            "groups: Base on, Notes off, Draft on, Unused off, A on, B off, C on, D2 on"
            + " | order: Base[Notes, Draft], (Extras)[Unused], A, (Labelled)[B, C], [D2] | rb: [Notes Draft], [A B] | locked: Draft, C"
            + " | as: View [Base Notes] Zoom"
            + " | configs: Review: Off on [Notes Draft] off [] locked [], Only C: Off on [C] off [] locked []",
            Describe(reopened.OptionalContent));
        Assert.Equal("Layers", reopened.OptionalContent!.DefaultConfiguration.Name);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_layered_document_merged_with_itself_lists_both_copies_of_each_group_with_their_states()
    {
        using PdfDocument layered = PdfDocument.Open(Corpus.Path("optional-content.pdf"));

        using PdfDocument merged = PdfDocument.Merge(layered, layered);

        using PdfDocument reopened = Reopen(merged);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        Assert.Equal(
            "groups: A on, B off, C on, D2 on, A on, B off, C on, D2 on"
            + " | order: A, (Labelled)[B, C], [D2], A, (Labelled)[B, C], [D2] | rb: [A B], [A B] | locked: C, C | as: none"
            + " | configs: Only C: Off on [C] off [] locked [], Only C: Off on [C] off [] locked []",
            Describe(properties));
        Assert.Equal(8, properties.Groups.Select(group => group.Reference).Distinct().Count());

        // Each page's sections name its own copies.
        PdfOptionalContentState states = properties.GetDefaultStates();
        Assert.Same(properties.Groups[1], properties.FindGroup(Property(reopened.Pages[0], "b")));
        Assert.Same(properties.Groups[5], properties.FindGroup(Property(reopened.Pages[1], "b")));
        Assert.False(properties.IsVisible(Property(reopened.Pages[1], "b"), states));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_split_keeps_only_the_groups_its_pages_use_in_the_group_list_the_default_configuration_and_the_order()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));

        IReadOnlyList<PdfDocument> parts = source.Split(0..1, 1..2, 2..3);

        try
        {
            string[] described = [.. parts.Select(part =>
            {
                using PdfDocument reopened = Reopen(part);
                Assert.Empty(reopened.Diagnostics);
                return Describe(reopened.OptionalContent);
            })];
            Assert.Equal(
                [
                    "groups: Base on | order: Base | rb: none | locked: none | as: View [Base] Zoom | configs: Review: Off on [] off [] locked []",
                    "groups: Notes off | order: Notes | rb: [Notes] | locked: none | as: View [Notes] Zoom | configs: Review: Off on [Notes] off [] locked []",
                    "groups: Draft on | order: Draft | rb: [Draft] | locked: Draft | as: none | configs: Review: Off on [Draft] off [] locked []",
                ],
                described);
        }
        finally
        {
            foreach (PdfDocument part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void Extracted_pages_keep_their_layers_hidden_by_default_and_sublayers_whose_parent_is_left_out_move_up()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));

        using PdfDocument extracted = source.ExtractPages(1..3);

        using PdfDocument reopened = Reopen(extracted);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        Assert.Equal(
            "groups: Notes off, Draft on | order: Notes, Draft | rb: [Notes Draft] | locked: Draft | as: View [Notes] Zoom"
            + " | configs: Review: Off on [Notes Draft] off [] locked []",
            Describe(properties));
        Assert.False(properties.IsVisible(Property(reopened.Pages[0], "notes"), properties.GetDefaultStates()));
        Assert.Equal(2, properties.Groups[0].Usage!.ZoomMin);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_layered_page_appended_to_a_layered_document_in_an_incremental_update_joins_its_configuration()
    {
        byte[] original = Corpus.Bytes("optional-content-pages.pdf");
        using PdfDocument target = PdfDocument.Open(original);
        using PdfDocument source = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));

        target.AppendPages(source, 1..2);

        using var stream = new MemoryStream();
        target.Save(stream, new PdfSaveOptions().WithIncrementalUpdate(true));
        byte[] file = stream.ToArray();
        Assert.Equal(original, file.AsSpan(0, original.Length).ToArray());
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        Assert.Equal(
            "groups: Base on, Notes off, Draft on, Unused off, Notes off | order: Base[Notes, Draft], (Extras)[Unused], Notes"
            + " | rb: [Notes Draft], [Notes] | locked: Draft | as: View [Base Notes] Zoom, View [Notes] Zoom"
            + " | configs: Review: Off on [Notes Draft] off [] locked [], Review: Off on [Notes] off [] locked []",
            Describe(properties));
        Assert.False(properties.IsVisible(Property(reopened.Pages[3], "notes"), properties.GetDefaultStates()));
        Assert.Same(properties.Groups[4], properties.FindGroup(Property(reopened.Pages[3], "notes")));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Pages_without_layers_appended_to_a_layered_document_leave_its_configuration_as_it_was()
    {
        using PdfDocument target = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        target.AppendPages(plain);

        using PdfDocument reopened = Reopen(target);
        Assert.Equal(4, reopened.Pages.Count);
        Assert.Equal(OptionalContentPages, Describe(reopened.OptionalContent));
    }

    [Fact]
    public void Pages_that_use_no_layer_split_from_a_layered_document_get_no_optional_content_properties()
    {
        using PdfDocument layered = PdfDocument.Open(Corpus.Path("optional-content-pages.pdf"));
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        layered.AppendPages(plain);

        using PdfDocument extracted = layered.ExtractPages(3..4);

        using PdfDocument reopened = Reopen(extracted);
        Assert.Null(reopened.OptionalContent);
    }

    private static CosObject Property(PdfPage page, string name)
    {
        PdfDocument document = page.Document;
        var resources = (CosDictionary)document.Resolve(page.Dictionary[new CosName("Resources")]);
        var properties = (CosDictionary)document.Resolve(resources[new CosName("Properties")]);
        return properties[new CosName(name)];
    }

    private static PdfDocument Reopen(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions());
        byte[] file = stream.ToArray();
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
        return PdfDocument.Open(file);
    }

    private static string Describe(PdfOptionalContentProperties? properties)
    {
        if (properties is null)
        {
            return "none";
        }

        PdfOptionalContentState states = properties.GetDefaultStates();
        PdfOptionalContentConfiguration defaults = properties.DefaultConfiguration;
        return string.Join(
            " | ",
            "groups: " + string.Join(", ", properties.Groups.Select(group => $"{group.Name} {(states.IsOn(group) ? "on" : "off")}")),
            "order: " + Order(defaults.Order),
            "rb: " + List(defaults.RadioButtonGroups.Select(groups => $"[{Names(groups, " ")}]")),
            "locked: " + List(defaults.Locked.Select(group => group.Name)),
            "as: " + List(defaults.AutoStates.Select(state => $"{state.Event} [{Names(state.Groups, " ")}] {string.Join(" ", state.Categories.Select(name => name.Value))}")),
            "configs: " + List(properties.Configurations.Where(configuration => !configuration.IsDefault).Select(configuration =>
                $"{configuration.Name}: {configuration.BaseState} on [{Names(configuration.On, " ")}] off [{Names(configuration.Off, " ")}] locked [{Names(configuration.Locked, " ")}]")));

        static string Names(IEnumerable<PdfOptionalContentGroup> groups, string separator) => string.Join(separator, groups.Select(group => group.Name));

        static string List(IEnumerable<string> items) => items.Any() ? string.Join(", ", items) : "none";

        static string Order(IReadOnlyList<PdfOptionalContentOrderItem> items) => string.Join(", ", items.Select(item => item switch
        {
            { Group: { } group } => group.Name + (item.Children.Count > 0 ? $"[{Order(item.Children)}]" : string.Empty),
            { Label: { } label } => $"({label})[{Order(item.Children)}]",
            _ => $"[{Order(item.Children)}]",
        }));
    }
}
