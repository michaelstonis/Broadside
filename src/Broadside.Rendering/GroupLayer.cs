using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// One open transparency group of the software rasterizer: the pixels its content paints into (one plane, or two for a CMYK group),
/// covering only the group's bounds, and what the group needs to composite: its initial backdrop, its own alpha and its shape. As the
/// scan converter's <see cref="ScanlineRasterizer.Hook"/> it sees every run of a paint and does what a knockout group and the
/// tracking of alpha or shape need around the paint's own compositing.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.4.8 in premultiplied form, per pixel and channel, with source shape fs (the run's coverage, which already holds the
/// clip, §8.5.4), source alpha αs (what the paint leaves over a transparent backdrop) and the group's initial backdrop B0:
/// </para>
/// <list type="bullet">
/// <item>Non-knockout: the paint composites over what the group holds (the plane), as on the page; the group alpha
/// αg = Union(αg, αs).</item>
/// <item>Knockout (b = 0): E is the paint composited over B0 (shape fs, so E = (1 − fs) B0 + Ct), and the plane becomes
/// (1 − fs) × plane + E − (1 − fs) × B0, which is (1 − fs) αi−1 Ci−1 + Ct; the group alpha αg = (1 − fs) αg + αs.</item>
/// <item>Shape: fg = Union(fg, fs).</item>
/// </list>
/// <para>
/// The plane always holds the complete result αi Ci (backdrop included for a non-isolated group); αg and fg are kept only when the
/// group's compositing needs them, and only in the first pass of a CMYK group (they are the same in every pass). Buffers are kept
/// and reused at their largest size, so a warm render allocates nothing.
/// </para>
/// </remarks>
internal sealed class GroupLayer : IDisposable
{
    private readonly Bitmap?[] _planes = new Bitmap?[2];
    private readonly Bitmap?[] _backdrops = new Bitmap?[2];
    private byte[] _alpha = [];
    private byte[] _shape = [];
    private uint[] _saved = [];
    private uint[] _element = [];

    /// <summary>Gets the left edge of the group's pixels in the render's pixels.</summary>
    public int Left { get; private set; }

    /// <summary>Gets the top edge of the group's pixels in the render's pixels.</summary>
    public int Top { get; private set; }

    /// <summary>Gets the width of the group's pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the group's pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Gets a value indicating whether the group's initial backdrop is transparent (§11.4.5).</summary>
    public bool Isolated { get; private set; }

    /// <summary>Gets a value indicating whether each paint composites with the initial backdrop only (§11.4.6).</summary>
    public bool Knockout { get; private set; }

    /// <summary>Gets a value indicating whether the group keeps its alpha without the backdrop, αg.</summary>
    public bool TracksAlpha { get; private set; }

    /// <summary>Gets a value indicating whether the group keeps its shape, fg.</summary>
    public bool TracksShape { get; private set; }

    /// <summary>Gets a value indicating whether the group keeps a copy of its initial backdrop.</summary>
    public bool KeepsBackdrop => Knockout || (!Isolated && TracksAlpha);

    /// <summary>Gets the pixel model of the group's content.</summary>
    public PixelModel Model { get; private set; }

    /// <summary>Gets or sets the pass being painted (0, or 1 for the black plane of a CMYK group).</summary>
    public int Pass { get; set; }

    /// <summary>Gets or sets a value indicating whether the pass being painted records the alpha and shape planes (the first pass).</summary>
    public bool Records { get; set; }

    /// <summary>Gets the alpha and blend mode the group's result composites with.</summary>
    public Compositor Compositor { get; private set; }

    /// <summary>Gets a value indicating whether the group is the page group, composited onto the white page in its own space (§11.4.7).</summary>
    public bool OntoWhite { get; private set; }

    /// <summary>Gets the group colour space (for converting the result), or <see langword="null"/> when the group blends in its parent's.</summary>
    public PdfColorSpace? ColorSpace { get; private set; }

    /// <summary>Gets the default colour spaces where the group was painted.</summary>
    public PdfDefaultColorSpaces? DefaultColorSpaces { get; private set; }

    /// <summary>Gets the conversion from the group's space to its parent's (intent and black point compensation at the group).</summary>
    public ColorConversion Conversion { get; private set; }

    /// <summary>Gets or sets what the backend held before the group: restored when it ends.</summary>
    public SavedState Saved { get; set; }

    /// <summary>Starts a group over a rectangle of the render's pixels, growing the buffers when they are too small.</summary>
    public void Open(
        int left,
        int top,
        int width,
        int height,
        bool isolated,
        bool knockout,
        bool tracksAlpha,
        bool tracksShape,
        PixelModel model,
        Compositor compositor,
        bool ontoWhite,
        PdfColorSpace? colorSpace,
        PdfDefaultColorSpaces? defaults,
        ColorConversion conversion)
    {
        (Left, Top, Width, Height) = (left, top, width, height);
        (Isolated, Knockout, TracksAlpha, TracksShape) = (isolated, knockout, tracksAlpha, tracksShape);
        (Model, Compositor, OntoWhite) = (model, compositor, ontoWhite);
        (ColorSpace, DefaultColorSpaces, Conversion) = (colorSpace, defaults, conversion);
        int planes = model.Model == DeviceColorModel.Cmyk ? 2 : 1;
        for (int plane = 0; plane < planes; plane++)
        {
            Ensure(ref _planes[plane], width, height);
            if (KeepsBackdrop)
            {
                Ensure(ref _backdrops[plane], width, height);
            }
        }

        if (tracksAlpha)
        {
            Pooled.Ensure(ref _alpha, width * height);
            _alpha.AsSpan(0, width * height).Clear();
        }

        if (tracksShape)
        {
            Pooled.Ensure(ref _shape, width * height);
            _shape.AsSpan(0, width * height).Clear();
        }

        Pooled.Ensure(ref _saved, width);
        Pooled.Ensure(ref _element, width);
    }

