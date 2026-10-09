namespace Broadside.Graphics;

/// <summary>The shape at the corners of stroked paths.</summary>
/// <remarks>ISO 32000-2 §8.4.3.4, Table 54.</remarks>
public enum LineJoin
{
    /// <summary>Miter join (0), converted to a bevel when the miter limit is exceeded (§8.4.3.5).</summary>
    Miter = 0,

    /// <summary>Round join (1).</summary>
    Round = 1,

    /// <summary>Bevel join (2).</summary>
    Bevel = 2,
}
