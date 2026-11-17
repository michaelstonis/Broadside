using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// Find in document for a <see cref="PdfView"/>: a query with match-case, whole-word and diacritics options, the match count, previous
/// and next, and the matches with their context. It drives the view's <see cref="PdfView.Search"/>, which indexes pages in the
/// background and highlights every match over the pages without drawing them again; choosing a match scrolls it into view.
/// </summary>
/// <remarks>Spec #33 viewer capability 7; text from ISO 32000-2 §9.10 (text extraction).</remarks>
public sealed class PdfSearchPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfSearchPanel), null, propertyChanged: (b, _, n) => ((PdfSearchPanel)b)._follower.Attach((PdfView?)n));

    private const int MaxResults = 200;
    private readonly ViewerFollower _follower;
    private readonly Entry _query = new() { Placeholder = "Find in document", AutomationId = "search-query", ReturnType = ReturnType.Search, ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
    private readonly CheckBox _matchCase = new() { AutomationId = "search-match-case" };
    private readonly CheckBox _wholeWord = new() { AutomationId = "search-whole-word" };
    private readonly CheckBox _diacritics = new() { AutomationId = "search-diacritics" };
    private readonly Label _status = PanelKit.Hint(string.Empty, "search-status");
    private readonly CollectionView _results = new() { SelectionMode = SelectionMode.Single, AutomationId = "search-results" };
    private DocumentSearch? _search;
    private int _updatePosted;
    private int _shownCount = -1;
    private bool _revealFirst;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view it searches.</summary>
    public PdfSearchPanel()
    {
        _follower = new ViewerFollower(OnDocumentChanged);
        SemanticProperties.SetDescription(_query, "Find in document");
        _query.Completed += (_, _) => Run();
        _results.ItemTemplate = new DataTemplate(() =>
        {
            var page = new Label { FontSize = 11, TextColor = PanelKit.HintColor };
            page.SetBinding(Label.TextProperty, static (Row row) => row.Page);
            var context = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            context.SetBinding(Label.TextProperty, static (Row row) => row.Context);
            return new VerticalStackLayout { Padding = new Thickness(4, 6), Children = { page, context } };
        });
        _results.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is Row row && _search is { } search && row.Index < search.MatchCount)
            {
                Reveal(search.MoveTo(row.Index));
            }
        };

        var header = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 6,
                    Children = { _query, Column(PanelKit.Button("Find", "search-find", Run), 1) },
                },
                PanelKit.Wrap(Option(_matchCase, "Match case"), Option(_wholeWord, "Whole word"), Option(_diacritics, "Diacritics")),
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 6,
                    Children =
                    {
                        _status,
                        Column(PanelKit.Button("‹", "search-previous", () => Reveal(_search?.Previous()), "Previous match"), 1),
                        Column(PanelKit.Button("›", "search-next", () => Reveal(_search?.Next()), "Next match"), 2),
                    },
                },
            },
        };
        _status.VerticalTextAlignment = TextAlignment.Center;
        _status.Text = "Matches are highlighted over the pages while the rest of the document is indexed in the background.";
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 6 };
        root.Add(header);
        root.Add(_results, 0, 1);
        Content = root;
    }

    /// <summary>Gets or sets the view this panel searches.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets or sets the query (searched when the user presses Find or the keyboard's search key).</summary>
    public string Query
    {
        get => _query.Text ?? string.Empty;
        set => _query.Text = value;
    }

    /// <summary>Clears the query, the matches and their highlights (for example when the panel closes).</summary>
    public void Clear()
    {
        _search?.Clear();
        Update();
    }

    private static View Column(View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    private static HorizontalStackLayout Option(CheckBox box, string text)
    {
        SemanticProperties.SetDescription(box, text);
        var label = new Label { Text = text, VerticalTextAlignment = TextAlignment.Center };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => box.IsChecked = !box.IsChecked;
        label.GestureRecognizers.Add(tap);
        return new HorizontalStackLayout { Children = { box, label } };
    }

    private void OnDocumentChanged()
    {
        DocumentSearch? search = _follower.View?.Search;
        if (!ReferenceEquals(search, _search))
        {
            _search?.Changed -= OnSearchChanged;
            _search = search;
            _search?.Changed += OnSearchChanged;
        }

        _shownCount = -1;
        Update();
    }

    private void Run()
    {
        if (_search is not { } search)
        {
            return;
        }

        _revealFirst = true;
        _shownCount = -1;
        search.Search(
            Query,
            new TextSearchOptions().WithMatchCase(_matchCase.IsChecked).WithWholeWord(_wholeWord.IsChecked).WithMatchDiacritics(_diacritics.IsChecked),
            _follower.CurrentPage);
        _query.Unfocus();
        Update();
    }

    // Raised on the indexing thread: one update at a time is posted to the UI thread.
    private void OnSearchChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _updatePosted, 1) == 0)
        {
            if (Dispatcher is { } dispatcher)
            {
                dispatcher.Dispatch(() =>
                {
                    Volatile.Write(ref _updatePosted, 0);
                    Update();
                });
            }
            else
            {
                Volatile.Write(ref _updatePosted, 0);
            }
        }
    }

    private void Update()
    {
        if (_search is not { Query.Length: > 0 } search)
        {
            _status.Text = _search is null ? "Open a document to search it." : "Matches are highlighted over the pages while the rest of the document is indexed in the background.";
            if (_shownCount != 0)
            {
                _shownCount = 0;
                _results.ItemsSource = null;
            }

            return;
        }

        IReadOnlyList<SearchMatch> matches = search.Matches;
        if (_revealFirst && search.Current is { } first)
        {
            _revealFirst = false;
            Reveal(first);
        }

        _status.Text = matches.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1} of {matches.Count}{(search.IsSearchComplete ? string.Empty : "+")}")
            : search.IsSearchComplete
                ? "No matches."
                : string.Create(CultureInfo.InvariantCulture, $"Searching… {search.SearchedPageCount} of {search.Session.PageCount} pages");
        int shown = Math.Min(matches.Count, MaxResults);
        if (shown != _shownCount)
        {
            _shownCount = shown;
            var rows = new List<Row>(shown);
            for (int i = 0; i < shown; i++)
            {
                SearchMatch match = matches[i];
                rows.Add(new Row(i, string.Create(CultureInfo.InvariantCulture, $"Page {match.PageIndex + 1}"), match.Context));
            }

            _results.ItemsSource = rows;
        }
    }

    private void Reveal(SearchMatch? match)
    {
        if (match is not null)
        {
            _follower.View?.Reveal(match.PageIndex, match.Bounds);
            Update();
        }
    }

    /// <summary>One listed match.</summary>
    internal sealed record Row(int Index, string Page, string Context);
}
