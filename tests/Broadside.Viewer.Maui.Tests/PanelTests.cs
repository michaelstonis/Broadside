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
            new PdfFormPanel { Viewer = view },
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
                PdfFormPanel => PdfFormPanel.ViewerProperty,
                _ => PdfViewSettingsPanel.ViewerProperty,
            }, null);
        }
    }

    // Android throws "the specified child already has a parent" when a second layout adds a native view the first still holds, so the
    // info panel's list views must keep one parent however often a document change shows their tab again (#358).
    [Fact]
    public void The_info_panel_keeps_one_parent_for_its_lists_when_another_document_shows_the_same_tab()
    {
        var panel = new PdfDocumentInfoPanel();
        foreach ((int tab, string list) in new[] { (1, "info-diagnostics"), (2, "info-tags") })
        {
            using PdfDocument first = PdfDocument.Open(Shell.CorpusFile("tagged-structure.pdf"));
            panel.SelectTab(tab);
            panel.Refresh(first);
            Microsoft.Maui.Controls.CollectionView view = Find<Microsoft.Maui.Controls.CollectionView>(panel, list);
            Microsoft.Maui.Controls.Element? parent = view.Parent;
            Assert.NotNull(parent);

            using PdfDocument second = PdfDocument.Open(Shell.CorpusFile("tagged-structure.pdf"));
            panel.Refresh(second);
            panel.Refresh(null);
            panel.Refresh(second);
            Assert.Same(view, Find<Microsoft.Maui.Controls.CollectionView>(panel, list));
            Assert.Same(parent, view.Parent);
            panel.Refresh(null); // before the documents close
        }
    }

    private static T Find<T>(Microsoft.Maui.Controls.Element root, string automationId)
        where T : Microsoft.Maui.Controls.Element =>
        ((Microsoft.Maui.IVisualTreeElement)root).GetVisualTreeDescendants().OfType<T>().First(e => e.AutomationId == automationId);
}
