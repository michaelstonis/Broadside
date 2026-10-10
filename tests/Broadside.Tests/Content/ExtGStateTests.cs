using Broadside.Content;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Graphics state parameter dictionaries (ISO 32000-2 §8.4.5, Table 57) applied by <c>gs</c>, with the soft mask's CTM captured
/// at the <c>gs</c> (§11.6.5.1), asserted on the state each path is painted with.
/// </summary>
public class ExtGStateTests
{
    [Fact]
    public void Every_parameter_of_extgstate_params_is_applied()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("extgstate-params.pdf"));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(2, recorder.States.Count);
        GraphicsState first = recorder.States[0];
        Assert.Equal(3, first.LineWidth);
        Assert.Equal(LineCap.Round, first.LineCap);
        Assert.Equal(LineJoin.Bevel, first.LineJoin);
        Assert.Equal(5, first.MiterLimit);
        Assert.Equal([4.0, 2.0], recorder.DashArrays[0]);
        Assert.Equal(1, first.DashPhase);
        Assert.Equal(RenderingIntent.Saturation, first.RenderingIntent);
        Assert.True(first.StrokeOverprint);
        Assert.False(first.FillOverprint);
        Assert.Equal(1, first.OverprintMode);
        Assert.Equal("Helvetica", first.Font?.BaseFont);
        Assert.Equal(14, first.FontSize);
        Assert.Equal(2, first.Flatness);
        Assert.True(first.StrokeAdjustment);
        Assert.Equal(BlendMode.Multiply, first.BlendMode);
        Assert.Equal(0.5, first.StrokeAlpha);
        Assert.Equal(0.25, first.FillAlpha);
        Assert.True(first.AlphaIsShape);
        Assert.False(first.TextKnockout);
        Assert.Equal(BlackPointCompensation.On, first.BlackPointCompensation);
        PdfSoftMask mask = Assert.IsType<PdfSoftMask>(first.SoftMask);
        Assert.Equal(PdfSoftMaskType.Luminosity, mask.Subtype);
        Assert.True(mask.Group?.IsForm);
        Assert.Equal([0.5], mask.Backdrop);
        Assert.Null(mask.TransferFunction);
        Assert.Equal(new Matrix(2, 0, 0, 2, 0, 0), first.SoftMaskMatrix);

        GraphicsState second = recorder.States[1];
        Assert.Equal(3, second.LineWidth);
        Assert.Null(second.SoftMask);
        Assert.Equal(Matrix.Identity, second.SoftMaskMatrix);
        Assert.Equal(0.5, second.Smoothness);
        Assert.Equal(new PathPoint(1, 2), second.HalftoneOrigin);
        Assert.Equal(PdfFunctionType.Exponential, second.BlackGeneration?.FunctionType);
        Assert.Equal(PdfFunctionType.Exponential, second.UndercolorRemoval?.FunctionType);
        Assert.True(second.TransferFunction?.IsIdentity);
        Assert.Null(second.Halftone);
        Assert.Equal(BlendMode.Screen, second.BlendMode);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_state_gives_black_generation_and_undercolour_removal_to_colour_conversion()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("extgstate-params.pdf"));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder);

        ColorConversion conversion = ColorConversion.FromState(recorder.States[1], DeviceColorModel.Cmyk);
        Assert.Same(recorder.States[1].BlackGeneration, conversion.BlackGeneration);
        Assert.Same(recorder.States[1].UndercolorRemoval, conversion.UndercolorRemoval);
        Assert.Equal(BlackPointCompensation.On, conversion.BlackPointCompensation);
    }

    [Fact]
    public void A_missing_dictionary_and_out_of_range_entries_are_reported_and_repaired()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/ExtGState << /G 5 0 R >>",
            "/Nope gs /G gs 0 0 1 1 re f",
            "<< /LW -2 /CA 3 /BM /BroadsideUnknown /SMask 7 /LC 9 >>"));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder);

        GraphicsState state = Assert.Single(recorder.States);
        Assert.Equal(2, state.LineWidth);
        Assert.Equal(1, state.StrokeAlpha);
        Assert.Equal(BlendMode.Normal, state.BlendMode);
        Assert.Null(state.SoftMask);
        Assert.Equal(LineCap.Butt, state.LineCap);
        Assert.Equal(["ContentExtGStateMissing", "ContentExtGStateInvalid"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void Op_sets_both_overprints_when_op_is_absent_and_q_restores_gs_parameters()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/ExtGState << /G 5 0 R >>",
            "q /G gs 0 0 1 1 re f Q 0 0 1 1 re f",
            "<< /OP true /ca 0.5 >>"));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.True(recorder.States[0].StrokeOverprint);
        Assert.True(recorder.States[0].FillOverprint);
        Assert.Equal(0.5, recorder.States[0].FillAlpha);
        Assert.False(recorder.States[1].FillOverprint);
        Assert.Equal(1, recorder.States[1].FillAlpha);
    }

    [Fact]
    public void The_soft_mask_group_runs_in_the_coordinate_system_of_its_gs()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("extgstate-params.pdf"));
        var mask = new FormTests.FormRecorder();
        var runner = new MaskRunner(mask);

        document.Pages[0].ProcessContent(runner);

        Assert.Contains("Paint Fill ctm=[2 0 0 2 0 0] run=SoftMask depth=1 structParents=", mask.Lines);
    }

    private sealed class MaskRunner(ContentProcessor mask) : ContentProcessor
    {
        private bool _ran;

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            if (!_ran && context.State.SoftMask is not null)
            {
                _ran = true;
                context.RunSoftMaskGroup(mask);
            }
        }
    }
}
