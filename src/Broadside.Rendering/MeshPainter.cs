using System.Runtime.InteropServices;

namespace Broadside.Rendering;

/// <summary>
/// Paints a function-based or mesh shading (Types 1, 4 and 5) through the coverage of the painted shape: each covered pixel takes
/// the colour the shading's <see cref="MeshLayer"/> holds for it, or the Background when the layer is empty there and the paint has
/// one; otherwise it is left unpainted.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.2, §8.7.4.5.5, §8.7.4.5.6, and §8.7.4.3 Table 77 (Background: shading patterns only, outside the shading's
/// own points). It is a coverage sink of <see cref="ScanlineRasterizer.Paint"/>, so the runs it receives are already cut by the
/// clipping path (§8.5.4), and it composites through the paint's <see cref="Compositor"/> (constant alpha and blend mode, §11.3).
/// Hot path: allocates nothing; a table read and a composite per pixel.
/// </remarks>
internal readonly ref struct MeshPainter : ICoverageSink
{
    private readonly Span<byte> _target;
    private readonly int _stride;
    private readonly ReadOnlySpan<uint> _layer;
    private readonly int _left;
    private readonly int _top;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _hasBackground;
    private readonly uint _background;
    private readonly Compositor _compositor;

    /// <summary>Creates the painter.</summary>
    /// <param name="target">The device pixels painted into, premultiplied BGRA in memory order.</param>
    /// <param name="stride">The bytes from one row of <paramref name="target"/> to the next.</param>
    /// <param name="layer">The shading's colours.</param>
    /// <param name="hasBackground">Whether pixels the layer leaves empty take <paramref name="background"/>.</param>
    /// <param name="background">The Background colour, opaque premultiplied BGRA.</param>
    /// <param name="compositor">The alpha and blend mode.</param>
    public MeshPainter(Span<byte> target, int stride, MeshLayer layer, bool hasBackground, uint background, Compositor compositor)
    {
        _target = target;
        _stride = stride;
        _layer = layer.Pixels;
        _left = layer.Left;
        _top = layer.Top;
        _width = layer.Width;
        _height = layer.Height;
        _hasBackground = hasBackground;
        _background = background;
        _compositor = compositor;
    }

    /// <inheritdoc/>
    public void Span(int row, int x, int count, int coverage)
    {
        if (coverage <= 0)
        {
            return;
        }

        Span<uint> line = MemoryMarshal.Cast<byte, uint>(_target.Slice(row * _stride, (x + count) * 4));
        int layerRow = row - _top;
        bool inRows = layerRow >= 0 && layerRow < _height;
        for (int i = 0; i < count; i++)
        {
            int column = x + i - _left;
            uint color = inRows && column >= 0 && column < _width ? _layer[(layerRow * _width) + column] : 0;
            if (color == 0)
            {
                if (!_hasBackground)
                {
                    continue;
                }

                color = _background;
            }

            ref uint pixel = ref line[x + i];
            pixel = _compositor.Composite(color, pixel, coverage);
        }
    }
}
