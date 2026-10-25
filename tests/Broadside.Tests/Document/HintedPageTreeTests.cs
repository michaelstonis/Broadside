using System.Globalization;
using Broadside.Annotations;
using Broadside.Layout;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.Document;

/// <summary>
/// The paths that used to walk the page tree of a remote linearized file read with synchronous fetching off, as the browser reads
/// it (issue #417): enumerating the pages, a link's or an outline item's destination page, a structure element's <c>Pg</c>. While
/// the hint tables locate the pages (ISO 32000-2 Annex G, G.4), each answers from them and the page tree is never read; what lies
/// after the pages (the outline, the structure tree, the name trees, Annex F, F.3.10) is reached through
/// <see cref="PdfDocument.ReadAsync{T}"/>, which fetches what a read misses.
/// </summary>
public sealed class HintedPageTreeTests
{
    private static readonly Uri FileUri = new("http://broadside.test/tagged-linearized.pdf");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task Enumerating_the_pages_goes_through_the_hinted_pages_and_never_reads_the_page_tree(PdfCrossReferenceLayout layout)
    {
        byte[] file = TaggedLinearizedFile.Create(layout);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await OpenAsync(client);

        Assert.Equal(TaggedLinearizedFile.PageCount, document.Pages.Count);

        // A synchronous enumeration needs every page's bytes; it says how to get them.
        PdfRangeNotLoadedException miss = Assert.Throws<PdfRangeNotLoadedException>(() => document.Pages.ToList());
        Assert.Contains(nameof(PdfDocument.ReadAsync), miss.Message, StringComparison.Ordinal);

        int[] numbers = await document.ReadAsync(static d => d.Pages.Select(static page => page.Reference!.ObjectNumber).ToArray(), Token);

        using PdfDocument local = PdfDocument.Open(file);
        Assert.Equal(local.Pages.Select(static page => page.Reference!.ObjectNumber), numbers);
        string[] texts = await document.ReadAsync(static d => d.Pages.Select(static page => page.ExtractText().Text.Trim()).ToArray(), Token);
        Assert.Equal(Enumerable.Range(1, TaggedLinearizedFile.PageCount).Select(static n => string.Create(CultureInfo.InvariantCulture, $"Page {n}")), texts);
        AssertPageTreeUnread(document);
        Assert.False(server.AnyServed(RoundUp(MainCrossReference(local)), file.Length), $"The main cross-reference section was fetched: {server}");
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task A_link_finds_its_page_from_the_hints_and_a_named_destination_resolves_through_the_async_read(PdfCrossReferenceLayout layout)
    {
        byte[] file = TaggedLinearizedFile.Create(layout);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await OpenAsync(client);
        PdfPage first = await document.Pages.PrefetchAsync(0, Token);
        await first.PrefetchAsync(Token);
        PdfLinkAnnotation[] links = [.. first.Annotations.OfType<PdfLinkAnnotation>()];
        int fetched = server.Requests.Count;

        var explicitDestination = Assert.IsType<PdfExplicitDestination>(links[0].Destination);
        Assert.Equal(TaggedLinearizedFile.LinkedPage, explicitDestination.PageIndex);
        Assert.Equal(PdfDestinationTarget.Page, explicitDestination.TargetKind);
        Assert.Null(explicitDestination.StructureElement);
        Assert.Equal(fetched, server.Requests.Count);
        AssertPageTreeUnread(document);

        PdfPage linked = await document.Pages.PrefetchAsync(TaggedLinearizedFile.LinkedPage, Token);
        Assert.Same(linked, explicitDestination.Page);
        Assert.Equal(TaggedLinearizedFile.LinkedPage, document.Pages.IndexOf(linked));

        // The name tree lies after the pages: its synchronous read misses, the asynchronous one fetches it.
        var named = Assert.IsType<PdfNamedDestination>(links[1].Destination);
        Assert.Throws<PdfRangeNotLoadedException>(() => named.Resolve());
        Assert.Equal(TaggedLinearizedFile.NamedPage, await document.ReadAsync(_ => named.Resolve()!.PageIndex, Token));
        AssertPageTreeUnread(document);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task The_outline_items_find_their_pages_from_the_hints(PdfCrossReferenceLayout layout)
    {
        using var server = new RangeServer(TaggedLinearizedFile.Create(layout));
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await OpenAsync(client);

        Assert.Throws<PdfRangeNotLoadedException>(() => document.Outline);
        int?[] pages = await document.ReadAsync(
            static d => d.Outline!.Items.Select(static item => Assert.IsType<PdfExplicitDestination>(item.Destination).PageIndex).ToArray(),
            Token);

        Assert.Equal(TaggedLinearizedFile.OutlinePages.Select(static page => (int?)page), pages);
        AssertPageTreeUnread(document);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task A_structure_element_finds_its_page_from_the_hints_and_a_tagged_page_extracts_its_layout(PdfCrossReferenceLayout layout)
    {
        using var server = new RangeServer(TaggedLinearizedFile.Create(layout));
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await OpenAsync(client);

        // Page 20 has not been handed out by the hints: its element's Pg is found among the loaded page objects, not in the tree.
        int[] pages = await document.ReadAsync(
            static d => d.StructureTree!.Children.Select(element => d.Pages.IndexOf(element.Page!)).ToArray(),
            Token);
        Assert.Equal(TaggedLinearizedFile.StructurePages, pages);
        AssertPageTreeUnread(document);

        foreach (int index in TaggedLinearizedFile.StructurePages)
        {
            PdfPage page = await document.Pages.PrefetchAsync(index, Token);
            await page.PrefetchAsync(Token);
            LayoutPage laidOut = Assert.Single((await document.ReadAsync(_ => page.ExtractLayout(), Token)).Pages);
            Assert.Equal(index, laidOut.PageIndex);
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Page {index + 1}"), laidOut.Text.Text, StringComparison.Ordinal);
        }

        AssertPageTreeUnread(document);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task A_local_document_runs_an_async_read_once()
    {
        using PdfDocument document = PdfDocument.Open(TaggedLinearizedFile.Create());
        int runs = 0;

        Assert.Equal(TaggedLinearizedFile.PageCount, await document.ReadAsync(d => ++runs > 0 ? d.Pages.Count : 0, Token));
        Assert.Equal(1, runs);
        await Assert.ThrowsAsync<ArgumentNullException>(() => document.ReadAsync<int>(null!, Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.ReadAsync(static d => d.Pages.Count, new CancellationToken(canceled: true)));
    }

    private static Task<PdfDocument> OpenAsync(HttpClient client) =>
        PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(false), Token);

    /// <summary>The hints are still in use and the page tree's root was never loaded.</summary>
    private static void AssertPageTreeUnread(PdfDocument document)
    {
        Assert.True(document.Pages.UsesHints, "The pages are no longer found through the hints.");
        var root = Assert.IsType<CosReference>(document.Catalog[KnownNames.Pages]);
        Assert.False(document.TryGetLoaded(root, out _), "The page tree was read.");
    }

    private static long MainCrossReference(PdfDocument local) => ((CosInteger)local.Linearization!.Dictionary[new CosName("T")]).Value;

    private static long RoundUp(long offset) => (offset + PdfOptions.DefaultRangeBlockSize - 1) / PdfOptions.DefaultRangeBlockSize * PdfOptions.DefaultRangeBlockSize;
}
