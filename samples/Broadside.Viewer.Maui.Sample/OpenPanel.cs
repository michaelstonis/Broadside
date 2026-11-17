namespace Broadside.Viewer.Maui.Sample;

/// <summary>Opens a document: a PDF from this device (the platform's file picker), an address (range requests), or the gallery.</summary>
public sealed class OpenPanel : ContentView
{
    private readonly SampleDocument _document;
    private readonly Entry _address = new() { Placeholder = "https://…/file.pdf", Keyboard = Keyboard.Url, AutomationId = "open-url", ReturnType = ReturnType.Go };

    public OpenPanel(SampleDocument document)
    {
        _document = document;
        SemanticProperties.SetDescription(_address, "Document address");
        _address.Completed += async (_, _) => await OpenAddressAsync();
        var gallery = new CollectionView
        {
            ItemsSource = SampleDocument.Gallery,
            SelectionMode = SelectionMode.Single,
            AutomationId = "gallery",
            ItemTemplate = new DataTemplate(() =>
            {
                var title = new Label { FontSize = 14 };
                title.SetBinding(Label.TextProperty, static (GalleryEntry e) => e.Title);
                var file = new Label { FontSize = 11, TextColor = Colors.Gray };
                file.SetBinding(Label.TextProperty, static (GalleryEntry e) => e.File);
                return new VerticalStackLayout { Padding = new Thickness(4, 8), Children = { title, file } };
            }),
        };
        gallery.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is GalleryEntry entry)
            {
                gallery.SelectedItem = null;
                await _document.OpenGalleryAsync(entry);
            }
        };

        var pick = new Button { Text = "Open a PDF from this device", AutomationId = "open-file" };
        pick.Clicked += async (_, _) => await PickAsync();
        var go = new Button { Text = "Open", AutomationId = "open-url-go" };
        go.Clicked += async (_, _) => await OpenAddressAsync();
        var addressRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 6 };
        addressRow.Add(_address);
        addressRow.Add(go, 1, 0);

        var header = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                pick,
                new Label { Text = "From an address", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 6, 0, 0) },
                addressRow,
                new Label { Text = "Read through HTTP range requests: page 1 of a linearized file shows before the rest downloads.", FontSize = 12, TextColor = Colors.Gray },
                new Label { Text = "Gallery", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 6, 0, 0) },
            },
        };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(header);
        root.Add(gallery, 0, 1);
        Content = root;
    }

    private async Task PickAsync()
    {
        FileResult? file;
        try
        {
            file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Open a PDF", FileTypes = FilePickerFileType.Pdf });
        }
        catch (Exception exception)
        {
            _document.Report("The file could not be picked: " + exception.Message);
            return;
        }

        if (file is null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            await _document.OpenBytesAsync(file.FileName, buffer.ToArray());
        }
        catch (IOException exception)
        {
            _document.Report("The file could not be read: " + exception.Message);
        }
    }

    private async Task OpenAddressAsync()
    {
        string address = (_address.Text ?? string.Empty).Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _document.Report("Enter an http or https address of a PDF.");
            return;
        }

        _address.Unfocus();
        await _document.OpenUrlAsync(uri.AbsoluteUri);
    }
}
