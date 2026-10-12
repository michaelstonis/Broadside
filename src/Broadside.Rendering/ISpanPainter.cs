namespace Broadside.Rendering;

/// <summary>
/// What <see cref="ScanlineRasterizer.Fill{TPainter}"/> composites into each run of pixels that share one coverage value: a solid
/// colour, image samples, and later shadings and patterns. Implemented by structs, so the call per run is direct.
/// </summary>
/// <remarks>ISO 32000-2 §10.7.4 (coverage) and §11.3 (premultiplied source-over compositing, with the coverage as shape).</remarks>
internal interface ISpanPainter
{
    /// <summary>Composites over <paramref name="count"/> pixels of <paramref name="line"/> from <paramref name="x"/>, at coverage 1 to 255.</summary>
    /// <param name="line">The bitmap row, premultiplied BGRA in memory order.</param>
    /// <param name="row">The row's device y.</param>
    /// <param name="x">The first pixel's device x.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="coverage">The share of each pixel the shape covers, 1 to 255.</param>
    void Paint(Span<uint> line, int row, int x, int count, int coverage);
}
