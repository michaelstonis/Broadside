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

    // Each distinct stroke record once per list (issue #337): a page that cycles through a few line styles (a table's rules, a
    // chart's series) shared a record only between consecutive strokes, so its list held one record and one dash slice per
    // change. Kept with the builder, so a warm build adds nothing to the list's own allocation.
    private Dictionary<DisplayStroke, int>? _strokeIndexes;

    /// <summary>
    /// Records the stroke of a path with the stroking colour and the line parameters current at the operator. Strokes with the same
    /// colour and parameters share one record.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.5.3.2. A stroke in a shading pattern (§8.7.4.1) strokes with its shading; one in a tiling pattern (§8.7.3) with its cell.</remarks>
    private void StrokePath(in PathEvent path, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        ref readonly PdfColor stroke = ref state.StrokeColor;
        if (stroke.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            if (context.GetPattern(stroke) is PdfTilingPattern)
            {
                StrokePathWithTilingPattern(path, context);
                return;
            }

            // §8.7.4.1: a shading pattern; the stroke record's colour index is the shading paint's.
            int shading = AddPatternShading(stroke, context);
            if (shading >= 0)
            {
                int shadedPath = InternPath(path.Path);
                int shadedStroke = AddStroke(shading, state);
                SetPatternCompositing(stroke, context, stroke: true);
                SetTransform(state.Ctm);
                AddOperation(new DisplayOperation(DisplayOperationKind.StrokePathWithShading, default, shadedPath, shadedStroke));
            }

            return;
        }

        int pathIndex = InternPath(path.Path);
        int colorIndex = AddColor(stroke, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        int strokeIndex = AddStroke(colorIndex, state);
        SetCompositing(state, context);
        SetTransform(state.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.StrokePath, default, pathIndex, strokeIndex));
    }

    /// <summary>Returns the index of the stroke record for the colour and the state's line parameters, reusing an equal one.</summary>
    private int AddStroke(int colorIndex, in GraphicsState state)
    {
        // The candidate's dash array goes after the recorded ones first, so that the comparer reads it like a recorded record's;
        // it stays there only when the record is new.
        ReadOnlySpan<double> dash = state.DashArray;
        EnsureCapacity(ref _dashes, _dashCount + dash.Length);
        dash.CopyTo(_dashes.AsSpan(_dashCount));
        var stroke = new DisplayStroke(
            colorIndex,
            state.LineWidth,
            state.LineCap,
            state.LineJoin,
            state.MiterLimit,
            _dashCount,
            dash.Length,
            state.DashPhase,
            state.StrokeAdjustment);
        _strokeIndexes ??= new Dictionary<DisplayStroke, int>(new StrokeComparer(this));
        if (_strokeIndexes.TryGetValue(stroke, out int index))
        {
            return index;
        }

        Append(ref _strokes, ref _strokeCount, stroke);
        _dashCount += dash.Length;
        _strokeIndexes.Add(stroke, _strokeCount - 1);
        return _strokeCount - 1;
    }

    /// <summary>Forgets the strokes, keeping the tables' capacity.</summary>
    private void ResetStrokes()
    {
        _strokeCount = 0;
        _dashCount = 0;
        _strokeIndexes?.Clear();
    }

    /// <summary>Compares stroke records by value: their fields, and their dash arrays element by element wherever they are stored.</summary>
    private sealed class StrokeComparer(DisplayListBuilder builder) : IEqualityComparer<DisplayStroke>
    {
        public bool Equals(DisplayStroke x, DisplayStroke y) =>
            x.ColorIndex == y.ColorIndex
            && x.LineWidth == y.LineWidth
            && x.LineCap == y.LineCap
            && x.LineJoin == y.LineJoin
            && x.MiterLimit == y.MiterLimit
            && x.DashPhase == y.DashPhase
            && x.StrokeAdjustment == y.StrokeAdjustment
            && builder._dashes.AsSpan(x.DashStart, x.DashCount).SequenceEqual(builder._dashes.AsSpan(y.DashStart, y.DashCount));

        public int GetHashCode(DisplayStroke obj)
        {
            var hash = default(HashCode);
            hash.Add(obj.ColorIndex);
            hash.Add(obj.LineWidth);
            hash.Add(obj.LineCap);
            hash.Add(obj.LineJoin);
            hash.Add(obj.MiterLimit);
            hash.Add(obj.DashPhase);
            hash.Add(obj.StrokeAdjustment);
            foreach (double value in builder._dashes.AsSpan(obj.DashStart, obj.DashCount))
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
