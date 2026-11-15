namespace Broadside.Viewer.Maui.Tests;

/// <summary>The reusable panels attach to a view and follow it without a platform (#358); the device runs are the sample's UI tests.</summary>
public sealed class PanelTests
{
    [Fact]
    public void Every_panel_attaches_to_a_view_without_a_document_and_detaches()
    {
        using var view = new PdfView();
        Microsoft.Maui.Controls.ContentView[] panels =
        [
            new PdfSearchPanel { Viewer = view },
            new PdfOutlinePanel { Viewer = view },
            new PdfThumbnailsPanel { Viewer = view },
            new PdfAnnotationsPanel { Viewer = view },
            new PdfDocumentInfoPanel { Viewer = view, FileName = "none.pdf" },
            new PdfSignaturesPanel { Viewer = view },
            new PdfViewSettingsPanel { Viewer = view },
        ];

        Assert.All(panels, panel => Assert.NotNull(panel.Content));
        var settings = (PdfViewSettingsPanel)panels[^1];
        Assert.Same(view, settings.Viewer);
        foreach (Microsoft.Maui.Controls.ContentView panel in panels)
        {
            panel.SetValue(panel switch
            {
                PdfSearchPanel => PdfSearchPanel.ViewerProperty,
                PdfOutlinePanel => PdfOutlinePanel.ViewerProperty,
                PdfThumbnailsPanel => PdfThumbnailsPanel.ViewerProperty,
                PdfAnnotationsPanel => PdfAnnotationsPanel.ViewerProperty,
                PdfDocumentInfoPanel => PdfDocumentInfoPanel.ViewerProperty,
                PdfSignaturesPanel => PdfSignaturesPanel.ViewerProperty,
                _ => PdfViewSettingsPanel.ViewerProperty,
            }, null);
        }
    }
}
