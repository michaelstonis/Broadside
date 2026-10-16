using System.Runtime.CompilerServices;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// The Direct2D forms of the shadings a backend met (<see cref="ShadingBuilder"/>), one per shading and colour conversion, keyed
/// weakly so that evicted display lists are not kept alive, and bounded in count and bytes. Finding a kept form allocates nothing.
/// </summary>
internal sealed class ShadingCache : IDisposable
{
    private const int MaxEntries = 256;
    private const long MaxBytes = 64L << 20;

    private readonly ConditionalWeakTable<PdfShading, Entry> _entries = [];
    private readonly List<Entry> _all = [];
    private long _bytes;

    /// <summary>Returns the entry built for <paramref name="shading"/> with this conversion, or null.</summary>
    public Entry? Find(PdfShading shading, ColorConversion conversion, PdfDefaultColorSpaces defaults) =>
        _entries.TryGetValue(shading, out Entry? entry) && entry.Conversion == conversion && ReferenceEquals(entry.Defaults, defaults) ? entry : null;

    /// <summary>Keeps a newly built entry, releasing everything first when the cache is full.</summary>
    public void Add(PdfShading shading, Entry entry)
    {
        if (_all.Count >= MaxEntries || _bytes + entry.Bytes > MaxBytes)
        {
            Dispose();
        }

        if (_entries.TryGetValue(shading, out Entry? old))
        {
            _entries.Remove(shading);
            _all.Remove(old);
            _bytes -= old.Bytes;
            old.Release();
        }

        _entries.Add(shading, entry);
        _all.Add(entry);
        _bytes += entry.Bytes;
    }

    /// <summary>Releases every entry.</summary>
    public void Dispose()
    {
        foreach (Entry entry in _all)
        {
            entry.Release();
        }

        _all.Clear();
        _entries.Clear();
        _bytes = 0;
    }

    /// <summary>A shading in Direct2D's terms, for one colour conversion.</summary>
    internal sealed unsafe class Entry
    {
        /// <summary>The most transforms a mesh (or a radial evaluated per pixel) is rasterized for; further transforms rasterize on each paint.</summary>
        public const int MaxRasters = 4;

        private readonly Raster[] _rasters = new Raster[MaxRasters];

        public required ColorConversion Conversion { get; init; }

        public required PdfDefaultColorSpaces Defaults { get; init; }

        /// <summary>Gets a value indicating whether the shading paints nothing (its colours convert to nothing, or its geometry is empty).</summary>
        public bool PaintsNothing { get; init; }

        /// <summary>Gets the Background colour (§8.7.4.3, Table 77) as 8-bit sRGB, when the shading has a usable one.</summary>
        public (byte R, byte G, byte B)? Background { get; init; }

        /// <summary>Gets the axial or radial gradient, in shading space.</summary>
        public GradientShape? Gradient { get; init; }

        /// <summary>Gets or sets the gradient brush made for <see cref="Gradient"/> on the device (released with the entry).</summary>
        public ID2D1Brush* Brush { get; set; }

        /// <summary>Gets the texture of a function-based shading over its domain (row 0 at the domain's smaller y).</summary>
        public PixelImage? Texture { get; init; }

        /// <summary>Gets the domain of <see cref="Texture"/> and the shading's Matrix, which maps it to shading space.</summary>
        public PdfRectangle TextureArea { get; init; }

        public Matrix TextureMatrix { get; init; }

        /// <summary>Gets the triangles of a mesh shading, rasterized by <see cref="MeshRasterizer"/>.</summary>
        public MeshTriangles? Mesh { get; init; }

        /// <summary>Gets the approximate bytes the entry holds.</summary>
        public long Bytes { get; init; }

        /// <summary>Returns the slot of <paramref name="transform"/>, taking a free one for a new transform; false when all are taken.</summary>
        public bool Admit(in Matrix transform, in PixelRect bounds, out int slot)
        {
            for (slot = 0; slot < _rasters.Length; slot++)
            {
                ref Raster raster = ref _rasters[slot];
                if (!raster.Used)
                {
                    raster = new Raster(true, transform, bounds, null);
                    return true;
                }

                if (raster.Transform == transform && raster.Bounds == bounds)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Gets or sets the mesh rasterized in shading space for transforms beyond its slots, and its pixels per unit.</summary>
        public PixelImage? MeshTexture { get; set; }

        public double MeshTextureScale { get; set; }

        public PixelImage? RasterAt(int slot) => _rasters[slot].Image;

        public void SetRaster(int slot, PixelImage image) => _rasters[slot] = _rasters[slot] with { Image = image };

        public void Release()
        {
            if (Brush is not null)
            {
                Brush->Release();
                Brush = null;
            }

            Texture?.Dispose();
            MeshTexture?.Dispose();
            for (int i = 0; i < _rasters.Length; i++)
            {
                _rasters[i].Image?.Dispose();
                _rasters[i] = default;
            }
        }

        private readonly record struct Raster(bool Used, Matrix Transform, PixelRect Bounds, PixelImage? Image);
    }
}

/// <summary>
/// An axial (ISO 32000-2 §8.7.4.5.3) or radial (§8.7.4.5.4) gradient in shading space: its geometry, Extend flags and
/// <see cref="ShadingBuilder.Stops"/> sRGB colours at evenly spaced s from the start (0) to the end (1).
/// </summary>
internal sealed class GradientShape
{
    public required byte[] Colors { get; init; }

    public bool Radial { get; init; }

    /// <summary>Gets the axis's start point, or the starting circle's centre.</summary>
    public PathPoint Start { get; init; }

    /// <summary>Gets the axis's end point, or the ending circle's centre.</summary>
    public PathPoint End { get; init; }

    public double StartRadius { get; init; }

    public double EndRadius { get; init; }

    public bool ExtendStart { get; init; }

    public bool ExtendEnd { get; init; }

    /// <summary>Gets a value indicating whether a Direct2D gradient brush draws the gradient; otherwise it is evaluated per pixel.</summary>
    public bool Native { get; init; }
}
