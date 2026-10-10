using System.Buffers;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>The graphics state operators of Table 56: <c>q Q cm w J j M d ri i gs</c>.</summary>
/// <remarks>ISO 32000-2 §8.4.2 to §8.4.4. Out-of-range values are clipped into range with a diagnostic (§8.4.1).</remarks>
internal sealed partial class ContentInterpreter
{
    private static readonly CosName ExtGStateName = new("ExtGState");

    private void ExecuteGraphicsState(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        switch (code)
        {
            case ContentOperatorCode.SaveState:
                SaveState(offset);
                break;
            case ContentOperatorCode.RestoreState:
                RestoreState(offset);
                break;
            case ContentOperatorCode.ConcatenateMatrix:
                var matrix = new Matrix(operands[0].Number, operands[1].Number, operands[2].Number, operands[3].Number, operands[4].Number, operands[5].Number);
                State.Ctm = matrix * State.Ctm;
                break;
            case ContentOperatorCode.SetLineWidth:
                double width = operands[0].Number;
                if (width < 0)
                {
                    Report(ContentIssue.GraphicsStateRange, offset, "A negative line width; its absolute value is used, as viewers do.");
                    width = -width;
                }

                State.LineWidth = width;
                break;
            case ContentOperatorCode.SetLineCap:
                State.LineCap = (LineCap)Style(operands[0].Number, offset, "A line cap style that is not 0, 1 or 2; butt caps (0) are used.");
                break;
            case ContentOperatorCode.SetLineJoin:
                State.LineJoin = (LineJoin)Style(operands[0].Number, offset, "A line join style that is not 0, 1 or 2; miter joins (0) are used.");
                break;
            case ContentOperatorCode.SetMiterLimit:
                double limit = operands[0].Number;
                if (limit < 1 || double.IsNaN(limit))
                {
                    Report(ContentIssue.GraphicsStateRange, offset, "A miter limit below 1; 1 is used.");
                    limit = 1;
                }

                State.MiterLimit = limit;
                break;
            case ContentOperatorCode.SetDash:
                SetDash(operands[0].Items, operands[1].Number, offset);
                break;
            case ContentOperatorCode.SetRenderingIntent:
                State.RenderingIntent = Intent(operands[0].Bytes);
                break;
            case ContentOperatorCode.SetFlatness:
                double flatness = operands[0].Number;
                if (flatness is < 0 or > 100 || double.IsNaN(flatness))
                {
                    Report(ContentIssue.GraphicsStateRange, offset, "A flatness tolerance outside 0 to 100; clipped into range.");
                    flatness = double.IsNaN(flatness) ? 0 : Math.Clamp(flatness, 0, 100);
                }

                State.Flatness = flatness;
                break;
            case ContentOperatorCode.SetGraphicsStateParameters:
                SetGraphicsStateParameters(operands[0].Bytes, offset);
                break;
        }
    }

    /// <summary>
    /// <c>gs</c>: applies the parameters of the dictionary named in the current resources' <c>ExtGState</c> subdictionary (§8.4.5,
    /// Table 57), in the fixed order of the table. A soft mask records the CTM of this moment (§11.6.5.1).
    /// </summary>
    private void SetGraphicsStateParameters(ReadOnlySpan<byte> name, int offset)
    {
        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, ExtGStateName, name, out _);
        if (value is null || document.Resolve(value) is not CosDictionary dictionary)
        {
            Report(ContentIssue.ExtGStateMissing, offset, "gs names a graphics state parameter dictionary that is not in the resources' ExtGState dictionary; ignored.");
            return;
        }

        ExtGStateParameters parameters = document.ContentResources.GetGraphicsState(dictionary, value as CosReference);
        if (parameters.Problem is { } problem)
        {
            Report(ContentIssue.ExtGStateInvalid, offset, "A graphics state parameter dictionary deviates from Table 57: " + problem);
        }

        if (parameters.DashArray is { } dash)
        {
            ApplyDash(dash, parameters.DashPhase, offset);
        }

