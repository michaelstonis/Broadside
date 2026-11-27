using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class DocumentLayersTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_order_tree_lists_sublayers_and_labelled_collections_with_their_default_state()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        DocumentLayers layers = await DocumentLayers.ReadAsync(session, Token);

        Assert.False(layers.IsEmpty);
        Assert.Equal(
            ["Base(on) [Notes(off) Draft(on, locked)]", "Extras(label) [Unused(off)]"],
            layers.Roots.Select(Describe));
    }

    [Fact]
    public async Task Toggling_a_layer_changes_the_session_state_and_locked_layers_and_labels_refuse()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        DocumentLayers layers = await DocumentLayers.ReadAsync(session, Token);
        int changes = 0;
        layers.Changed += (_, _) => changes++;
        LayerItem draft = layers.Roots[0].Children[1];

        Assert.False(layers.SetVisible(draft, false)); // Locked
        Assert.False(layers.SetVisible(layers.Roots[1], false)); // a label
        Assert.False(layers.SetVisible(layers.Roots[0], true)); // already on
        Assert.Null(session.OptionalContentState);
        Assert.Equal(0, changes);

        Assert.True(layers.SetVisible(layers.Roots[0], false));
        Assert.Equal(1, changes);
        PdfOptionalContentState state = Assert.IsType<PdfOptionalContentState>(session.OptionalContentState);
        Assert.False(state.IsOn(layers.Roots[0].Group!));
        Assert.False(layers.Roots[0].IsVisible);

        layers.Reset();
        Assert.Null(session.OptionalContentState);
        Assert.Equal(2, changes);
        Assert.True(layers.Roots[0].IsVisible);
    }

    [Fact]
    public async Task Showing_a_radio_button_layer_hides_the_others_of_its_set()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content.pdf"));
        DocumentLayers layers = await DocumentLayers.ReadAsync(session, Token);
        Assert.Equal(["A(on)", "Labelled(label) [B(off) C(on, locked)]", "D2(on)"], layers.Roots.Select(Describe));

        Assert.True(layers.SetVisible(layers.Roots[1].Children[0], true));

        Assert.Equal(["A(off)", "Labelled(label) [B(on) C(on, locked)]", "D2(on)"], layers.Roots.Select(Describe));
    }

    [Fact]
    public async Task A_state_set_on_the_session_elsewhere_reaches_the_tree()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("optional-content-pages.pdf"));
        DocumentLayers layers = await DocumentLayers.ReadAsync(session, Token);
        int changes = 0;
        layers.Changed += (_, _) => changes++;
        PdfOptionalContentProperties properties = session.Document.OptionalContent!;

        session.OptionalContentState = properties.GetDefaultStates().With(layers.Roots[1].Children[0].Group!, true);

        Assert.Equal(1, changes);
        Assert.True(layers.Roots[1].Children[0].IsVisible);
    }

    [Fact]
    public async Task A_document_without_optional_content_has_no_layers()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("embedded-files.pdf"));
        DocumentLayers layers = await DocumentLayers.ReadAsync(session, Token);
        Assert.True(layers.IsEmpty);
        Assert.Empty(layers.Roots);
    }

    private static string Describe(LayerItem item)
    {
        string state = item.Group is null ? "label" : (item.IsVisible ? "on" : "off") + (item.IsLocked ? ", locked" : string.Empty);
        string children = item.Children.Count == 0 ? string.Empty : " [" + string.Join(' ', item.Children.Select(Describe)) + "]";
        return $"{item.Title}({state}){children}";
    }
}
