namespace Broadside.Rendering;

/// <summary>A rectangle of whole device pixels: the column and row of its top-left pixel, and its size.</summary>
/// <param name="X">The column of the leftmost pixel.</param>
/// <param name="Y">The row of the top pixel.</param>
/// <param name="Width">The number of pixels across.</param>
/// <param name="Height">The number of pixels down.</param>
/// <remarks>ISO 32000-2 §8.3.2.2: device space, where pixel boundaries fall on integer coordinates (§10.7.4); origin top-left, y down.</remarks>
public readonly record struct PixelRectangle(int X, int Y, int Width, int Height);