        // §8.6.8, §8.7.3.3, §9.6.4: where colours shall not be specified (a d1 glyph, an uncoloured cell) the colour-related
        // parameters are ignored too.
        bool colors = !IgnoresColorOperators;
        if (!colors && parameters.SetsColorParameters)
        {
            Report(ContentIssue.ColorOperatorIgnored, offset, "A graphics state parameter dictionary sets colour-related parameters inside a d1 glyph or an uncoloured pattern; they are ignored.");
        }

        ref GraphicsState state = ref State;
        if (parameters.LineWidth is { } width)
        {
            state.LineWidth = width;
        }

        if (parameters.LineCap is { } cap)
        {
            state.LineCap = cap;
        }

        if (parameters.LineJoin is { } join)
        {
            state.LineJoin = join;
        }

        if (parameters.MiterLimit is { } miter)
        {
            state.MiterLimit = miter;
        }

        if (colors && parameters.RenderingIntent is { } intent)
        {
            state.RenderingIntent = intent;
        }

        if (colors && parameters.StrokeOverprint is { } strokeOverprint)
        {
            state.StrokeOverprint = strokeOverprint;
        }

        if (colors && parameters.FillOverprint is { } fillOverprint)
        {
            state.FillOverprint = fillOverprint;
        }

        if (colors && parameters.OverprintMode is { } overprintMode)
        {
            state.OverprintMode = overprintMode;
        }

        if (parameters.HasFont)
        {
            state.Font = parameters.Font;
            state.FontSize = parameters.FontSize;
        }

        if (colors && parameters.HasBlackGeneration)
        {
            state.BlackGeneration = parameters.BlackGeneration;
        }

        if (colors && parameters.HasUndercolorRemoval)
        {
            state.UndercolorRemoval = parameters.UndercolorRemoval;
        }

        if (colors && parameters.HasTransfer)
        {
            state.TransferFunction = parameters.Transfer;
        }

        if (colors && parameters.HasHalftone)
        {
            state.Halftone = parameters.Halftone;
        }

        if (parameters.Flatness is { } flatness)
        {
            state.Flatness = flatness;
        }

        if (parameters.Smoothness is { } smoothness)
        {
            state.Smoothness = smoothness;
        }

        if (parameters.StrokeAdjustment is { } adjustment)
        {
            state.StrokeAdjustment = adjustment;
        }

        if (parameters.BlendMode is { } blendMode)
        {
            state.BlendMode = blendMode;
        }

        if (parameters.HasSoftMask)
        {
            state.SoftMask = parameters.SoftMask;
            state.SoftMaskMatrix = parameters.SoftMask is null ? Matrix.Identity : state.Ctm;
        }

        if (parameters.StrokeAlpha is { } strokeAlpha)
        {
            state.StrokeAlpha = strokeAlpha;
        }

        if (parameters.FillAlpha is { } fillAlpha)
        {
            state.FillAlpha = fillAlpha;
        }

        if (parameters.AlphaIsShape is { } alphaIsShape)
        {
            state.AlphaIsShape = alphaIsShape;
        }

        if (parameters.TextKnockout is { } knockout)
        {
            state.TextKnockout = knockout;
        }

        if (colors && parameters.BlackPointCompensation is { } compensation)
        {
            state.BlackPointCompensation = compensation;
        }

