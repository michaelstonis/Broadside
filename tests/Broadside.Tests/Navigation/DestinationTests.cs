using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Explicit and named destinations (ISO 32000-2 §12.3.2.2, Table 149, and §12.3.2.4) read through
/// <see cref="PdfDocument.GetNamedDestination(string)"/> and outline items.
/// </summary>
public class DestinationTests
{
    /// <summary>Key in destinations-all.pdf, then "View left bottom right top zoom" with "-" for null.</summary>
    public static TheoryData<string, string> EveryForm => new()
    {
        { "alpha", "Xyz 0 - - 792 -" },
        { "fit", "Fit - - - - -" },
        { "fitb", "FitB - - - - -" },
        { "fitbh", "FitBH - - - 700 -" },
        { "fitbv", "FitBV 36 - - - -" },
        { "fith", "FitH - - - 650 -" },
        { "fith-null", "FitH - - - - -" },
        { "fitr", "FitR 10 20 300 400.5 -" },
        { "fitv", "FitV 50 - - - -" },
        { "indirect", "FitH - - - 500 -" },
        { "with-d", "Fit - - - - -" },
        { "xyz", "Xyz 72 - - 720 1.5" },
        { "xyz-null", "Xyz - - - - -" },
        { "xyz-zero", "Xyz 10 - - 20 -" },
    };

    [Fact]
    public void Name_tree_dests_pdf_resolves_its_three_named_destinations_to_the_first_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-dests.pdf"));

        PdfExplicitDestination alpha = Assert.IsType<PdfExplicitDestination>(document.GetNamedDestination("alpha"));
        PdfExplicitDestination beta = Assert.IsType<PdfExplicitDestination>(document.GetNamedDestination(new CosString("beta"u8)));
        PdfExplicitDestination gamma = Assert.IsType<PdfExplicitDestination>(document.GetNamedDestination("gamma"));

