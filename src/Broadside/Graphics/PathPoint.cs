using System.Globalization;

namespace Broadside.Graphics;

/// <summary>A point of a path, in the coordinate space the path is defined in (user space for a content stream path).</summary>
/// <remarks>ISO 32000-2 §8.3.2, §8.5.2. Coordinates are doubles: real files combine large offsets with small scales.</remarks>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public readonly record struct PathPoint(double X, double Y)
{
    /// <summary>Returns the point as <c>(x, y)</c> in the invariant culture.</summary>
    /// <returns>The point text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}
