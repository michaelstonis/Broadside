using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The catalog entries a viewer reads before showing a page: Version, Extensions, Requirements, Lang, PageLayout, PageMode and
/// ViewerPreferences. ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11.
/// </summary>
public sealed class CatalogEntryTests
{
    [Fact]
    public void Viewer_preferences_pdf_types_every_table_147_entry()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("viewer-preferences.pdf"));
        PdfViewerPreferences preferences = document.ViewerPreferences!;

        Assert.True(preferences.HideToolbar);
        Assert.True(preferences.HideMenubar);
        Assert.True(preferences.HideWindowUI);
        Assert.True(preferences.FitWindow);
        Assert.True(preferences.CenterWindow);
        Assert.True(preferences.DisplayDocTitle);
        Assert.Equal(PdfPageMode.UseOutlines, preferences.NonFullScreenPageMode);
        Assert.Equal(PdfReadingDirection.RightToLeft, preferences.Direction);
        Assert.Equal(PdfPageBoundary.MediaBox, preferences.ViewArea);
        Assert.Equal(PdfPageBoundary.BleedBox, preferences.ViewClip);
        Assert.Equal(PdfPageBoundary.TrimBox, preferences.PrintArea);
        Assert.Equal(PdfPageBoundary.ArtBox, preferences.PrintClip);
        Assert.Equal(PdfPrintScaling.None, preferences.PrintScaling);
        Assert.Equal(PdfDuplex.DuplexFlipLongEdge, preferences.Duplex);
        Assert.True(preferences.PickTrayByPdfSize);
        Assert.Equal([new PdfPageRange(1, 1), new PdfPageRange(1, 1)], preferences.PrintPageRange);
        Assert.Equal(2, preferences.NumCopies);
        Assert.Equal([new CosName("PrintScaling")], preferences.Enforce);
        Assert.Equal(PdfPageLayout.TwoPageRight, document.PageLayout);
        Assert.Equal(PdfPageMode.UseOC, document.PageMode);
        Assert.Empty(document.Diagnostics);
        Assert.False(document.Catalog.IsDirty);
    }

    [Fact]
    public void Empty_page_pdf_has_the_default_layout_and_mode_and_no_viewer_preferences()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Equal(PdfPageLayout.SinglePage, document.PageLayout);
        Assert.Equal(PdfPageMode.UseNone, document.PageMode);
        Assert.Null(document.ViewerPreferences);
        Assert.Null(document.Language);
        Assert.Empty(document.Extensions);
        Assert.Empty(document.Requirements);
        Assert.Equal(new PdfVersion(1, 7), document.HeaderVersion);
        Assert.Null(document.CatalogVersion);
        Assert.Equal(new PdfVersion(1, 7), document.Version);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Outline_pdf_opens_with_the_outline_visible()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"));

        Assert.Equal(PdfPageMode.UseOutlines, document.PageMode);
    }

    [Fact]
    public void An_empty_viewer_preferences_dictionary_reads_every_default()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/ViewerPreferences << >>"));
        PdfViewerPreferences preferences = document.ViewerPreferences!;

        Assert.False(preferences.HideToolbar || preferences.HideMenubar || preferences.HideWindowUI || preferences.FitWindow
            || preferences.CenterWindow || preferences.DisplayDocTitle);
        Assert.Equal(PdfPageMode.UseNone, preferences.NonFullScreenPageMode);
        Assert.Equal(PdfReadingDirection.LeftToRight, preferences.Direction);
        Assert.Equal(PdfPageBoundary.CropBox, preferences.ViewArea);
        Assert.Equal(PdfPageBoundary.CropBox, preferences.ViewClip);
        Assert.Equal(PdfPageBoundary.CropBox, preferences.PrintArea);
        Assert.Equal(PdfPageBoundary.CropBox, preferences.PrintClip);
        Assert.Equal(PdfPrintScaling.AppDefault, preferences.PrintScaling);
        Assert.Null(preferences.Duplex);
        Assert.Null(preferences.PickTrayByPdfSize);
        Assert.Null(preferences.PrintPageRange);
        Assert.Null(preferences.NumCopies);
        Assert.Empty(preferences.Enforce);
        Assert.Empty(preferences.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    public static TheoryData<string> MalformedViewerPreferences => new()
    {
        "/HideToolbar /true",
        "/NonFullScreenPageMode /FullScreen",
        "/Direction /TopToBottom",
        "/ViewArea /PageBox",
        "/PrintScaling /Fit",
        "/Duplex /Triplex",
        "/NumCopies 0",
        "/PrintPageRange [1 2 3]",
        "/PrintPageRange [3 2]",
        "/Enforce [/HideToolbar]",
        "/Enforce [/PrintScaling]",
    };

    [Theory]
    [MemberData(nameof(MalformedViewerPreferences))]
    public void A_malformed_viewer_preference_reads_as_its_default_with_a_warning(string entry)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", $"/ViewerPreferences << {entry} >>"));
        PdfViewerPreferences preferences = document.ViewerPreferences!;

        Assert.False(preferences.HideToolbar);
        Assert.Equal(PdfPageMode.UseNone, preferences.NonFullScreenPageMode);
        Assert.Equal(PdfReadingDirection.LeftToRight, preferences.Direction);
        Assert.Equal(PdfPageBoundary.CropBox, preferences.ViewArea);
        Assert.Equal(PdfPrintScaling.AppDefault, preferences.PrintScaling);
        Assert.Null(preferences.Duplex);
        Assert.Null(preferences.NumCopies);
        Assert.True(preferences.PrintPageRange is null or { Count: 0 } or [{ First: 1, Last: 2 }]);
        _ = preferences.Enforce;
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("ViewerPreferenceInvalid", diagnostic.Code);
        Assert.Equal(new CosReference(1, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void Unknown_viewer_preference_keys_are_ignored()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/ViewerPreferences << /AcmeZoom 3 >>"));

        Assert.False(document.ViewerPreferences!.FitWindow);
        Assert.Empty(document.Diagnostics);
    }

    public static TheoryData<string, string> UnknownCatalogEntries => new()
    {
        { "/PageLayout /Spread", "PageLayoutInvalid" },
        { "/PageMode (UseOutlines)", "PageModeInvalid" },
        { "/ViewerPreferences [1]", "ViewerPreferencesInvalid" },
    };

    [Theory]
    [MemberData(nameof(UnknownCatalogEntries))]
    public void An_unknown_layout_mode_or_preferences_entry_reads_as_the_default_with_a_warning(string entry, string code)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", entry));

        Assert.Equal(PdfPageLayout.SinglePage, document.PageLayout);
        Assert.Equal(PdfPageMode.UseNone, document.PageMode);
        Assert.Null(document.ViewerPreferences);
        Assert.Equal(code, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void The_language_is_a_text_string()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Lang (de-CH)"));

        Assert.Equal("de-CH", document.Language);
    }

    [Fact]
    public void Catalog_version_extensions_pdf_exposes_its_version_extensions_and_requirements()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("catalog-version-extensions.pdf"));

        Assert.Equal(new PdfVersion(1, 7), document.HeaderVersion);
        Assert.Equal(new PdfVersion(2, 0), document.CatalogVersion);
        Assert.Equal(new PdfVersion(2, 0), document.Version);

        Assert.Equal(2, document.Extensions.Count);
        PdfDeveloperExtension adobe = document.Extensions[0];
        Assert.Equal(new CosName("ADBE"), adobe.Prefix);
        Assert.Equal(new PdfVersion(1, 7), adobe.BaseVersion);
        Assert.Equal(3, adobe.ExtensionLevel);
        Assert.Null(adobe.Url);
        Assert.Null(adobe.ExtensionRevision);
        PdfDeveloperExtension iso = document.Extensions[1];
        Assert.Equal(new CosName("ISO_"), iso.Prefix);
        Assert.Equal(new PdfVersion(2, 0), iso.BaseVersion);
        Assert.Equal(32001, iso.ExtensionLevel);
        Assert.Equal(":2022", iso.ExtensionRevision);
        Assert.Equal(new Uri("https://www.iso.org/standard/45874.html"), iso.Url);

        PdfRequirement requirement = Assert.Single(document.Requirements);
        Assert.Equal(new CosName("EnableJavaScripts"), requirement.RequirementType);
        Assert.Equal(50, requirement.Penalty);
        Assert.Null(requirement.Version);
        PdfRequirementHandler handler = Assert.Single(requirement.Handlers);
        Assert.Equal(new CosName("NoOp"), handler.HandlerType);
        Assert.Null(handler.Script);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Pdf20_header_pdf_is_version_2_0_from_the_header_and_the_catalog()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdf20-header.pdf"));

        Assert.Equal(new PdfVersion(2, 0), document.HeaderVersion);
        Assert.Equal(new PdfVersion(2, 0), document.CatalogVersion);
        Assert.NotNull(document.FileIdentifier);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_requirement_takes_the_default_penalty_and_handlers_given_as_an_array()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "/MediaBox [0 0 612 792]",
            "/Requirements [<< /S /Navigation /RH [<< /S /JS /Script (check) >> << /S /NoOp >>] >>]"));
        PdfRequirement requirement = Assert.Single(document.Requirements);

        Assert.Equal(100, requirement.Penalty);
        Assert.Equal(["check", null], requirement.Handlers.Select(h => h.Script));
        Assert.Equal(new CosName("JS"), requirement.Handlers[0].HandlerType);
        Assert.Empty(document.Diagnostics);
    }

    public static TheoryData<string> MalformedRequirements => new()
    {
        "/Requirements << /S /Navigation >>",
        "/Requirements [(x) << /S /Navigation >>]",
        "/Requirements [<< /S /Navigation /Penalty 101 >>]",
        "/Requirements [<< /Penalty 1 >>]",
    };

    [Theory]
    [MemberData(nameof(MalformedRequirements))]
    public void A_malformed_requirement_is_reported(string entry)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", entry));

        Assert.All(document.Requirements, requirement => Assert.InRange(requirement.Penalty, 0, 100));
        Assert.Equal("RequirementInvalid", Assert.Single(document.Diagnostics).Code);
    }

    public static TheoryData<string, string, int> MalformedExtensions => new()
    {
        { "/Extensions [1]", "ExtensionsInvalid", 0 },
        { "/Extensions << /ADBE 5 >>", "DeveloperExtensionInvalid", 0 },
        { "/Extensions << /ADBE << /ExtensionLevel 3 >> >>", "DeveloperExtensionInvalid", 1 },
        { "/Extensions << /ADBE << /BaseVersion /1.7 /ExtensionLevel 3.5 >> >>", "DeveloperExtensionInvalid", 1 },
    };

    [Theory]
    [MemberData(nameof(MalformedExtensions))]
    public void A_malformed_extensions_entry_is_reported(string entry, string code, int count)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", entry));

        Assert.Equal(count, document.Extensions.Count);
        Assert.Equal(code, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Extensions_given_through_indirect_references_are_read_with_a_warning()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Extensions 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /ADBE << /BaseVersion /1.7 /ExtensionLevel 8 >> >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(8, Assert.Single(document.Extensions).ExtensionLevel);
        Assert.Equal("ExtensionsNotDirect", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Reading_every_catalog_entry_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("catalog-version-extensions.pdf"));

        _ = (document.Extensions, document.Requirements, document.PageLayout, document.PageMode, document.ViewerPreferences, document.Language);
        _ = (document.Information, document.Metadata, document.Properties.Title, document.FileIdentifier);

        Assert.False(document.Catalog.IsDirty);
        Assert.False(document.Trailer.IsDirty);
    }
}
