namespace Broadside.Viewer.Tests;

public sealed class SearchResultsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Matches_are_grouped_by_page_with_whole_words_of_context()
    {
        await using DocumentSession session = DocumentSession.Open(
            PanelSamples.Text(["the quick brown fox jumps over the lazy dog"], ["no match here"], ["a fox", "another fox"]),
            Options());
        using var search = new DocumentSearch(session);
        using var results = new SearchResults(search, contextCharacters: 12);
        int changes = 0;
        results.Changed += (_, _) => changes++;

        await SearchAsync(search, "fox");

        Assert.True(changes > 0);
        Assert.Equal(3, results.Count);
        Assert.Equal([0, 2], results.Pages.Select(p => p.PageIndex));
        SearchResultItem first = Assert.Single(results.Pages[0].Items);
        Assert.Equal(("…quick brown ", "fox", " jumps over…"), (first.Before, first.Text, first.After));
        Assert.Equal([1, 2], results.Pages[1].Items.Select(i => i.MatchIndex));
    }

    [Fact]
    public async Task Showing_a_result_makes_it_current_and_reveals_it()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Text(["fox"], ["fox"]), Options());
        using var search = new DocumentSearch(session);
        using var results = new SearchResults(search);
        await SearchAsync(search, "fox");
        var viewer = new RecordingViewer(session, search);

        SearchResultItem second = results.Pages[1].Items[0];
        results.Show(second, viewer);

        Assert.Equal(1, search.CurrentIndex);
        Assert.Equal((1, second.Match.Bounds), viewer.Revealed);
    }

    [Fact]
    public async Task Clearing_the_search_empties_the_results_and_disposing_stops_following()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Text(["fox"]), Options());
        using var search = new DocumentSearch(session);
        var results = new SearchResults(search);
        await SearchAsync(search, "fox");
        Assert.Single(results.Pages);

        search.Clear();
        Assert.Empty(results.Pages);

        int changes = 0;
        results.Changed += (_, _) => changes++;
        results.Dispose();
        await SearchAsync(search, "fox");
        Assert.Equal(0, changes);
    }

    private static ViewerOptions Options() => new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts());

    private static async Task SearchAsync(DocumentSearch search, string query)
    {
        search.Search(query);
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (search.IsIndexing || !search.IsSearchComplete)
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, Token);
        }
    }
}
