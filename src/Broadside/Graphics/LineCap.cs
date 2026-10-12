namespace Broadside.Graphics;

/// <summary>The shape at the ends of open subpaths and dashes when they are stroked.</summary>
/// <remarks>ISO 32000-2 §8.4.3.3, Table 53.</remarks>
public enum LineCap
{
    /// <summary>Butt cap (0): the stroke is squared off at the endpoint.</summary>
    Butt = 0,

    /// <summary>Round cap (1): a semicircular arc with a diameter equal to the line width.</summary>
    Round = 1,

    /// <summary>Projecting square cap (2): the stroke continues half the line width beyond the endpoint.</summary>
    ProjectingSquare = 2,
}
