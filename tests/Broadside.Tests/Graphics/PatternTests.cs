using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;
using static Broadside.Tests.Graphics.ShadingTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Tiling and shading patterns through the content interpreter: the paint's colour selects the pattern, the pattern matrix maps to
/// the space of the stream that selected it (ISO 32000-2 §8.7.2), and a tiling pattern's cell runs on demand (§8.7.3.1).
/// </summary>
public class PatternTests
{
    [Fact]
    public void A_coloured_tiling_pattern_reports_its_cell_and_maps_to_the_page_space_not_the_ctm_at_the_fill()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-colored.pdf"));
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        PaintSeen fill = Assert.Single(processor.Paints, paint => paint.Depth == 0);
        PdfTilingPattern pattern = Assert.IsType<PdfTilingPattern>(fill.Pattern);
        Assert.Equal(new Matrix(2, 0, 0, 2, 0, 0), fill.Ctm);
        Assert.Equal(Matrix.Identity, fill.Color.PatternMatrix);
        Assert.Equal(new Matrix(1, 0, 0, 1, 10, 10), pattern.Matrix);
        Assert.Equal(new Matrix(1, 0, 0, 1, 10, 10), pattern.GetPatternSpace(fill.Color));
        Assert.Equal(new PdfRectangle(0, 0, 20, 20), pattern.BoundingBox);
        Assert.Equal((25.0, 25.0), (pattern.XStep, pattern.YStep));
        Assert.Equal(PdfTilingPaintType.Colored, pattern.PaintType);
        Assert.Equal(PdfTilingType.ConstantSpacing, pattern.TilingType);
        Assert.NotNull(pattern.Resources);
        Assert.Equal("1 0 0 rg 0 0 10 10 re f 0 0 1 rg 10 10 10 10 re f"u8.ToArray(), pattern.GetContent().ToArray());
        Assert.True(pattern.IsValid);