        if (colors && parameters.HalftoneOrigin is { } origin)
        {
            state.HalftoneOrigin = origin;
        }
    }

    /// <summary><c>q</c>: pushes a copy of the whole state (§8.4.2). Beyond the depth limit it is ignored, and so is its <c>Q</c>.</summary>
    private void SaveState(int offset)
    {
        if (_depth >= _maxSaveDepth)
        {
            Report(ContentIssue.StackOverflow, offset, "The graphics state stack is deeper than the limit; this q and its matching Q are ignored.");
            _ignoredSaves++;
            return;
        }

        if (_depth + 1 == _states.Length)
        {
            GraphicsState[] larger = ArrayPool<GraphicsState>.Shared.Rent(_states.Length * 2);
            _states.AsSpan(0, _depth + 1).CopyTo(larger);
            ArrayPool<GraphicsState>.Shared.Return(_states, clearArray: true);
            _states = larger;
        }

        _states[_depth + 1] = _states[_depth];
        _depth++;
        if ((_events & ContentEvents.StateStack) != 0)
        {
            _processor.SaveState(_context);
        }
    }

    /// <summary><c>Q</c>: pops the state (§8.4.2); never below the running stream's floor.</summary>
    private void RestoreState(int offset)
    {
        if (_ignoredSaves > 0)
        {
            _ignoredSaves--;
            return;
        }

        if (_depth <= _floor)
        {
            Report(ContentIssue.StackUnderflow, offset, "Q without a matching q; ignored.");
            return;
        }

        PopState();
    }

    private void PopState()
    {
        _depth--;
        if ((_events & ContentEvents.StateStack) != 0)
        {
            _processor.RestoreState(_context);
        }
    }

    /// <summary>
    /// <c>d</c> (§8.4.3.6): elements shall be non-negative and not all zero, else the line is solid; a negative phase is increased by
    /// twice the sum of the elements until it is not negative.
    /// </summary>
    private void SetDash(ContentOperands elements, double phase, int offset)
    {
        int count = elements.Count;
        double[]? rented = null;
        Span<double> values = count <= 32 ? stackalloc double[32] : (rented = ArrayPool<double>.Shared.Rent(count));
        values = values[..count];
        int index = 0;
        foreach (ContentOperand element in elements)
        {
            if (!element.IsNumber)
            {
                Report(ContentIssue.OperandType, offset, "A dash array element is not a number; the dash pattern is not changed.");
                Return(rented);
                return;
            }

            values[index++] = element.Number;
        }

        ApplyDash(values, phase, offset);
        Return(rented);

        static void Return(double[]? array)
        {
            if (array is not null)
            {
                ArrayPool<double>.Shared.Return(array);
            }
        }
    }

    /// <summary>Sets a dash pattern from <c>d</c> or the <c>D</c> entry of <c>gs</c>, normalized as §8.4.3.6 says.</summary>
    private void ApplyDash(ReadOnlySpan<double> values, double phase, int offset)
    {
        int count = values.Length;
        double sum = 0;
        bool valid = true;
        foreach (double value in values)
        {
            sum += value;
            valid &= value >= 0;
        }

        if (!valid || (count > 0 && sum <= 0) || !double.IsFinite(sum) || !double.IsFinite(phase))
        {
            Report(ContentIssue.GraphicsStateRange, offset, "A dash array with a negative element, or with only zeros; a solid line is used.");
            values = [];
            phase = 0;
        }
        else if (count == 0)
        {
            // §8.4.3.6: with an empty array the phase is zero.
            phase = 0;
        }
        else if (phase < 0)
        {
            double period = 2 * sum;
            phase += period * Math.Ceiling(-phase / period);
        }

        State.SetDash(values, phase);
    }

    /// <summary>A line cap or join style: an integer 0, 1 or 2 (Tables 53, 54); anything else is 0 with a diagnostic.</summary>
    private int Style(double value, int offset, string message)
    {
        if (value is 0 or 1 or 2)
        {
            return (int)value;
        }

        Report(ContentIssue.GraphicsStateRange, offset, message);
        return 0;
    }

    /// <summary>A rendering intent name (Table 69); a name not recognised selects RelativeColorimetric (§8.6.5.8).</summary>
    internal static RenderingIntent Intent(ReadOnlySpan<byte> name) =>
        name.SequenceEqual("AbsoluteColorimetric"u8) ? RenderingIntent.AbsoluteColorimetric
        : name.SequenceEqual("Saturation"u8) ? RenderingIntent.Saturation
        : name.SequenceEqual("Perceptual"u8) ? RenderingIntent.Perceptual
        : RenderingIntent.RelativeColorimetric;
}