    /// <summary>Gets the plane of a pass.</summary>
    public Bitmap Plane(int pass) => _planes[pass]!;

    /// <summary>Returns <paramref name="count"/> pixels of a pass's plane from (<paramref name="x"/>, <paramref name="row"/>) in group pixels.</summary>
    public Span<uint> PlaneRow(int pass, int row, int x, int count) => Row(_planes[pass]!, row, x, count);

    /// <summary>Returns <paramref name="count"/> pixels of a pass's initial backdrop.</summary>
    public Span<uint> BackdropRow(int pass, int row, int x, int count) => Row(_backdrops[pass]!, row, x, count);

    /// <summary>Returns <paramref name="count"/> values of the alpha plane αg.</summary>
    public Span<byte> AlphaRow(int row, int x, int count) => _alpha.AsSpan((row * Width) + x, count);

    /// <summary>Returns <paramref name="count"/> values of the shape plane fg.</summary>
    public Span<byte> ShapeRow(int row, int x, int count) => _shape.AsSpan((row * Width) + x, count);

    /// <summary>Gets a row of scratch pixels the width of the group.</summary>
    public Span<uint> Scratch(int count) => _saved.AsSpan(0, count);

    /// <summary>
    /// Takes one run of a paint's coverage (shape fs, clipped) in group pixels and lets <paramref name="inner"/> composite it into the
    /// plane, applying knockout and recording alpha and shape around it (see the type's remarks).
    /// </summary>
    public void Span<TSink>(int row, int x, int count, int coverage, ref TSink inner)
        where TSink : ICoverageSink, allows ref struct
    {
        Span<uint> line = PlaneRow(Pass, row, x, count);
        bool alpha = TracksAlpha && Records;
        if (Knockout)
        {
            Span<uint> previous = _saved.AsSpan(0, count);
            line.CopyTo(previous);
            Span<uint> backdrop = BackdropRow(Pass, row, x, count);
            backdrop.CopyTo(line);
            inner.Span(row, x, count, coverage);
            if (alpha)
            {
                Span<uint> composited = _element.AsSpan(0, count);
                line.CopyTo(composited);
                line.Clear();
                inner.Span(row, x, count, coverage);
                Span<byte> groupAlpha = AlphaRow(row, x, count);
                for (int i = 0; i < count; i++)
                {
                    // αg = (1 − fs) αg + αs.
                    groupAlpha[i] = (byte)Math.Min(255, Compositor.Divide255(groupAlpha[i] * (255 - coverage)) + (int)(line[i] >> 24));
                }

                composited.CopyTo(line);
            }

            for (int i = 0; i < count; i++)
            {
                line[i] = Compositor.KnockOut(previous[i], line[i], backdrop[i], coverage);
            }
        }
        else if (alpha)
        {
            Span<uint> previous = _saved.AsSpan(0, count);
            line.CopyTo(previous);
            line.Clear();
            inner.Span(row, x, count, coverage);
            Span<byte> groupAlpha = AlphaRow(row, x, count);
            for (int i = 0; i < count; i++)
            {
                groupAlpha[i] = (byte)Compositor.Union(groupAlpha[i], (int)(line[i] >> 24));
            }

            previous.CopyTo(line);
            inner.Span(row, x, count, coverage);
        }
        else
        {
            inner.Span(row, x, count, coverage);
        }

        if (TracksShape && Records)
        {
            foreach (ref byte shape in ShapeRow(row, x, count))
            {
                shape = (byte)Compositor.Union(shape, coverage);
            }
        }
    }

    /// <summary>Lets go of the colour spaces the group referenced.</summary>
    public void Forget()
    {
        ColorSpace = null;
        DefaultColorSpaces = null;
        Saved = default;
    }

    /// <summary>Returns every buffer.</summary>
    public void Dispose()
    {
        Forget();
        for (int plane = 0; plane < 2; plane++)
        {
            _planes[plane]?.Dispose();
            _planes[plane] = null;
            _backdrops[plane]?.Dispose();
            _backdrops[plane] = null;
        }

        Pooled.Return(ref _alpha);
        Pooled.Return(ref _shape);
        Pooled.Return(ref _saved);
        Pooled.Return(ref _element);
    }

    private static Span<uint> Row(Bitmap bitmap, int row, int x, int count) =>
        MemoryMarshal.Cast<byte, uint>(bitmap.Pixels.Slice((row * bitmap.Stride) + (x * 4), count * 4));

    /// <summary>Makes <paramref name="bitmap"/> at least <paramref name="width"/> × <paramref name="height"/>, keeping a larger one.</summary>
    private static void Ensure(ref Bitmap? bitmap, int width, int height)
    {
        if (bitmap is not null && bitmap.Width >= width && bitmap.Height >= height)
        {
            return;
        }

        int newWidth = Math.Max(width, bitmap?.Width ?? 0);
        int newHeight = Math.Max(height, bitmap?.Height ?? 0);
        bitmap?.Dispose();
        bitmap = null;
        bitmap = new Bitmap(newWidth, newHeight);
    }

    /// <summary>What the backend held when the group opened.</summary>
    internal readonly record struct SavedState(
        Bitmap? Target,
        Matrix Device,
        int Width,
        int Height,
        int OriginX,
        int OriginY,
        PixelModel Model,
        GroupLayer? Hook);
}
