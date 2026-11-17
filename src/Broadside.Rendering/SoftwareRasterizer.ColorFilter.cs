using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The color filter of a render (spec #33 viewer capability 9): run on the finished pixels, outside the images the render painted
/// (see <see cref="ColorFilter.DarkMode"/>).
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's color-filter half.</summary>
    private sealed partial class Backend
    {
        private ColorFilter _colorFilter;

        // How much of each pixel of the render the images cover, 0 to 255, row by row across the render's width; valid while
        // _imagesCovered is set (the first image of the render clears it).
        private byte[] _imageCover = [];
        private bool _imagesCovered;
        private int _filterWidth;
        private int _filterHeight;

        /// <summary>Starts the render's filter over a <paramref name="width"/> × <paramref name="height"/> area of the target.</summary>
        private void BeginColorFilter(ColorFilter filter, int width, int height)
        {
            _colorFilter = filter;
            _imagesCovered = false;
            _filterWidth = width;
            _filterHeight = height;
        }

        /// <summary>
        /// Adds the shape just painted by an image (still in the scan converter, cut by the clip) to the area images cover, in the
        /// render's own pixels: the image's outline, not its opacity, so what shows through its transparent parts is kept too.
        /// </summary>
        private void CoverImage()
        {
            int length = _filterWidth * _filterHeight;
            if (!_imagesCovered)
            {
                Pooled.Ensure(ref _imageCover, length);
                _imageCover.AsSpan(0, length).Clear();
                _imagesCovered = true;
            }

            // The knockout and group hooks saw the image's paint already; this second pass only records its coverage.
            GroupLayer? hook = _scan.Hook;
            _scan.Hook = null;
            try
            {
                var sink = new ImageCoverSink(_imageCover.AsSpan(0, length), _filterWidth, _filterHeight, _originX, _originY);
                _scan.Paint(FillRule.NonZero, ref sink);
            }
            finally
            {
                _scan.Hook = hook;
            }
        }

        /// <summary>Runs the filter over the finished render, in place: every pixel outside the images, and the images' edges in part.</summary>
        public void FinishColorFilter()
        {
            if (_colorFilter == ColorFilter.None || _target is not { } target)
            {
                return;
            }

            int width = Math.Min(_filterWidth, target.Width);
            int height = Math.Min(_filterHeight, target.Height);
            for (int y = 0; y < height; y++)
            {
                Span<uint> row = MemoryMarshal.Cast<byte, uint>(target.Pixels.Slice(y * target.Stride, width * 4));
                if (_imagesCovered)
                {
                    InvertLightness(row, _imageCover.AsSpan(y * _filterWidth, width));
                }
                else
                {
                    InvertLightness(row);
                }
            }
        }

        /// <summary>Lets go of the area images covered; the buffer stays for the next render.</summary>
        private void EndColorFilter()
        {
            _colorFilter = ColorFilter.None;
            _imagesCovered = false;
        }

        private void DisposeColorFilter() => Pooled.Return(ref _imageCover);
    }

    /// <summary>
    /// Inverts the lightness of premultiplied BGRA pixels and keeps their hue: c' = c + α − max − min for each color channel
    /// (<see cref="ColorFilter.DarkMode"/>).
    /// </summary>
    internal static void InvertLightness(Span<uint> pixels)
    {
        for (int x = 0; x < pixels.Length; x++)
        {
            uint pixel = pixels[x];
            int a = (int)(pixel >> 24);
            int r = (int)(pixel >> 16) & 0xFF;
            int g = (int)(pixel >> 8) & 0xFF;
            int b = (int)pixel & 0xFF;
            int k = a - Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            pixels[x] = Shift(pixel, r, g, b, k);
        }
    }

    /// <summary>
    /// Inverts the lightness of premultiplied BGRA pixels outside the images: a pixel an image covers by <c>m</c> of 255 moves only
    /// (255 − m) / 255 of the way, so one it covers whole keeps its color.
    /// </summary>
    internal static void InvertLightness(Span<uint> pixels, ReadOnlySpan<byte> imageCover)
    {
        for (int x = 0; x < pixels.Length; x++)
        {
            int m = imageCover[x];
            if (m == 255)
            {
                continue;
            }

            uint pixel = pixels[x];
            int a = (int)(pixel >> 24);
            int r = (int)(pixel >> 16) & 0xFF;
            int g = (int)(pixel >> 8) & 0xFF;
            int b = (int)pixel & 0xFF;
            int k = a - Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (m != 0)
            {
                // Rounded half away from zero, so |k| never grows and every channel stays between its value and the inverted one.
                int scaled = k * (255 - m);
                k = (scaled + (scaled >= 0 ? 127 : -127)) / 255;
            }

            pixels[x] = Shift(pixel, r, g, b, k);
        }
    }

    /// <summary>
    /// Adds <paramref name="k"/> to each color channel. For a premultiplied pixel every result lies between 0 and its alpha already;
    /// the clamp only keeps a bitmap that was not premultiplied from spilling into the next channel.
    /// </summary>
    private static uint Shift(uint pixel, int r, int g, int b, int k) =>
        (pixel & 0xFF000000u) | ((uint)Math.Clamp(r + k, 0, 255) << 16) | ((uint)Math.Clamp(g + k, 0, 255) << 8) | (uint)Math.Clamp(b + k, 0, 255);

    /// <summary>Records the coverage of an image's shape into the area images cover: m' = m + c × (255 − m) / 255.</summary>
    private readonly ref struct ImageCoverSink(Span<byte> cover, int width, int height, int originX, int originY) : ICoverageSink
    {
        private readonly Span<byte> _cover = cover;

        public void Span(int row, int x, int count, int coverage)
        {
            int y = row + originY;
            int left = Math.Max(0, x + originX);
            int right = Math.Min(width, x + originX + count);
            if ((uint)y >= (uint)height || right <= left)
            {
                return;
            }

            Span<byte> line = _cover.Slice((y * width) + left, right - left);
            if (coverage >= 255)
            {
                line.Fill(255);
                return;
            }

            for (int i = 0; i < line.Length; i++)
            {
                int m = line[i];
                line[i] = (byte)(m + ((((255 - m) * coverage) + 127) / 255));
            }
        }
    }
}
