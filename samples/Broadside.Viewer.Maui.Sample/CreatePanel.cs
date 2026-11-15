using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Viewer.Blazor.Sample;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// Creates a document with the flow layout package and saves it with the chosen PDF/A and PDF/UA conformance (the Blazor demo's
/// <see cref="DocumentCreator"/>, shared by link), then opens it. The save checks the rules it knows and identifies the file only when they pass.
/// </summary>
public sealed class CreatePanel : ContentView
{
    private static readonly (string Title, PdfAConformance Value)[] Archival =
        [("None", PdfAConformance.None), ("PDF/A-2b", PdfAConformance.PdfA2b), ("PDF/A-2u", PdfAConformance.PdfA2u), ("PDF/A-3u", PdfAConformance.PdfA3u), ("PDF/A-4", PdfAConformance.PdfA4)];

    private static readonly (string Title, PdfUAConformance Value)[] Accessible =
        [("None", PdfUAConformance.None), ("PDF/UA-1", PdfUAConformance.PdfUA1), ("PDF/UA-2", PdfUAConformance.PdfUA2)];

    private readonly SampleDocument _document;
    private readonly Entry _title = new() { Text = "Quarterly report", AutomationId = "create-title" };
    private readonly Entry _author = new() { Text = "Broadside sample", AutomationId = "create-author" };
    private readonly Editor _body = new() { HeightRequest = 120, AutomationId = "create-body" };
    private readonly CheckBox _table = new() { IsChecked = true, AutomationId = "create-table" };
    private readonly CheckBox _figure = new() { IsChecked = true, AutomationId = "create-figure" };
    private readonly Picker _pdfa = new() { Title = "PDF/A", ItemsSource = Archival.Select(a => a.Title).ToList(), SelectedIndex = 2, AutomationId = "create-pdfa" };
    private readonly Picker _pdfua = new() { Title = "PDF/UA", ItemsSource = Accessible.Select(a => a.Title).ToList(), SelectedIndex = 1, AutomationId = "create-pdfua" };
    private readonly Button _create = new() { Text = "Create and open", AutomationId = "create-run" };
    private readonly Label _result = new() { AutomationId = "create-result", FontSize = 13 };
    private readonly VerticalStackLayout _diagnostics = new() { Spacing = 4 };

    public CreatePanel(SampleDocument document)
    {
        _document = document;
        _body.Text = new CreateRequest().Body.Replace("in your browser", "on this device", StringComparison.Ordinal)
            .Replace("so it runs in WebAssembly as it runs on a server", "so it runs on phones as it runs on a server", StringComparison.Ordinal);
        _create.Clicked += async (_, _) => await CreateAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Title" }, _title,
                    new Label { Text = "Author" }, _author,
                    new Label { Text = "Text" }, _body,
                    Check(_table, "Table with a header row"),
                    Check(_figure, "Figure with alternate text"),
                    new Label { Text = "PDF/A" }, _pdfa,
                    new Label { Text = "PDF/UA" }, _pdfua,
                    _create,
                    _result,
                    _diagnostics,
                    new Label
                    {
                        Text = "Built on this device with the flow layout package and saved with the conformance you choose: the save checks the rules it knows (veraPDF rule numbers in the messages) and identifies the file only when they pass.",
                        FontSize = 12,
                        TextColor = Colors.Gray,
                    },
                },
            },
        };
    }

    /// <summary>Gets the last created document, or null.</summary>
    public CreateResult? Result { get; private set; }

    /// <summary>Gets whether the last created document passed the conformance it claims.</summary>
    public bool IsConformant { get; private set; }

    /// <summary>Sets the conformance levels (the UI tests use it) by their titles, such as "PDF/A-2u" and "PDF/UA-1".</summary>
    public void SelectConformance(string archival, string accessible)
    {
        _pdfa.SelectedIndex = Array.FindIndex(Archival, a => a.Title == archival);
        _pdfua.SelectedIndex = Array.FindIndex(Accessible, a => a.Title == accessible);
    }

    private static HorizontalStackLayout Check(CheckBox box, string text)
    {
        SemanticProperties.SetDescription(box, text);
        return new HorizontalStackLayout { Children = { box, new Label { Text = text, VerticalTextAlignment = TextAlignment.Center } } };
    }

    private async Task CreateAsync()
    {
        _create.IsEnabled = false;
        _create.Text = "Creating…";
        _diagnostics.Children.Clear();
        var request = new CreateRequest
        {
            Title = _title.Text ?? string.Empty,
            Author = _author.Text ?? string.Empty,
            Body = _body.Text ?? string.Empty,
            IncludeTable = _table.IsChecked,
            IncludeFigure = _figure.IsChecked,
            Archival = Archival[Math.Max(_pdfa.SelectedIndex, 0)].Value,
            Accessible = Accessible[Math.Max(_pdfua.SelectedIndex, 0)].Value,
        };
        try
        {
            // The layout runs off the UI thread: the button shows "Creating…" and the view keeps scrolling meanwhile.
            CreateResult result = await Task.Run(() => DocumentCreator.Create(request));
            Result = result;
            IsConformant = !result.Diagnostics.Any(d => d.Code.EndsWith("NonConformant", StringComparison.Ordinal) || d.Code.EndsWith("NotIdentified", StringComparison.Ordinal));
            string claim = string.Join(" + ", new[]
            {
                request.Archival == PdfAConformance.None ? null : Archival.First(a => a.Value == request.Archival).Title,
                request.Accessible == PdfUAConformance.None ? null : Accessible.First(a => a.Value == request.Accessible).Title,
            }.Where(s => s is not null).DefaultIfEmpty("plain PDF"));
            _result.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Created {result.Pages} page(s), {result.Bytes.Length / 1024.0:0.0} KB. {(IsConformant ? "Identified as " + claim : "Not identified: see the problems below")}.");
            foreach (Diagnostic diagnostic in result.Diagnostics.Take(50))
            {
                _diagnostics.Children.Add(new Label { Text = diagnostic.Code + ": " + diagnostic.Message, FontSize = 12 });
            }

            await _document.OpenBytesAsync("created.pdf", result.Bytes);
        }
        catch (Exception exception)
        {
            _result.Text = "The document could not be created: " + exception.Message;
        }
        finally
        {
            _create.IsEnabled = true;
            _create.Text = "Create and open";
        }
    }
}
