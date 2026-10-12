namespace Broadside.Annotations;

/// <summary>A line ending style, as the <c>LE</c> entry of line, polyline and free text annotations names it.</summary>
/// <remarks>ISO 32000-2 §12.5.6.7, Table 179. A name the table does not list reads as <see cref="None"/>, as viewers draw it.</remarks>
public enum PdfLineEnding
{
    /// <summary><c>None</c>: no line ending (the default).</summary>
    None,

    /// <summary><c>Square</c>: a square filled with the interior colour.</summary>
    Square,

    /// <summary><c>Circle</c>: a circle filled with the interior colour.</summary>
    Circle,

    /// <summary><c>Diamond</c>: a diamond filled with the interior colour.</summary>
    Diamond,

    /// <summary><c>OpenArrow</c>: two short lines meeting in an acute angle to form an open arrowhead.</summary>
    OpenArrow,

    /// <summary><c>ClosedArrow</c>: two short lines meeting in an acute angle, closed and filled with the interior colour.</summary>
    ClosedArrow,

    /// <summary><c>Butt</c> (PDF 1.5): a short line at the endpoint perpendicular to the line.</summary>
    Butt,

    /// <summary><c>ROpenArrow</c> (PDF 1.5): an open arrowhead pointing the other way.</summary>
    ROpenArrow,

    /// <summary><c>RClosedArrow</c> (PDF 1.5): a closed arrowhead pointing the other way.</summary>
    RClosedArrow,

    /// <summary><c>Slash</c> (PDF 1.6): a short line at the endpoint, 30 degrees clockwise from perpendicular to the line.</summary>
    Slash,
}
