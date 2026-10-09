using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>Path construction (Table 58), painting (Table 59) and clipping (Table 60).</summary>
/// <remarks>
/// ISO 32000-2 §8.5. A clip set by <c>W</c> or <c>W*</c> takes effect after the painting operator that ends the path object, so
/// the paint event comes first and is not clipped by its own path (§8.5.4); <c>W n</c> only clips.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private void ExecutePathConstruction(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        switch (code)
        {
            case ContentOperatorCode.MoveTo:
                _path.MoveTo(operands[0].Number, operands[1].Number);
                break;
            case ContentOperatorCode.Rectangle:
                _path.Rectangle(operands[0].Number, operands[1].Number, operands[2].Number, operands[3].Number);
                break;
            case ContentOperatorCode.ClosePath:
                if (!_path.IsOpen)
                {
                    Report(ContentIssue.NoCurrentPoint, offset, "h without a current point; ignored.");
                }
                else
                {
                    _path.Close();
                }

                break;
            case ContentOperatorCode.LineTo:
                if (StartsWithoutCurrentPoint(operands[0].Number, operands[1].Number, offset))
                {
                    break;
                }

                _path.LineTo(operands[0].Number, operands[1].Number);
                break;
            case ContentOperatorCode.CurveTo:
                if (StartsWithoutCurrentPoint(operands[4].Number, operands[5].Number, offset))
                {
                    break;
                }

                _path.CurveTo(
                    new PathPoint(operands[0].Number, operands[1].Number),
                    new PathPoint(operands[2].Number, operands[3].Number),
                    new PathPoint(operands[4].Number, operands[5].Number));
                break;
            case ContentOperatorCode.CurveToInitialPointReplicated:
                if (StartsWithoutCurrentPoint(operands[2].Number, operands[3].Number, offset))
                {
                    break;
                }

                _path.CurveTo(_path.CurrentPoint, new PathPoint(operands[0].Number, operands[1].Number), new PathPoint(operands[2].Number, operands[3].Number));
                break;
            case ContentOperatorCode.CurveToFinalPointReplicated:
                if (StartsWithoutCurrentPoint(operands[2].Number, operands[3].Number, offset))
                {
                    break;
                }

                var end = new PathPoint(operands[2].Number, operands[3].Number);
                _path.CurveTo(new PathPoint(operands[0].Number, operands[1].Number), end, end);
                break;
        }
    }

    /// <summary>
    /// A segment operator with no current point (§8.5.2.1: an error) begins a subpath at its end point instead, as PDFBox does;
    /// returns true when it did.
    /// </summary>
    private bool StartsWithoutCurrentPoint(double x, double y, int offset)
    {
        if (_path.IsOpen)
        {
            return false;
        }

        Report(ContentIssue.NoCurrentPoint, offset, "A line or curve without a current point; read as a moveto to its end point.");
        _path.MoveTo(x, y);
        return true;
    }

    private void ExecuteClip(ContentOperatorCode code, int offset)
    {
        if (!_path.IsOpen)
        {
            Report(ContentIssue.NoCurrentPath, offset, "W or W* without a current path; ignored.");
            return;
        }

        _pendingClip = code == ContentOperatorCode.Clip ? FillRule.NonZero : FillRule.EvenOdd;
    }

    private void ExecutePathPainting(ContentOperatorCode code, int offset)
    {
        (PathPaint paint, FillRule rule, bool close) = code switch
        {
            ContentOperatorCode.Stroke => (PathPaint.Stroke, FillRule.NonZero, false),
            ContentOperatorCode.CloseAndStroke => (PathPaint.Stroke, FillRule.NonZero, true),
            ContentOperatorCode.Fill or ContentOperatorCode.FillObsolete => (PathPaint.Fill, FillRule.NonZero, false),
            ContentOperatorCode.FillEvenOdd => (PathPaint.Fill, FillRule.EvenOdd, false),
            ContentOperatorCode.FillAndStroke => (PathPaint.FillAndStroke, FillRule.NonZero, false),
            ContentOperatorCode.FillEvenOddAndStroke => (PathPaint.FillAndStroke, FillRule.EvenOdd, false),
            ContentOperatorCode.CloseFillAndStroke => (PathPaint.FillAndStroke, FillRule.NonZero, true),
            ContentOperatorCode.CloseFillEvenOddAndStroke => (PathPaint.FillAndStroke, FillRule.EvenOdd, true),
            _ => (PathPaint.None, FillRule.NonZero, false),
        };

        if (!_path.IsOpen)
        {
            Report(ContentIssue.NoCurrentPath, offset, "A path-painting operator without a current path; nothing is painted.");
            _pendingClip = null;
            return;
        }

        if (close)
        {
            _path.Close();
        }

        if ((_events & ContentEvents.Paths) != 0 && (!_context.IsHidden || (_events & ContentEvents.HiddenContent) != 0))
        {
            var paintEvent = new PathEvent
            {
                Path = _path.View,
                Paint = paint,
                FillRule = rule,
                ClosedBeforePaint = close,
                PendingClip = _pendingClip,
                IsHidden = _context.IsHidden,
            };
            _processor.PaintPath(paintEvent, _context);
        }

        if (_pendingClip is { } clipRule && (_events & ContentEvents.Clips) != 0)
        {
            int parent = State.ClipHandle;
            int handle = Clips.Add(parent, ClipKind.Path, clipRule, _path.View, State.Ctm);
            State.ClipHandle = handle;
            var clipEvent = new ClipEvent
            {
                Handle = handle,
                ParentHandle = parent,
                Kind = ClipKind.Path,
                Rule = clipRule,
                Path = _path.View,
                Ctm = State.Ctm,
            };
            _processor.IntersectClip(clipEvent, _context);
        }

        _pendingClip = null;
        _path.Reset();
    }
}