        // The cell ran in pattern space, clipped to its BBox, with its own colours.
        Assert.Equal(
            [
                "q 1",
                "Clip Rectangle [1 0 0 1 10 10] M 0,0 L 20,0 L 20,20 L 0,20 Z",
                "Paint depth 1 Pattern [1 0 0 1 10 10] DeviceRgb 1 0 0",
                "Paint depth 1 Pattern [1 0 0 1 10 10] DeviceRgb 0 0 1",
                "Q 0",
            ],
            processor.Cell);
        Assert.Equal(new PdfVersion(1, 2), pattern.MinimumVersion);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_uncoloured_tiling_pattern_paints_its_cell_in_the_underlying_colour_given_with_scn()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-uncolored.pdf"));
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        PaintSeen fill = Assert.Single(processor.Paints, paint => paint.Depth == 0);
        PdfTilingPattern pattern = Assert.IsType<PdfTilingPattern>(fill.Pattern);
        Assert.Equal(PdfTilingPaintType.Uncolored, pattern.PaintType);
        Assert.Equal(PdfTilingType.NoDistortion, pattern.TilingType);
        Assert.Equal([0.8f, 0.2f, 0], fill.Color.Components.ToArray());
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, ((PdfPatternColorSpace)fill.Color.ColorSpace).Underlying!.Family);
        Assert.Contains("Paint depth 1 Pattern [1 0 0 1 0 0] DeviceRgb 0.8 0.2 0", processor.Cell);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_uncoloured_cell_ignores_colour_operators_and_sh_and_keeps_the_underlying_colour()
    {
        string cell = StreamObject(
            "/PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << /Shading << /S 6 0 R >> >>",
            "1 0 0 rg /S sh 0 0 5 5 re f");
        byte[] file = Page(
            "/C cs 0 1 0 /P scn 0 0 100 100 re f",
            "<< /ColorSpace << /C [/Pattern /DeviceRGB] >> /Pattern << /P 5 0 R >> >>",
            cell,
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>");
        using PdfDocument document = PdfDocument.Open(file);
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        Assert.Contains("Paint depth 1 Pattern [1 0 0 1 0 0] DeviceRgb 0 1 0", processor.Cell);
        Assert.Empty(processor.Shadings);
        Assert.Equal(["ContentColorOperatorIgnored"], document.Codes());
    }

    [Fact]
    public void A_shading_pattern_reports_its_shading_matrix_background_and_graphics_state()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-shading-axial.pdf"));
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        PaintSeen fill = Assert.Single(processor.Paints);
        PdfShadingPattern pattern = Assert.IsType<PdfShadingPattern>(fill.Pattern);
        Assert.Equal(PdfPatternType.Shading, pattern.PatternType);
        Assert.Equal(new Matrix(0.5, 0, 0, 0.5, 0, 0), pattern.GetPatternSpace(fill.Color));
        PdfAxialShading shading = Assert.IsType<PdfAxialShading>(pattern.Shading);
        Assert.Equal([0.9f, 0.9f, 0.9f], shading.Background);
        Assert.Equal(new PathPoint(200, 0), shading.Start);
        Assert.Equal(0.5, Assert.IsAssignableFrom<CosNumber>(pattern.ExtGState![new CosName("CA")]).ToDouble());
        Assert.Equal(new PdfVersion(1, 3), pattern.MinimumVersion);
        Assert.False(processor.RanCell);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_pattern_selected_inside_a_cell_maps_to_the_outer_pattern_space()
    {
        // §8.7.2: "the inner pattern's matrix defines its relationship to the pattern space of the outer pattern".
        using PdfDocument document = PdfDocument.Open(NestedPatterns());
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        PaintSeen innerFill = Assert.Single(processor.Paints, paint => paint.Depth == 1);
        Assert.Equal(new Matrix(2, 0, 0, 2, 7, 0), innerFill.Color.PatternMatrix);
        Assert.Equal(new Matrix(6, 0, 0, 6, 7, 0), innerFill.Ctm);
        Assert.Equal(new Matrix(2, 0, 0, 2, 7, 8), innerFill.Pattern!.GetPatternSpace(innerFill.Color));
        Assert.Contains("Paint depth 2 Pattern [2 0 0 2 7 8] DeviceGray 0", processor.Cell);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_pattern_matrix_of_pattern_in_form_pdf_belongs_to_the_form_resources()
    {
        // The form's run (issue #56) gives its patterns the base CTM at Do x form Matrix; here the model is read from its resources.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-in-form.pdf"));
        var form = (CosStream)document.Resolve(((CosDictionary)document.Resolve(document.Pages[0].Resources![new CosName("XObject")]))[new CosName("Fm0")]);
        var patterns = (CosDictionary)document.Resolve(((CosDictionary)document.Resolve(form.Dictionary[new CosName("Resources")]))[new CosName("Pattern")]);

        PdfTilingPattern pattern = Assert.IsType<PdfTilingPattern>(document.GetPattern(patterns[new CosName("P1")]));

        Assert.Equal(new CosReference(6, 0), pattern.Reference);
        Assert.Equal(Matrix.Identity, pattern.Matrix);
        Assert.Equal(new PdfRectangle(0, 0, 20, 20), pattern.BoundingBox);
    }

    [Fact]
    public void A_cell_that_paints_with_its_own_pattern_stops_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-recursive.pdf"));
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor);

        Assert.Equal([false, true], processor.CellResults);
        Assert.Equal(2, processor.Paints.Count);
        Assert.Equal(["ContentPatternRecursion"], document.Codes());
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(document.Diagnostics).Severity);
    }

    [Fact]
    public void A_recursive_pattern_throws_in_strict_mode()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-recursive.pdf"), new PdfOptions().UseStrict());

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.Pages[0].ProcessContent(new CellRunner()));

        Assert.Equal("ContentPatternRecursion", error.Diagnostic.Code);
    }

    [Fact]
    public void Nesting_deeper_than_the_limit_stops_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(NestedPatterns());
        var processor = new CellRunner();

        document.Pages[0].ProcessContent(processor, new ContentOptions().WithMaxNestingDepth(1));

        Assert.Equal([false, true], processor.CellResults);
        Assert.Equal(["ContentNestingTooDeep"], document.Codes());
    }

    [Fact]
    public void Paint_events_after_a_cell_still_see_the_outer_path_state_and_clip()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-colored.pdf"));
        var recorder = new RecordingProcessor();
        var processor = new CellRunner(recorder);

        document.Pages[0].ProcessContent(processor);

        Assert.Equal(
            ["Paint Fill NonZero M 0,0 L 200,0 L 200,200 L 0,200 Z ctm=[2 0 0 2 0 0]"],
            recorder.Semantic.Where(line => line.StartsWith("Paint", StringComparison.Ordinal) && line.Contains("ctm=[2", StringComparison.Ordinal)));
        Assert.Equal(0, processor.FinalDepth);
    }

    /// <summary>A page filling with a tiling pattern whose cell, after a cm, fills with another tiling pattern.</summary>
    private static byte[] NestedPatterns()
    {
        string outer = StreamObject(
            "/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 50 50] /XStep 50 /YStep 50 /Matrix [2 0 0 2 7 0] /Resources << /Pattern << /Inner 6 0 R >> >>",
            "3 0 0 3 0 0 cm /Pattern cs /Inner scn 0 0 10 10 re f");
        string inner = StreamObject(
            "/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 5 5] /XStep 5 /YStep 5 /Matrix [1 0 0 1 0 4] /Resources << >>",
            "0 0 5 5 re f");
        return Page("1 0 0 1 100 0 cm /Pattern cs /Outer scn 0 0 200 200 re f", "<< /Pattern << /Outer 5 0 R >> >>", outer, inner);
    }

    /// <summary>What a paint saw: its pattern, colour and CTM, and how deeply nested it was.</summary>
    private sealed record PaintSeen(int Depth, PdfPattern? Pattern, PdfColor Color, Matrix Ctm);

    /// <summary>Records paints with a pattern colour and runs each tiling pattern's cell from the paint, as a renderer would.</summary>
    private sealed class CellRunner(RecordingProcessor? inner = null) : ContentProcessor
    {
        public List<PaintSeen> Paints { get; } = [];

        public List<string> Cell { get; } = [];

        public List<bool> CellResults { get; } = [];

        public List<string> Shadings { get; } = [];

        public bool RanCell => CellResults.Count > 0;

        public int FinalDepth { get; private set; } = -1;

        public override void SaveState(ContentContext context) => Note(context, $"q {context.StateDepth}");

        public override void RestoreState(ContentContext context) => Note(context, $"Q {context.StateDepth}");

        public override void IntersectClip(in ClipEvent clip, ContentContext context) =>
            Note(context, $"Clip {clip.Kind} {RecordingProcessor.Matrix(clip.Ctm)} {RecordingProcessor.Path(clip.Path)}");

        public override void PaintShading(in ShadingEvent shading, ContentContext context) => Shadings.Add("sh");

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            PdfColor color = context.State.FillColor;
            Note(context, $"Paint depth {context.Depth} {context.RunKind} {RecordingProcessor.Matrix(context.State.Ctm)} {Describe(color)}");
            if (color.ColorSpace is PdfPatternColorSpace)
            {
                PdfPattern? pattern = context.GetPattern(color);
                Paints.Add(new PaintSeen(context.Depth, pattern, color, context.State.Ctm));
                if (pattern is PdfTilingPattern)
                {
                    CellResults.Add(context.RunPatternCell(color, this));
                }
            }

            // After the cell, the outer paint's path and state are what they were.
            inner?.PaintPath(path, context);
        }

        public override void EndRun(ContentContext context) => FinalDepth = context.StateDepth;

        private static string Describe(PdfColor color) =>
            $"{color.ColorSpace.Family} {string.Join(' ', color.Components.ToArray().Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture)))}".TrimEnd();

        private void Note(ContentContext context, string line)
        {
            if (context.Depth > 0)
            {
                Cell.Add(line);
            }
        }
    }
}
