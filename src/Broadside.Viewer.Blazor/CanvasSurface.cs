using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Broadside.Rendering;
using Broadside.Rendering.WebGL;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The surface tiles are presented to: each tile becomes an offscreen canvas in the page, named by a small integer key, and frames
/// draw those canvases (<see cref="ViewerController"/> composes them). Pixels are converted from the rasterizer's premultiplied BGRA
/// to the straight RGBA of <c>ImageData</c> in one reused buffer.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class CanvasSurface(int viewerId) : IRenderSurface
{
    private readonly Dictionary<Tile, int> _keys = [];
    private byte[] _rgba = [];
    private int _next;

    /// <summary>Gets the number of tiles presented so far.</summary>
    public int Presented { get; private set; }

    /// <inheritdoc/>
    public void Present(Tile tile, Bitmap pixels)
    {
        if (!_keys.TryGetValue(tile, out int key))
        {
            key = ++_next;
            _keys.Add(tile, key);
        }

        int width = pixels.Width;
        int height = pixels.Height;
        if (WorkerPresentation.TryTake(pixels, out JSObject? image))
        {
            // Drawn by the render worker: its ImageBitmap becomes the tile as it is (#357).
            ViewerInterop.PutTileFromImage(viewerId, key, image!);
            Presented++;
            return;
        }

        if (GpuPresentation.TryClaim(pixels))
        {
            // Drawn by the WebGL backend and still in its canvas: copied GPU to GPU (#353).
            ViewerInterop.PutTileFromCanvas(viewerId, key, width, height, WebGLRenderer.Canvas);
            Presented++;
            return;
        }

        int length = width * height * 4;
        if (_rgba.Length < length)
        {
            _rgba = new byte[length];
        }

        Span<byte> target = _rgba.AsSpan(0, length);
        for (int y = 0; y < height; y++)
        {
            ToStraightRgba(pixels.GetRow(y)[..(width * 4)], target.Slice(y * width * 4, width * 4));
        }

        ViewerInterop.PutTile(viewerId, key, width, height, target);
        Presented++;
    }

    /// <inheritdoc/>
    public void Evict(Tile tile)
    {
        if (_keys.Remove(tile, out int key))
        {
            ViewerInterop.DropTile(viewerId, key);
        }
    }

    /// <summary>Finds the key of a presented tile.</summary>
    public bool TryGetKey(Tile tile, out int key) => _keys.TryGetValue(tile, out key);

    /// <summary>Forgets every tile (the document closed).</summary>
    public void Clear()
    {
        foreach (int key in _keys.Values)
        {
            ViewerInterop.DropTile(viewerId, key);
        }

        _keys.Clear();
    }

    internal static void ToStraightRgba(ReadOnlySpan<byte> source, Span<byte> target)
    {
        for (int i = 0; i < source.Length; i += 4)
        {
            uint bgra = BinaryPrimitives.ReadUInt32LittleEndian(source[i..]);
            uint alpha = bgra >> 24;
            if (alpha == 255)
            {
                // B G R A -> R G B A: swap the first and third bytes.
                BinaryPrimitives.WriteUInt32LittleEndian(target[i..], (bgra & 0xFF00FF00u) | ((bgra >> 16) & 0xFFu) | ((bgra & 0xFFu) << 16));
            }
            else if (alpha == 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target[i..], 0);
            }
            else
            {
                target[i] = Unpremultiply(source[i + 2], alpha);
                target[i + 1] = Unpremultiply(source[i + 1], alpha);
                target[i + 2] = Unpremultiply(source[i], alpha);
                target[i + 3] = (byte)alpha;
            }
        }
    }

    private static byte Unpremultiply(byte channel, uint alpha) => (byte)Math.Min(255u, ((channel * 255u) + (alpha / 2)) / alpha);
}
