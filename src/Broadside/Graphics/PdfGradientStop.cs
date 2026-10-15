namespace Broadside.Graphics;

/// <summary>A colour at a position along a gradient: 0 at its start, 1 at its end.</summary>
/// <param name="Offset">The position, from 0 (the gradient's start) to 1 (its end).</param>
/// <param name="Color">The colour at that position; every stop of one gradient is in the same colour space.</param>
/// <remarks>
/// ISO 32000-2 §7.10.3 and §7.10.4: a gradient's stops are written as an exponential function (Type 2) between two stops, or a
/// stitching function (Type 3) of one Type 2 function per pair of neighbouring stops, the input t of an axial or radial shading
/// (§8.7.4.5.3, §8.7.4.5.4). Stops at one offset make a hard edge there; the colour before the first stop is the first stop's,
/// after the last stop the last stop's.
/// </remarks>
public readonly record struct PdfGradientStop(double Offset, PdfColor Color);
