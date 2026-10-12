namespace Broadside.Rendering;

/// <summary>A size in whole device pixels.</summary>
/// <param name="Width">The number of pixels across.</param>
/// <param name="Height">The number of pixels down.</param>
/// <remarks>ISO 32000-2 §8.3.2.2: device space, where pixel boundaries fall on integer coordinates (§10.7.4).</remarks>
public readonly record struct PixelSize(int Width, int Height);
