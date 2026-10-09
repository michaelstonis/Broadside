namespace Broadside.Fonts.CharStrings;

/// <summary>
/// Builds a glyph outline from the relative moves of a charstring: tracks the current point, opens a contour at the first segment
/// after a moveto, and closes the open contour before the next moveto and at the end. Shared by the Type 2 and Type 1 interpreters.
/// </summary>
/// <remarks>
/// <para>
/// Adobe Technical Note #5177 §4.1: "the first stack-clearing operator ... rmoveto ... Every character path and subpath must begin
/// with one of the moveto operators", "a closepath is implied by a moveto or endchar". A moveto alone draws nothing, so consecutive
/// movetos leave one contour. A segment with no moveto before it starts a contour at the current point and is recorded in
/// <see cref="MovetoMissing"/> for the interpreter to report.
/// </para>
/// <para>With no outline the builder only follows the current point (for measuring). <see cref="OffsetX"/>/<see cref="OffsetY"/>
/// translate every point written (the accent of an accented character, §C of 5177).</para>
/// </remarks>
internal struct CharStringPath
{
    private readonly GlyphOutline? _outline;
    private double _startX;
    private double _startY;
    private bool _moved;
    private bool _drawing;

    /// <summary>Initializes a new instance of the <see cref="CharStringPath"/> struct at the origin.</summary>
    /// <param name="outline">The outline to write to, or <see langword="null"/> to follow the current point only.</param>
    /// <param name="offsetX">The x translation of every point written.</param>
    /// <param name="offsetY">The y translation of every point written.</param>
    public CharStringPath(GlyphOutline? outline, double offsetX = 0, double offsetY = 0)
    {
        _outline = outline;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    /// <summary>Gets the outline written to, or <see langword="null"/> when measuring.</summary>
    public readonly GlyphOutline? Outline => _outline;

    /// <summary>Gets the x coordinate of the current point, in glyph space before the offset.</summary>
    public double X { get; private set; }

    /// <summary>Gets the y coordinate of the current point, in glyph space before the offset.</summary>
    public double Y { get; private set; }

    /// <summary>Gets the x translation applied to every point written.</summary>
    public double OffsetX { get; }

    /// <summary>Gets the y translation applied to every point written.</summary>
    public double OffsetY { get; }

    /// <summary>Gets a value indicating whether a segment was drawn with no moveto before it (the builder started a contour anyway).</summary>
    public bool MovetoMissing { get; private set; }

    /// <summary>Closes the open contour, if any, and moves the current point by a delta.</summary>
    public void MoveTo(double dx, double dy)
    {
        Close();
        X += dx;
        Y += dy;
        _startX = X;
        _startY = Y;
        _moved = true;
    }

    /// <summary>Draws a line to the current point moved by a delta.</summary>
    public void LineTo(double dx, double dy)
    {
        Begin();
        X += dx;
        Y += dy;
        _outline?.LineTo(X + OffsetX, Y + OffsetY);
    }

    /// <summary>Draws a cubic curve; each control point and the end point is relative to the previous point.</summary>
    public void CurveTo(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        Begin();
        double x1 = X + dx1;
        double y1 = Y + dy1;
        double x2 = x1 + dx2;
        double y2 = y1 + dy2;
        X = x2 + dx3;
        Y = y2 + dy3;
        _outline?.CubicTo(x1 + OffsetX, y1 + OffsetY, x2 + OffsetX, y2 + OffsetY, X + OffsetX, Y + OffsetY);
    }

    /// <summary>Closes the open contour, if any; the current point stays where it is.</summary>
    public void Close()
    {
        if (_drawing)
        {
            _outline?.Close();
            _drawing = false;
        }
    }

    private void Begin()
    {
        if (_drawing)
        {
            return;
        }

        if (!_moved)
        {
            MovetoMissing = true;
            _startX = X;
            _startY = Y;
        }

        _outline?.MoveTo(_startX + OffsetX, _startY + OffsetY);
        _drawing = true;
        _moved = false;
    }
}
