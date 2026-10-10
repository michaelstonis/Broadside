using System.Text;
using Broadside.Content;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Marked content (ISO 32000-2 §14.6): tags, property lists inline or named in the resources, MCIDs (§14.7.5.2) resolved through the
/// stream's StructParents, and optional content sections (§8.11.3.2) that hide what they enclose.
/// </summary>
public class MarkedContentTests
{
    [Fact]
    public void Every_operator_of_marked_content_pdf_is_reported_with_its_properties()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("marked-content.pdf"));
        var recorder = new MarkedRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(
        [
            "Begin Artifact depth=1 props=none mcid= hidden=False",
            "Paint hidden=False",
            "End Artifact depth=1",
            "Begin P depth=1 props=inline mcid=0 hidden=False",
            "Glyph 97",
            "End P depth=1",
            "Begin Span depth=1 props=named(Lang=en) mcid=1 hidden=False",
            "Paint hidden=False",
            "End Span depth=1",
            "Point Pt depth=0 props=none",
            "Point Pt2 depth=0 props=inline",
            "Begin OC depth=1 props=named(Lang=) mcid= hidden=True oc=True",
            "End OC depth=1",
            "Begin Span depth=1 props=inline mcid= hidden=False",
            "Glyph 121",
            "End Span depth=1",
        ],
            recorder.Lines);
        Assert.Equal("x", recorder.ActualText);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Hidden_content_is_reported_flagged_to_a_processor_that_asks_for_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("marked-content.pdf"));
        var recorder = new MarkedRecorder(ContentEvents.MarkedContent | ContentEvents.Paths | ContentEvents.Glyphs | ContentEvents.HiddenContent);

        document.Pages[0].ProcessContent(recorder);

        Assert.Contains("Paint hidden=True", recorder.Lines);
    }

    [Fact]
    public void Hidden_text_still_advances_the_text_matrix_and_changes_the_state()
    {
        string group = "<< /Type /OCG /Name (Off) >>";
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWithCatalog(
            "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            ContentPdf.HelveticaResources + " /Properties << /oc 6 0 R >>",
            "BT /F1 10 Tf /OC /oc BDC (a) Tj 2 Tc EMC (b) Tj ET",
            ContentPdf.Helvetica,
            group));
        var recorder = new GlyphRecorder(ContentEvents.Glyphs);

        document.Pages[0].ProcessContent(recorder);

        // §8.11.3.1: "a" is not reported, but it moved the text matrix by 5.56 and Tc 2 still applies to "b".
        RecordedGlyph b = Assert.Single(recorder.Glyphs);
        Assert.Equal(98u, b.Code);
        Assert.Equal(5.56, b.DeviceOrigin.X, 9);
        Assert.Equal(7.56, b.AdvanceX, 9);
    }

    [Fact]
    public void Optional_content_sections_and_memberships_of_optional_content_pdf_hide_their_paints()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        var recorder = new PaintOrigins(ContentEvents.Paths | ContentEvents.HiddenContent);

        document.Pages[0].ProcessContent(recorder);

        // /a visible, /m1 (A and B all off: false) hidden, /m2 (A or not B) visible, /a inside /b (off) hidden; the form /OC B is not run.
        Assert.Equal([(0.0, false), (20.0, true), (40.0, false), (60.0, true)], recorder.Paints);
    }

    [Fact]
    public void The_states_in_the_options_decide_what_is_hidden()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        PdfOptionalContentProperties properties = document.OptionalContent!;
        PdfOptionalContentState state = properties.GetDefaultStates().With(properties.Groups[1], on: true);
        var recorder = new PaintOrigins(ContentEvents.Paths);

        document.Pages[0].ProcessContent(recorder, new ContentOptions().WithOptionalContentState(state));

        // With B on, /b is visible, /m1 is still hidden (A is on) and the form is drawn.
        Assert.Equal([0.0, 40.0, 60.0, 80.0], recorder.Paints.Select(paint => paint.X));
    }

    [Fact]
    public void Each_mcid_of_tagged_structure_pdf_finds_the_element_tagged_like_its_sequence()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        var recorder = new StructureRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(10, recorder.Pairs.Count);
        Assert.All(recorder.Pairs, pair => Assert.Equal(pair.Tag, pair.Element));
        Assert.All(recorder.StructParents, key => Assert.Equal(0, key));
    }

    [Fact]
    public void Unbalanced_emc_and_a_missing_named_property_list_are_reported()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("EMC /Span /Nope BDC EMC /Open BMC", ContentEvents.MarkedContent);

        Assert.Equal(["BMC", "EMC", "BMC", "EMC"], events.Body);
        Assert.Equal(["ContentMarkedContentUnbalanced", "ContentPropertiesMissing"], ContentPdf.Codes(diagnostics));
    }

    private sealed class MarkedRecorder(ContentEvents events = ContentEvents.MarkedContent | ContentEvents.Paths | ContentEvents.Glyphs) : ContentProcessor
    {
        public List<string> Lines { get; } = [];

        public string? ActualText { get; private set; }

        public override ContentEvents Events => events;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            string oc = mc.IsOptionalContent ? " oc=True" : string.Empty;
            Lines.Add($"Begin {Encoding.Latin1.GetString(mc.Tag)} depth={mc.Depth} props={Props(mc)} mcid={mc.Mcid} hidden={mc.IsHidden}{oc}");
            if (mc.InlineProperties.Kind == ContentOperandKind.Dictionary && mc.InlineProperties.ToCosObject() is CosDictionary inline
                && inline.TryGetValue(new CosName("ActualText"), out CosObject? text))
            {
                ActualText = ((CosString)text).DecodeText();
            }
        }

        public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context) =>
            Lines.Add($"End {Encoding.Latin1.GetString(mc.Tag)} depth={mc.Depth}");

        public override void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context) =>
            Lines.Add($"Point {Encoding.Latin1.GetString(mc.Tag)} depth={mc.Depth} props={Props(mc)}");

        public override void PaintPath(in PathEvent path, ContentContext context) => Lines.Add($"Paint hidden={path.IsHidden}");

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => Lines.Add($"Glyph {glyph.CharacterCode}");

        private static string Props(in MarkedContentEvent mc) =>
            mc.Properties is { } named ? $"named(Lang={(named.TryGetValue(new CosName("Lang"), out CosObject? lang) ? ((CosString)lang).DecodeText() : string.Empty)})"
            : mc.InlineProperties.Kind == ContentOperandKind.Dictionary ? "inline"
            : "none";
    }

    private sealed class PaintOrigins(ContentEvents events) : ContentProcessor
    {
        public List<(double X, bool Hidden)> Paints { get; } = [];

        public override ContentEvents Events => events;

        public override void PaintPath(in PathEvent path, ContentContext context) =>
            Paints.Add((context.State.Ctm.Transform(path.Path.Bounds.Left, 0).X, path.IsHidden));
    }

    private sealed class StructureRecorder : ContentProcessor
    {
        public List<(string Tag, string? Element)> Pairs { get; } = [];

        public List<int?> StructParents { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            if (mc.Mcid is { } mcid)
            {
                Pairs.Add((Encoding.Latin1.GetString(mc.Tag), context.FindStructureElement(mcid)?.StandardType?.Name));
                StructParents.Add(mc.StructParents);
            }
        }
    }
}
