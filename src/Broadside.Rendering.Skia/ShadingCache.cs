using System.Runtime.CompilerServices;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>
/// The Skia form of each shading a backend painted, built once per shading and conversion: a native gradient shader for axial and
/// radial shadings, vertices for triangle and patch meshes, a sampled texture for function-based shadings. Bounded by count and
/// bytes; past either everything is released and built again on use.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.2 to §8.7.4.5.8. Keys are weak, so a backend does not keep closed documents alive. Finding a built shading
/// allocates nothing. Not thread-safe: a backend renders one list at a time.
/// </remarks>
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

    /// <summary>A shading in Skia's terms, for one colour conversion.</summary>
    internal sealed class Entry
    {
        public required ColorConversion Conversion { get; init; }

        public required PdfDefaultColorSpaces Defaults { get; init; }

        /// <summary>Gets a value indicating whether the shading paints nothing (its colours convert to nothing, or its geometry is empty).</summary>
        public bool PaintsNothing { get; init; }

        /// <summary>Gets the Background colour (§8.7.4.3, Table 77), opaque, when the shading has a usable one.</summary>
        public SKColor? Background { get; init; }

        /// <summary>Gets the gradient of an axial or radial shading, in shading space.</summary>
        public SKShader? Gradient { get; init; }

        /// <summary>Gets the triangles of a mesh shading, in shading space.</summary>
        public SKVertices? Mesh { get; init; }

        /// <summary>Gets the colour table a mesh with a function reads through its texture coordinates.</summary>
        public SKShader? MeshTable { get; init; }

        /// <summary>Gets the sampled colours of a function-based shading over its domain.</summary>
        public SKImage? Texture { get; init; }

        /// <summary>Gets the domain rectangle the texture covers.</summary>
        public SKRect TextureArea { get; init; }

        /// <summary>Gets the function-based shading's Matrix: its domain to shading space.</summary>
        public SKMatrix TextureMatrix { get; init; }

        /// <summary>Gets the approximate bytes the entry holds natively.</summary>
        public long Bytes { get; init; }

        public void Release()
        {
            Gradient?.Dispose();
            Mesh?.Dispose();
            MeshTable?.Dispose();
            Texture?.Dispose();
        }
    }
}
