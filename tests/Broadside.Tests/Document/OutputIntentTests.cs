using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Output intents (ISO 32000-2 §14.11.5, Tables 401 and 402): read from the catalog and from a page (PDF 2.0), their ICC profile as
/// an ICCBased space, and the output intent in effect carried with every colour of a page's display list.
/// </summary>
public sealed class OutputIntentTests
{
    [Fact]
    public void The_catalog_s_output_intent_reads_every_table_401_entry_and_its_profile()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"));

        PdfOutputIntent intent = Assert.Single(document.OutputIntents);
        Assert.Equal("GTS_PDFX", intent.Subtype);
        Assert.Equal("Broadside made-up press", intent.OutputCondition);
        Assert.Equal("Custom", intent.OutputConditionIdentifier);
        Assert.Equal("http://www.color.org", intent.RegistryName);
        Assert.Equal("A made-up CMYK press for tests", intent.Info);
        Assert.Equal(new CosReference(6, 0), intent.Reference);
        PdfIccBasedColorSpace profile = intent.DestinationProfile!;
        Assert.Equal(4, profile.ComponentCount);
        Assert.Equal("prtr", profile.ProfileHeader!.DeviceClass);
        Assert.Equal("CMYK", profile.ProfileHeader.DataColorSpace);
        Assert.Same(PdfDeviceCmykColorSpace.Instance, profile.Alternate);
        Assert.Same(intent, document.OutputIntents[0]);
        Assert.Same(profile, intent.DestinationProfile);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_page_s_own_output_intent_is_read_from_the_page_and_others_have_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"));

        Assert.Empty(document.Pages[0].OutputIntents);
        PdfOutputIntent intent = Assert.Single(document.Pages[1].OutputIntents);
        Assert.Null(intent.Reference);
        Assert.Equal("A made-up newsprint press", intent.Info);
        Assert.Null(intent.OutputCondition);
        Assert.NotSame(document.OutputIntents[0].DestinationProfile!.Stream, intent.DestinationProfile!.Stream);
        using PdfDocument empty = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        Assert.Empty(empty.OutputIntents);
    }

    [Fact]
    public void Every_colour_of_a_page_carries_the_output_intent_in_effect_and_a_page_s_own_takes_precedence()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"));
        var first = new RecordingBackend();
        var second = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(first);
        document.Pages[1].CreateDisplayList().Replay(second);

        Assert.Equal(9, first.Fills.Count);
        Assert.All(first.Fills, fill => Assert.Same(document.OutputIntents[0], fill.Conversion.OutputIntent));
        Assert.All(second.Fills, fill => Assert.Same(document.Pages[1].OutputIntents[0], fill.Conversion.OutputIntent));

        using PdfDocument none = PdfDocument.Open(Corpus.Path("paths-fill-rules.pdf"));
        var recorder = new RecordingBackend();
        none.Pages[0].CreateDisplayList().Replay(recorder);
        Assert.All(recorder.Fills, fill => Assert.Null(fill.Conversion.OutputIntent));
    }

    [Fact]
    public void Malformed_output_intents_are_skipped_with_a_diagnostic_and_strict_mode_throws()
    {
        using PdfDocument document = PdfDocument.Create();
        CosReference profile = document.AddObject(new CosDictionary { [new CosName("S")] = new CosName("GTS_PDFX"), [new CosName("DestOutputProfile")] = new CosInteger(5) });
        CosReference subtype = document.AddObject(new CosDictionary { [new CosName("S")] = new CosInteger(2) });
        document.Catalog[new CosName("OutputIntents")] = new CosArray([new CosInteger(1), profile, subtype]);

        IReadOnlyList<PdfOutputIntent> intents = document.OutputIntents;

        Assert.Equal(2, intents.Count);
        Assert.Null(intents[0].DestinationProfile);
        Assert.Null(intents[1].Subtype);
        Assert.Equal(3, document.Diagnostics.Count(d => d.Code == "OutputIntentInvalid"));
        document.Pages[0].Dictionary[new CosName("OutputIntents")] = new CosName("Nope");
        Assert.Empty(document.Pages[0].OutputIntents);
        Assert.Equal(4, document.Diagnostics.Count(d => d.Code == "OutputIntentInvalid" && d.Severity == DiagnosticSeverity.Warning));

        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        strict.Catalog[new CosName("OutputIntents")] = new CosArray([new CosInteger(1)]);
        Assert.Equal("OutputIntentInvalid", Assert.Throws<DiagnosticException>(() => strict.OutputIntents).Diagnostic.Code);
    }
}
