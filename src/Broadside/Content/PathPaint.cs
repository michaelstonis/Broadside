namespace Broadside.Content;

/// <summary>How a path-painting operator paints the current path.</summary>
/// <remarks>ISO 32000-2 §8.5.3, Table 59.</remarks>
public enum PathPaint
{
    /// <summary><c>n</c>: nothing is painted; the path object ends, usually to set a clip.</summary>
    None,

    /// <summary><c>S</c>, <c>s</c>: stroke.</summary>
    Stroke,

    /// <summary><c>f</c>, <c>F</c>, <c>f*</c>: fill.</summary>
    Fill,

    /// <summary><c>B</c>, <c>B*</c>, <c>b</c>, <c>b*</c>: fill, then stroke, as two path objects painted in turn.</summary>
    FillAndStroke,
}