        Assert.Equal("Fit - - - - -", Describe(alpha));
        Assert.Equal("Xyz 0 - - 792 -", Describe(beta)); // zoom 0 keeps the current zoom, like null
        Assert.Equal("FitH - - - 792 -", Describe(gamma));
        Assert.All([alpha, beta, gamma], destination =>
        {
            Assert.Equal(0, destination.PageIndex);
            Assert.Same(document.Pages[0], destination.Page);
            Assert.Equal(PdfDestinationTarget.Page, destination.TargetKind);
            Assert.True(destination.IsValid);
            Assert.False(destination.IsRemote);
        });
        Assert.Null(document.GetNamedDestination("delta"));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void Every_explicit_form_of_table_149_is_typed_with_null_meaning_unchanged(string name, string expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));

        PdfExplicitDestination destination = Assert.IsType<PdfExplicitDestination>(document.GetNamedDestination(name));

        Assert.Equal(expected, Describe(destination));
        Assert.True(destination.IsValid);
        Assert.Equal(0, destination.PageIndex);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_pdf_1_1_name_resolves_through_the_catalog_dests_dictionary()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));

        Assert.Equal("FitV 72 - - - -", Describe(document.GetNamedDestination(new CosName("Chap6"))!));
        Assert.Equal("FitV 72 - - - -", Describe(document.GetNamedDestination("Chap6")!));
        Assert.Null(document.GetNamedDestination(new CosName("Chap7")));

        // A string resolves through the tree first, a name through the catalog dictionary first; each falls back to the other.
        Assert.Equal("Xyz 0 - - 792 -", Describe(document.GetNamedDestination(new CosName("alpha"))!));
        Assert.Equal("FitV 72 - - - -", Describe(document.GetNamedDestination(new CosString("Chap6"u8))!));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Outline_items_refer_to_destinations_by_name_and_by_string()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));

        IReadOnlyList<PdfOutlineItem> items = document.Outline!.Items;

        PdfNamedDestination byName = Assert.IsType<PdfNamedDestination>(items[0].Destination);
        Assert.Equal(new CosName("Chap6"), byName.Name);
        Assert.Equal("Chap6", byName.Text);
        Assert.Equal("FitV 72 - - - -", Describe(byName.Resolve()!));
        PdfNamedDestination byString = Assert.IsType<PdfNamedDestination>(items[1].Destination);
        Assert.Equal(new CosString("alpha"u8), byString.Name);
        Assert.Equal("Xyz 0 - - 792 -", Describe(byString.Resolve()!));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Malformed_destinations_are_read_leniently_each_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(MalformedDestinations());

        Assert.Equal("Xyz 10 - - - -", Describe(Named(document, "missing")));
        Assert.Equal("FitH - - - 1 -", Describe(Named(document, "extra")));
        PdfExplicitDestination fitR = Named(document, "fitr");
        Assert.False(fitR.IsValid);
        PdfExplicitDestination number = Named(document, "int");
        Assert.Equal(PdfDestinationTarget.PageNumber, number.TargetKind);
        Assert.Equal(0, number.PageIndex);
        PdfExplicitDestination notInTree = Named(document, "nopage");
        Assert.Equal(PdfDestinationTarget.Page, notInTree.TargetKind);
        Assert.Null(notInTree.PageIndex);
        Assert.Equal("Xyz - - - 1 1", Describe(Named(document, "text")));
        PdfExplicitDestination unknown = Named(document, "unknown");
        Assert.Equal(PdfDestinationView.Unknown, unknown.View);
        Assert.Equal(new CosName("FitW"), unknown.ViewName);
        Assert.False(unknown.IsValid);
        Assert.Equal(0, unknown.PageIndex);
        Assert.Null(document.GetNamedDestination("chained"));

        Assert.Equal(
            [
                "DestinationInvalid 5", // [3 0 R /XYZ 10]: top and zoom missing
                "DestinationInvalid 6", // [3 0 R /FitH 1 2]: one parameter too many
                "DestinationInvalid 7", // [3 0 R /FitR 1 2 3]: a FitR needs four coordinates
                "DestinationInvalid 8", // [0 /Fit]: a page number in a local destination
                "DestinationPageNotFound 9", // [12 0 R /Fit]: a page object outside the page tree
                "DestinationInvalid 10", // [3 0 R /XYZ (a) 1 1]: a parameter that is not a number
                "DestinationInvalid 11", // [3 0 R /FitW 5]: not a view of Table 149
                "DestinationInvalid -", // (chained) (fit): a value that is a string
            ],
            document.Diagnostics.Select(Describe));
    }

    [Fact]
    public void Strict_mode_throws_when_a_malformed_destination_is_read_not_at_open()
    {
        using PdfDocument document = PdfDocument.Open(MalformedDestinations(), new PdfOptions().UseStrict());

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.GetNamedDestination("missing"));

        Assert.Equal("DestinationInvalid 5", Describe(error.Diagnostic));
    }

    [Fact]
    public void Reading_destinations_writes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));

        foreach (string name in (string[])["alpha", "fit", "fitb", "fitbh", "fitbv", "fith", "fith-null", "fitr", "fitv", "indirect", "with-d", "xyz", "xyz-null", "xyz-zero"])
        {
            PdfExplicitDestination destination = document.GetNamedDestination(name)!;
            _ = (destination.Page, destination.Zoom, destination.Left, destination.Top, destination.Right, destination.Bottom, destination.View);
        }

        _ = document.Outline!.Items.Select(item => item.Destination).OfType<PdfNamedDestination>().Select(named => named.Resolve()).ToList();

        Assert.All(Enumerable.Range(1, 9), number => Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty));
    }

    private static byte[] MalformedDestinations() => new TestPdf().Build(
        "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        "<< /Names [(chained) (fit) (extra) 6 0 R (fitr) 7 0 R (int) 8 0 R (missing) 5 0 R (nopage) 9 0 R (text) 10 0 R (unknown) 11 0 R] >>",
        "[3 0 R /XYZ 10]",
        "[3 0 R /FitH 1 2]",
        "[3 0 R /FitR 1 2 3]",
        "[0 /Fit]",
        "[12 0 R /Fit]",
        "[3 0 R /XYZ (a) 1 1]",
        "[3 0 R /FitW 5]",
        "<< /Type /Page /MediaBox [0 0 100 100] /Resources << >> >>");

    private static PdfExplicitDestination Named(PdfDocument document, string name)
    {
        PdfExplicitDestination destination = Assert.IsType<PdfExplicitDestination>(document.GetNamedDestination(name));
        _ = destination.PageIndex;
        return destination;
    }

    private static string Describe(PdfExplicitDestination destination) =>
        string.Join(' ', [destination.View.ToString(), .. new[] { destination.Left, destination.Bottom, destination.Right, destination.Top, destination.Zoom }
            .Select(value => value?.ToString(CultureInfo.InvariantCulture) ?? "-")]);

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
