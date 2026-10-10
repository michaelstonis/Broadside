using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>The document outline (ISO 32000-2 §12.3.3, Tables 150 to 152) and the actions of its items (§12.6.4.2, §12.6.4.8).</summary>
public class OutlineTests
{
    [Fact]
    public void Outline_pdf_yields_two_closed_leaf_items_with_their_destinations_and_default_style()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"));

        PdfOutline outline = Assert.IsType<PdfOutline>(document.Outline);

        Assert.Equal(2, outline.Count);
        IReadOnlyList<PdfOutlineItem> items = outline.Items;
        Assert.Equal(["First", "Second"], items.Select(item => item.Title));
        PdfExplicitDestination first = Assert.IsType<PdfExplicitDestination>(items[0].Destination);
        Assert.Equal(PdfDestinationView.Fit, first.View);
        PdfExplicitDestination second = Assert.IsType<PdfExplicitDestination>(items[1].Destination);
        Assert.Equal((PdfDestinationView.Xyz, (double?)0, (double?)792, (double?)null), (second.View, second.Left, second.Top, second.Zoom));
        Assert.All(items, item =>
        {
            Assert.Equal(0, item.Destination is PdfExplicitDestination destination ? destination.PageIndex : -1);
            Assert.Null(item.Count);
            Assert.False(item.IsOpen);
            Assert.Empty(item.Children);
            Assert.Equal(PdfRgbColor.Black, item.Color);
            Assert.Equal(PdfOutlineItemFlags.None, item.Flags);
            Assert.False(item.IsBold || item.IsItalic);
            Assert.Null(item.Action);
            Assert.Null(item.StructureElement);
            Assert.Equal(0, item.Level);
            Assert.Null(item.Parent);
        });
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_document_without_outlines_has_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Null(document.Outline);
        Assert.Null(document.Names);
    }

    [Fact]
    public void Outline_full_pdf_yields_three_levels_with_open_and_closed_items_styles_and_actions()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutline outline = document.Outline!;

        IReadOnlyList<PdfOutlineItem> parts = outline.Items;

        Assert.Equal(4, outline.Count);
        Assert.Equal(
            [
                "0 Part I open 2",
                "1 Chapter 1 closed -1",
                "2 Section 1.1 closed -",
                "1 Chapter 2 closed -",
                "0 Part II closed -2",
                "1 Chapter 3 – Übersicht closed -",
                "1 Chapter 4 closed -",
            ],
            Flatten(parts).Select(item => $"{item.Level} {item.Title} {(item.IsOpen ? "open" : "closed")} {item.Count?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));

        PdfOutlineItem chapter1 = parts[0].Children[0];
        Assert.Same(parts[0], chapter1.Parent);
        Assert.Same(chapter1, chapter1.Children[0].Parent);

        PdfUriAction uri = Assert.IsType<PdfUriAction>(parts[0].Children[1].Action);
        Assert.Equal("https://example.org/chapter-2", uri.Uri);
        Assert.False(uri.IsMap);
        Assert.Equal(new CosName("URI"), uri.ActionType);

        PdfOutlineItem chapter3 = parts[1].Children[0];
        Assert.Equal(new PdfRgbColor(1, 0, 0), chapter3.Color);
        Assert.Equal(PdfOutlineItemFlags.Italic | PdfOutlineItemFlags.Bold, chapter3.Flags);
        Assert.True(chapter3.IsBold && chapter3.IsItalic);
        PdfGoToAction goTo = Assert.IsType<PdfGoToAction>(chapter3.Action);
        Assert.Null(goTo.StructureDestination);
        PdfNamedDestination named = Assert.IsType<PdfNamedDestination>(goTo.Destination);
        PdfExplicitDestination alpha = Assert.IsType<PdfExplicitDestination>(named.Resolve());
        Assert.Equal((PdfDestinationView.Xyz, (double?)792, (int?)0), (alpha.View, alpha.Top, alpha.PageIndex));
        Assert.Null(chapter3.Destination);

        PdfOutlineItem chapter4 = parts[1].Children[1];
        CosDictionary element = Assert.IsType<CosDictionary>(chapter4.StructureElement);
        Assert.Equal(new CosName("H1"), element[new CosName("S")]);
        Assert.Equal(PdfDestinationView.FitB, Assert.IsType<PdfExplicitDestination>(chapter4.Destination).View);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_damaged_outline_is_read_as_a_finite_tree_with_exactly_the_documented_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-broken.pdf"));
        Assert.Empty(document.Diagnostics);

        IReadOnlyList<PdfOutlineItem> items = document.Outline!.Items;

        Assert.Equal(["0 One", "1 One.One", "0 ", "0 Three", "1 Three.One"], Flatten(items).Select(item => $"{item.Level} {item.Title}"));
        Assert.Empty(items[0].Children[0].Children);
        Assert.Equal(
            [
                "OutlineDestAndAction 5", // both Dest and A
                "OutlineItemInvalid 6", // no Title
                "OutlineCycle 6", // 7's Next leads back to 6
                "OutlineLinkInconsistent 4", // the outline's Last is 6, the chain ends at 7
                "OutlineCycle 5", // 8's First is its ancestor 5
                "OutlineCountInvalid 4", // the outline's Count is negative
                "OutlineCountInvalid 7", // a child, but Count 0
            ],
            document.Diagnostics.Select(Describe));
        Assert.Equal(DiagnosticSeverity.Error, document.Diagnostics.Single(diagnostic => diagnostic.Code == "OutlineCycle" && diagnostic.ObjectReference!.ObjectNumber == 6).Severity);

        // Both the destination and the action are exposed; the action is the one to perform.
        Assert.IsType<PdfExplicitDestination>(items[0].Destination);
        Assert.IsType<PdfUriAction>(items[0].Action);

        // A second walk reports nothing new.
        Assert.Equal(5, Flatten(document.Outline.Items).Count());
        Assert.Equal(7, document.Diagnostics.Count);
    }

    [Fact]
    public void Strict_mode_throws_from_the_walk_not_from_open()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-broken.pdf"), new PdfOptions().UseStrict());
        PdfOutline outline = document.Outline!;

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => outline.Items);

        Assert.Equal("OutlineDestAndAction 5", Describe(error.Diagnostic));
    }

    [Fact]
    public void Walking_the_outline_writes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));

        foreach (PdfOutlineItem item in Flatten(document.Outline!.Items))
        {
            _ = (item.Title, item.Color, item.Flags, item.Count, item.StructureElement, item.Destination, item.Action);
            if (item.Action is PdfGoToAction { Destination: PdfNamedDestination named })
            {
                _ = named.Resolve()?.Page;
            }
        }

        Assert.False(document.Catalog.IsDirty);
        Assert.All(Enumerable.Range(4, 8), number => Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty));
    }

    [Fact]
    public void Malformed_item_entries_read_as_their_defaults_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Type /Outlines /First 5 0 R /Last 8 0 R >>",
            "<< /Title (Clamped) /Parent 4 0 R /Next 6 0 R /C [2 0.5 -1] /F 7 >>",
            "<< /Title (Short colour) /Parent 4 0 R /Prev 5 0 R /Next 7 0 R /C [1 0] /F /Bold >>",
            "<< /Title (Action in Dest) /Parent 4 0 R /Prev 6 0 R /Next 8 0 R /Dest << /S /URI /URI (https://example.org/) >> >>",
            "<< /Title (Missing name) /Parent 4 0 R /Prev 7 0 R /Dest (nowhere) >>");
        using PdfDocument document = PdfDocument.Open(file);

        IReadOnlyList<PdfOutlineItem> items = document.Outline!.Items;

        Assert.Equal(new PdfRgbColor(1, 0.5, 0), items[0].Color);
        Assert.Equal((PdfOutlineItemFlags)7, items[0].Flags); // bits other than 1 and 2 are kept as stored
        Assert.Equal(PdfRgbColor.Black, items[1].Color);
        Assert.Equal(PdfOutlineItemFlags.None, items[1].Flags);
        Assert.Null(items[2].Destination);
        Assert.Equal("https://example.org/", Assert.IsType<PdfUriAction>(items[2].Action).Uri);
        PdfNamedDestination missing = Assert.IsType<PdfNamedDestination>(items[3].Destination);
        Assert.Null(missing.Resolve());
        Assert.Equal(
            ["OutlineItemInvalid 5", "OutlineItemInvalid 6", "DestinationInvalid 7", "NamedDestinationNotFound 8"],
            document.Diagnostics.Select(Describe));
    }

    [Fact]
    public void An_outline_deeper_than_the_cap_ends_with_a_diagnostic()
    {
        const int levels = 300;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R >>",
        };
        for (int level = 0; level < levels; level++)
        {
            int number = 5 + level;
            string children = level + 1 < levels ? string.Create(CultureInfo.InvariantCulture, $" /First {number + 1} 0 R /Last {number + 1} 0 R /Count -1") : string.Empty;
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Title (Level {level}) /Parent {(level == 0 ? 4 : number - 1)} 0 R{children} >>"));
        }

        using PdfDocument document = PdfDocument.Open(new TestPdf().Build([.. objects]));

        IReadOnlyList<PdfOutlineItem> items = document.Outline!.Items;

        Assert.Equal(256, Flatten(items).Count());
        Assert.Equal(["OutlineTooDeep 260"], document.Diagnostics.Select(Describe));
    }

    private static IEnumerable<PdfOutlineItem> Flatten(IEnumerable<PdfOutlineItem> items)
    {
        var pending = new Stack<PdfOutlineItem>(items.Reverse());
        while (pending.TryPop(out PdfOutlineItem? item))
        {
            yield return item;
            for (int index = item.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(item.Children[index]);
            }
        }
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
