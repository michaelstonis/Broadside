using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Path strokes (ISO 32000-2 §8.5.3.2) with the line parameters of the graphics state (§8.4.3, §10.7.5).</summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayStroke[] _strokes = new DisplayStroke[16];
    private int _strokeCount;
    private double[] _dashes = new double[16];
    private int _dashCount;

    /// <summary>
    /// Records the stroke of a path with the stroking colour and the line parameters current at the operator. Consecutive strokes
    /// with the same colour and parameters share one record.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.5.3.2. A stroke in a Pattern colour (§8.7) is not in the list yet.</remarks>
    private void StrokePath(in PathEvent path, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        ref readonly PdfColor stroke = ref state.StrokeColor;
        if (stroke.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            return;
        }

        int pathIndex = InternPath(path.Path);
        int colorIndex = AddColor(stroke, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        int strokeIndex = AddStroke(colorIndex, state);
        SetTransform(state.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.StrokePath, default, pathIndex, strokeIndex));
    }

    /// <summary>Returns the index of the stroke record for the colour and the state's line parameters, reusing the last when equal.</summary>
    private int AddStroke(int colorIndex, in GraphicsState state)
    {
        ReadOnlySpan<double> dash = state.DashArray;
        if (_strokeCount > 0)
        {
            DisplayStroke last = _strokes[_strokeCount - 1];
            if (last.ColorIndex == colorIndex
                && last.LineWidth == state.LineWidth
                && last.LineCap == state.LineCap
                && last.LineJoin == state.LineJoin
                && last.MiterLimit == state.MiterLimit
                && last.DashPhase == state.DashPhase
                && last.StrokeAdjustment == state.StrokeAdjustment
                && _dashes.AsSpan(last.DashStart, last.DashCount).SequenceEqual(dash))
            {
                return _strokeCount - 1;
            }
        }

        EnsureCapacity(ref _dashes, _dashCount + dash.Length);
        dash.CopyTo(_dashes.AsSpan(_dashCount));
        Append(ref _strokes, ref _strokeCount, new DisplayStroke(
            colorIndex,
            state.LineWidth,
            state.LineCap,
            state.LineJoin,
            state.MiterLimit,
            _dashCount,
            dash.Length,
            state.DashPhase,
            state.StrokeAdjustment));
        _dashCount += dash.Length;
        return _strokeCount - 1;
    }

    /// <summary>Forgets the strokes, keeping the tables' capacity.</summary>
    private void ResetStrokes()
    {
        _strokeCount = 0;
        _dashCount = 0;
    }
}
