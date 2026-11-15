using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The signature status of a <see cref="PdfView"/>'s document: each signed field's integrity (the signed byte ranges against the CMS
/// digest), whether it covers the whole file, the signer and the certificate chain, with the problems validation found. No trust
/// anchors are configured, so a chain that ends at a root the platform does not know is indeterminate rather than valid.
/// </summary>
/// <remarks>ISO 32000-2 §12.8 (digital signatures), ISO/TS 32002.</remarks>
public sealed class PdfSignaturesPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfSignaturesPanel), null, propertyChanged: (b, _, n) => ((PdfSignaturesPanel)b)._follower.Attach((PdfView?)n));

    private readonly ViewerFollower _follower;
    private readonly Label _status = PanelKit.Hint(string.Empty, "signature-status");
    private readonly VerticalStackLayout _list = new() { Spacing = 12, AutomationId = "signature-list" };
    private int _generation;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view whose document it validates.</summary>
    public PdfSignaturesPanel()
    {
        _follower = new ViewerFollower(() => _ = RefreshAsync());
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _status,
                    _list,
                    PanelKit.Hint("Integrity (the signed byte ranges against the CMS digest) and the certificate chain. No trust anchors are configured, so a chain that ends at a root the platform does not know is indeterminate rather than valid."),
                },
            },
        };
    }

    /// <summary>Gets or sets the view whose document's signatures this panel shows.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Validates the signatures again (an edit after signing shows as a signature that no longer covers the whole file once saved).</summary>
    /// <returns>A task that completes when the panel shows the results.</returns>
    public async Task RefreshAsync()
    {
        int generation = ++_generation;
        _list.Children.Clear();
        if (_follower.View?.CurrentSession?.Document is not { } document)
        {
            _status.Text = "No document.";
            return;
        }

        _status.Text = "Validating…";
        IReadOnlyList<SignatureRow> rows;
        try
        {
            rows = await SignatureStatus.ValidateAsync(document, CancellationToken.None).ConfigureAwait(true);
        }
        catch (PdfRangeNotLoadedException)
        {
            if (generation == _generation)
            {
                _status.Text = "Validating the signatures needs the whole file.";
            }

            return;
        }
#pragma warning disable CA1031 // shown in the panel
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (generation == _generation)
            {
                _status.Text = "The signatures could not be validated: " + exception.Message;
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _status.Text = rows.Count == 0
            ? "This document has no signed signature fields."
            : string.Create(CultureInfo.InvariantCulture, $"{rows.Count} signed field(s)");
        foreach (SignatureRow row in rows)
        {
            var badge = new Label
            {
                Text = row.Status,
                AutomationId = "signature-badge",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                Padding = new Thickness(8, 2),
                BackgroundColor = row.Status switch
                {
                    "Valid" => Color.FromRgb(0x1E, 0x8E, 0x3E),
                    "Invalid" => Color.FromRgb(0xC5, 0x22, 0x1F),
                    _ => Color.FromRgb(0xB0, 0x6D, 0x00),
                },
                HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.End,
            };
            var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            header.Add(new Label { Text = row.FieldName, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation });
            header.Add(badge, 1, 0);
            var card = new VerticalStackLayout { Spacing = 4, Children = { header, PanelKit.Facts(row.Facts) } };
            foreach (string problem in row.Problems)
            {
                card.Children.Add(PanelKit.Hint(problem));
            }

            _list.Children.Add(card);
        }
    }
}
