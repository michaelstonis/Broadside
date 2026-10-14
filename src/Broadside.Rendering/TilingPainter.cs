using System.Runtime.InteropServices;

namespace Broadside.Rendering;

/// <summary>
/// Paints a tiled area: each covered device pixel takes the pixel of the tile layer (the pattern's tiles laid over a clear surface)
/// and composites it over the backdrop by the coverage of the painted shape.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.7.3.1 (the tiles, trimmed to the painted area) and §11.6.7 (the tiled pattern composites with the backdrop as one
/// object, through the paint's alpha constant and blend mode). Hot path: allocates nothing; one read and, where the layer is not
/// clear, one <see cref="Compositor.Composite"/> per pixel. It is a coverage sink of <see cref="ScanlineRasterizer.Paint"/>, so the
/// runs it receives are already cut by the clipping path (§8.5.4).
/// </remarks>
internal readonly ref struct TilingPainter : ICoverageSink
{
    private readonly Span<byte> _target;
    private readonly int _stride;
    private readonly ReadOnlySpan<uint> _layer;
    private readonly int _left;
    private readonly int _top;
    private readonly int _width;
    private readonly int _height;
    private readonly Compositor _compositor;

    /// <summary>Creates the painter.</summary>
    /// <param name="target">The device pixels painted into, premultiplied BGRA in memory order.</param>
    /// <param name="stride">The bytes from one row of <paramref name="target"/> to the next.</param>
    /// <param name="layer">The tiles, premultiplied BGRA, <paramref name="width"/> pixels per row.</param>
    /// <param name="left">The device column of the layer's first pixel.</param>
    /// <param name="top">The device row of the layer's first row.</param>
    /// <param name="width">The layer's width in pixels.</param>
    /// <param name="height">The layer's height in pixels.</param>
    /// <param name="compositor">The alpha constant and blend mode of the paint (§11.3).</param>
    public TilingPainter(Span<byte> target, int stride, ReadOnlySpan<uint> layer, int left, int top, int width, int height, Compositor compositor)
    {
        _target = target;
        _stride = stride;
        _layer = layer;
        _left = left;
        _top = top;
        _width = width;
        _height = height;
        _compositor = compositor;
    }

    /// <inheritdoc/>
    public void Span(int row, int x, int count, int coverage)
    {
        int layerRow = row - _top;
        int start = Math.Max(x, _left);
        int end = Math.Min(x + count, _left + _width);
        if (coverage <= 0 || layerRow < 0 || layerRow >= _height || start >= end)
        {
            return;
        }

        Span<uint> line = MemoryMarshal.Cast<byte, uint>(_target.Slice(row * _stride, end * 4));
        ReadOnlySpan<uint> tiles = _layer.Slice(layerRow * _width, _width);
        for (int column = start; column < end; column++)
        {
            uint source = tiles[column - _left];
            if (source != 0)
            {
                line[column] = _compositor.Composite(source, line[column], coverage);
            }
        }
    }
}
