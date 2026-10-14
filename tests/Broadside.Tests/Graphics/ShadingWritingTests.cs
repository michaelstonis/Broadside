using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Shadings written through the <see cref="PdfDocument"/> factories and painted with <see cref="PdfCanvas.PaintShading"/>: after a
/// save and a reopen, each re-reads through the shading models (#79) with the geometry and colours it was given. ISO 32000-2
/// §8.7.4.3 (Table 77), §8.7.4.5 (Tables 78 to 85) and §7.10.3, §7.10.4 (the functions gradient stops become).
/// </summary>
public sealed class ShadingWritingTests
{
    [Fact]
    public void An_axial_shading_from_two_stops_re_reads_with_its_axis_and_colours()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfAxialShading shading = document.CreateAxialShading(
            new PathPoint(100, 200),
            new PathPoint(400, 200),
            [new PdfGradientStop(0, PdfColor.FromRgb(1, 0, 0)), new PdfGradientStop(1, PdfColor.FromRgb(0, 0, 1))]);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.PaintShading(shading);
        }

        ShadingRecorder recorder = Replay(document);

        PdfAxialShading read = Assert.IsType<PdfAxialShading>(Assert.Single(recorder.Shadings));
        Assert.True(read.IsValid);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, read.ColorSpace.Family);
        Assert.Equal(new PathPoint(100, 200), read.Start);
        Assert.Equal(new PathPoint(400, 200), read.End);
        Assert.Equal([0.0, 1.0], read.Domain);
        Assert.False(read.ExtendStart);
        Assert.False(read.ExtendEnd);
        Assert.Equal([1f, 0f, 0f], Evaluate(read, 0));
        Assert.Equal([0.5f, 0f, 0.5f], Evaluate(read, 0.5f));
        Assert.Equal([0f, 0f, 1f], Evaluate(read, 1));
    }

    /// <summary>Saves, reopens and records the shadings painted on the first page and the pattern colours it fills with.</summary>
    internal static ShadingRecorder Replay(PdfDocument document, int page = 0)
    {
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var recorder = new ShadingRecorder();
        reopened.Pages[page].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder;
    }

    internal static float[] Evaluate(PdfShading shading, params float[] input)
    {
        float[] color = new float[shading.ColorComponentCount];
        shading.EvaluateFunction(input, color);
        return [.. color.Select(value => MathF.Round(value, 4))];
    }

    /// <summary>Records the shading models painted with <c>sh</c>, and the pattern of every pattern-coloured fill or stroke.</summary>
    internal sealed class ShadingRecorder : ContentProcessor
    {
        public List<PdfShading> Shadings { get; } = [];

        public List<Matrix> ShadingMatrices { get; } = [];

        public List<PdfPattern> Patterns { get; } = [];

        public List<PdfColor> PatternColors { get; } = [];

        public List<RecordingProcessor> Cells { get; } = [];

        public override ContentEvents Events => ContentEvents.All & ~ContentEvents.Operators;

        public override void PaintShading(in ShadingEvent shading, ContentContext context)
        {
            Shadings.Add(shading.Model!);
            ShadingMatrices.Add(shading.Ctm);
        }

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            if (path.Paint is PathPaint.Fill or PathPaint.FillAndStroke)
            {
                Record(context.State.FillColor, context);
            }

            if (path.Paint is PathPaint.Stroke or PathPaint.FillAndStroke)
            {
                Record(context.State.StrokeColor, context);
            }
        }

        private void Record(in PdfColor color, ContentContext context)
        {
            if (context.GetPattern(color) is not { } pattern)
            {
                return;
            }

            Patterns.Add(pattern);
            PatternColors.Add(color);
            if (pattern is PdfTilingPattern)
            {
                var cell = new RecordingProcessor(ContentEvents.All & ~ContentEvents.Operators);
                context.RunPatternCell(color, cell);
                Cells.Add(cell);
            }
        }
    }
}
