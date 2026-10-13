using System.Globalization;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Writing destinations (ISO 32000-2 §12.3.2.2, Table 149) and named destinations (§12.3.2.4) in the name dictionary's <c>Dests</c>
/// tree, read back through <see cref="PdfDocument.GetNamedDestination(string)"/> after a save.
/// </summary>
public sealed class DestinationWritingTests
{
    public static TheoryData<PdfDestinationView, double?, double?, double?, double?, double?, string> EveryView => new()
    {
        { PdfDestinationView.Xyz, 72, 700, null, null, 1.5, "/XYZ 72 700 1.5" },
        { PdfDestinationView.Xyz, null, null, null, null, null, "/XYZ null null null" },
        { PdfDestinationView.Fit, null, null, null, null, null, "/Fit" },
        { PdfDestinationView.FitH, null, 650.25, null, null, null, "/FitH 650.25" },
        { PdfDestinationView.FitH, null, null, null, null, null, "/FitH null" },
        { PdfDestinationView.FitV, 36, null, null, null, null, "/FitV 36" },
        { PdfDestinationView.FitR, 10, 500, 300, 20, null, "/FitR 10 20 300 500" },
        { PdfDestinationView.FitB, null, null, null, null, null, "/FitB" },
        { PdfDestinationView.FitBH, null, 400, null, null, null, "/FitBH 400" },
        { PdfDestinationView.FitBV, 50, null, null, null, null, "/FitBV 50" },
    };

    [Theory]
    [MemberData(nameof(EveryView))]
    public void Every_view_of_table_149_is_written_with_its_parameters_in_order(
        PdfDestinationView view, double? left, double? top, double? right, double? bottom, double? zoom, string expected)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];

        PdfExplicitDestination destination = document.CreateDestination(page, view, left, top, right, bottom, zoom);

        Assert.Equal(page.Reference, destination.Array[0]);
        Assert.Equal(expected, string.Join(' ', destination.Array.Skip(1).Select(value => value.ToString())));
        Assert.Equal((view, 0, true), (destination.View, destination.PageIndex, destination.IsValid));
        Assert.Equal((left, top, right, bottom), (destination.Left, destination.Top, destination.Right, destination.Bottom));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_destination_needs_the_parameters_of_its_view_and_no_others()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];

        Assert.Throws<ArgumentException>(() => document.CreateDestination(page, PdfDestinationView.FitR, left: 0, top: 1, right: 1));
        Assert.Throws<ArgumentException>(() => document.CreateDestination(page, PdfDestinationView.Fit, top: 1));
        Assert.Throws<ArgumentException>(() => document.CreateDestination(page, PdfDestinationView.FitH, zoom: 2));
        Assert.Throws<ArgumentException>(() => document.CreateDestination(page, PdfDestinationView.Xyz, left: double.NaN));
        Assert.Throws<ArgumentException>(() => document.CreateDestination(page, PdfDestinationView.Unknown));
    }

    [Fact]
    public void Named_destinations_written_to_a_new_document_resolve_after_a_save_and_from_an_outline_item()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];

        PdfNamedDestination intro = document.SetNamedDestination("Intro", document.CreateDestination(page, PdfDestinationView.FitH, top: 700));
        document.SetNamedDestination("章 1", document.CreateDestination(page, PdfDestinationView.Xyz, 0, 792, zoom: 2));
        document.SetNamedDestination(new CosString([0x80, 0x00, 0xFF]), document.CreateDestination(page, PdfDestinationView.Fit));
        document.CreateOutline().Add("Introduction").Destination = intro;

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal("FitH - - - 700 -", Describe(reopened.GetNamedDestination("Intro")!));
        Assert.Equal("Xyz 0 - - 792 2", Describe(reopened.GetNamedDestination("章 1")!));
        Assert.Equal("Fit - - - - -", Describe(reopened.GetNamedDestination(new CosString([0x80, 0x00, 0xFF]))!));
        PdfNamedDestination named = Assert.IsType<PdfNamedDestination>(reopened.Outline!.Items[0].Destination);
        Assert.Equal(0, named.Resolve()!.PageIndex);
        Assert.Equal(
            ["496E74726F", "8000FF", "FEFF7AE000200031"],
            reopened.Names!.Dests!.Select(entry => Convert.ToHexString(entry.Key.Bytes)).Order(StringComparer.Ordinal));
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode is 0 or 3 && !output.Contains("ERROR", StringComparison.Ordinal), output);
    }

    [Fact]
    public void Setting_an_existing_name_replaces_its_destination_and_the_legacy_entry_of_that_name()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));
        int count = document.Names!.Dests!.Count();
        PdfPage page = document.Pages[0];

        document.SetNamedDestination("alpha", document.CreateDestination(page, PdfDestinationView.FitV, left: 10));
        document.SetNamedDestination("Chap6", document.CreateDestination(page, PdfDestinationView.FitB));

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(count + 1, reopened.Names!.Dests!.Count());
        Assert.Equal("FitV 10 - - - -", Describe(reopened.GetNamedDestination("alpha")!));
        Assert.Equal("FitB - - - - -", Describe(reopened.GetNamedDestination(new CosName("Chap6"))!));
        Assert.Equal("FitB - - - - -", Describe(reopened.GetNamedDestination("Chap6")!));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Removing_a_named_destination_removes_it_from_the_tree_and_the_catalog_dests()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("destinations-all.pdf"));
        int count = document.Names!.Dests!.Count();

        Assert.True(document.RemoveNamedDestination("alpha"));
        Assert.True(document.RemoveNamedDestination(new CosString("Chap6"u8)));
        Assert.False(document.RemoveNamedDestination("alpha"));
        Assert.False(document.RemoveNamedDestination("no such name"));

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Null(reopened.GetNamedDestination("alpha"));
        Assert.Null(reopened.GetNamedDestination(new CosName("Chap6")));
        Assert.Equal(count - 1, reopened.Names!.Dests!.Count());
    }

    [Fact]
    public void A_destination_of_another_document_or_a_remote_one_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfDocument other = PdfDocument.Create();

        PdfExplicitDestination foreign = other.CreateDestination(other.Pages[0], PdfDestinationView.Fit);

        Assert.Throws<ArgumentException>(() => document.SetNamedDestination("x", foreign));
        Assert.Throws<ArgumentException>(() => document.CreateOutline().Add("x").Destination = foreign);
    }

    private static string Describe(PdfExplicitDestination destination) =>
        string.Join(' ', [destination.View.ToString(), .. new[] { destination.Left, destination.Bottom, destination.Right, destination.Top, destination.Zoom }
            .Select(value => value?.ToString(CultureInfo.InvariantCulture) ?? "-")]);

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }
}
