using System.Buffers;
using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// Replays a display list into a <see cref="CommandList"/> for WebGL: fills and glyph outlines by stencil-then-cover, strokes as
/// tessellated triangles, clips in the stencil buffer, images and stencil masks as textures, transparency groups that are not plain
/// drawn by the software rasterizer into a texture. Anything else (shadings, tiling patterns, blend modes other than Normal, soft
/// masks, annotations with opacity) stops the recording: the renderer then draws the tile with the software rasterizer and reports why.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.4.2 (graphics state stack), §8.5.3 (painting), §8.5.4 (clipping), §8.9 (images), §9.4.3 and §9.2.4 (glyph outlines,
/// non-zero), §11.4 and §11.6.4.4 (groups, constant alpha), §12.5.5 (annotation appearances), through the display list.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed class Recorder : IRenderingBackend
{
    private static readonly Diagnostic GroupRasterized = new(
        "BackendTransparencyGroupRasterized",
        DiagnosticSeverity.Information,
        "A transparency group with opacity, a soft mask or knockout (ISO 32000-2 §11.4) was drawn by the software rasterizer into a texture that WebGL composited.");

    private readonly CommandList _commands;
    private readonly Tessellator _tessellator = new();
    private readonly List<int> _temporary = [];
    private readonly List<Diagnostic> _diagnostics;
    private readonly float[] _color = new float[4];
    private Saved[] _saved = new Saved[16];
    private int _depth;
    private ClipPath[] _clips = new ClipPath[16];
    private int _clipCount;
    private int[] _applied = new int[16];
    private int _appliedCount;
    private int _clipIds;

    private PdfDocument _document = null!;
    private Matrix _device;
    private Matrix _ctm;
    private Matrix _toDevice;
    private int _width;
    private int _height;
    private double _strokeAlpha;
    private double _fillAlpha;
    private CancellationTokenSource _stop = null!;
    private SoftwareRasterizer? _rasterizer;

    private PdfColorSpace? _space;
    private PdfDefaultColorSpaces? _defaults;
    private ColorConversion _conversion;
    private PdfColorConverter? _converter;

    public Recorder(CommandList commands, List<Diagnostic> diagnostics)
    {
        _commands = commands;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets the reason the recording stopped (the tile then needs the software rasterizer); null while it can continue.</summary>
    public Diagnostic? Fallback { get; private set; }

    public IReadOnlyList<int> TemporaryTextures => _temporary;

    public void Reset(PdfDocument document, in Matrix device, int width, int height, CancellationTokenSource stop)
    {
        _document = document;
        _device = device;
        _width = width;
        _height = height;
        _ctm = Matrix.Identity;
        _toDevice = device;
        _strokeAlpha = 1;
        _fillAlpha = 1;
        _depth = 0;
        _clipCount = 0;
        _appliedCount = 0;
        _stop = stop;
        _temporary.Clear();
        Fallback = null;
        _converter = null;
        _space = null;
        _defaults = null;
        _commands.Clear(width, height);
        _tessellator.Begin(_commands);
    }

    public void SaveState()
    {
        if (_depth == _saved.Length)
        {
            Array.Resize(ref _saved, _depth * 2);
        }

        _saved[_depth++] = new Saved(_ctm, _clipCount, _strokeAlpha, _fillAlpha);
    }

    public void RestoreState()
    {
        if (_depth == 0)
        {
            return;
        }

        Saved saved = _saved[--_depth];
        _ctm = saved.Ctm;
        _toDevice = _ctm * _device;
        _clipCount = saved.ClipCount;
        _strokeAlpha = saved.StrokeAlpha;
        _fillAlpha = saved.FillAlpha;
    }

    public void SetTransform(in Matrix transform)
    {
        _ctm = transform;
        _toDevice = transform * _device;
    }

    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        if (Fallback is not null || !TryGetColor(color, _fillAlpha))
        {
            return;
        }

        _tessellator.Begin(_commands);
        _tessellator.Flatten(path, _toDevice, 0.2);
        Fill(rule == FillRule.EvenOdd ? CommandList.EvenOdd : CommandList.NonZero, allowDirect: true);
    }

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
        if (Fallback is not null || !TryGetColor(color, _strokeAlpha))
        {
            return;
        }

        _tessellator.Begin(_commands);
        SyncClip();
        int first = _commands.VertexCount;
        Stroke(path, Matrix.Identity, style);
        Cover(CommandList.Union, first);
    }

    public void IntersectClip(PathView path, FillRule rule)
    {
        if (Fallback is not null)
        {
            return;
        }

        _tessellator.Begin(_commands);
        _tessellator.Flatten(path, _toDevice, 0.2);
        int first = _commands.VertexCount;
        int count = _tessellator.EmitFill();
        if (_clipCount == _clips.Length)
        {
            Array.Resize(ref _clips, _clipCount * 2);
        }

        _clips[_clipCount++] = new ClipPath(first, count, rule == FillRule.EvenOdd ? CommandList.EvenOdd : CommandList.NonZero, ++_clipIds);
    }

    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        if (Fallback is not null)
        {
            return;
        }

        bool unfoldedSoft = image.SoftMask is not null && !ImageConverter.FoldsSoftMask(image);
        bool unfoldedMask = image.SoftMask is null && image.Mask is not null && !ImageConverter.FoldsMask(image);
        if (unfoldedSoft || unfoldedMask)
        {
            Stop(
                "BackendImageMaskRasterized",
                "An image whose explicit or soft mask has another size than the image (ISO 32000-2 §8.9.6.3, §11.6.5.2) made the software rasterizer draw the tile.");
            return;
        }

        int texture = TextureCache.GetImage(image, _document, defaultColorSpaces, conversion);
        if (texture == 0)
        {
            Stop("BackendImageTooLarge", "An image larger than the largest WebGL texture (ISO 32000-2 §8.9.5) made the software rasterizer draw the tile.");
            return;
        }

        float a = (float)_fillAlpha;
        _color[0] = a;
        _color[1] = a;
        _color[2] = a;
        _color[3] = a;
        DrawUnitSquare(CommandList.Image, texture);
    }

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
        if (Fallback is not null || !TryGetColor(color, _fillAlpha))
        {
            return;
        }

        int texture = TextureCache.GetMask(mask);
        if (texture == 0)
        {
            Stop("BackendImageTooLarge", "A stencil mask larger than the largest WebGL texture (ISO 32000-2 §8.9.6.2) made the software rasterizer draw the tile.");
            return;
        }

        DrawUnitSquare(CommandList.Mask, texture);
    }

    public void BeginAnnotation(in DisplayAnnotation annotation)
    {
        if (Fallback is not null)
        {
            return;
        }

        if (annotation.Opacity < 1 || annotation.BlendMode != BlendMode.Normal)
        {
            Stop(
                "BackendAnnotationRasterized",
                "An annotation appearance composited with opacity or a blend mode (ISO 32000-2 §12.5.5, §11.6.4.4) made the software rasterizer draw the tile.");
            return;
        }

        SaveState();
    }

    public void EndAnnotation()
    {
        if (Fallback is null)
        {
            RestoreState();
        }
    }

    public void DrawGlyphs(in GlyphRun run)
    {
        if (Fallback is not null)
        {
            return;
        }

        ReadOnlySpan<DisplayGlyph> glyphs = run.Glyphs;
        if (run.Fills && TryGetColor(run.FillColor, _fillAlpha))
        {
            _tessellator.Begin(_commands);
            SyncClip();
            int first = _commands.VertexCount;
            for (int i = 0; i < glyphs.Length; i++)
            {
                PathView outline = run.GetOutline(i);
                if (outline.IsEmpty)
                {
                    continue;
                }

                Matrix toDevice = run.GetGlyphTransform(i) * _toDevice;
                if (run.Program is { } program)
                {
                    _tessellator.LoadContours(GlyphCache.Get(program, glyphs[i].GlyphId, outline, toDevice, _tessellator), toDevice);
                }
                else
                {
                    _tessellator.Flatten(outline, toDevice, 0.2);
                }

                _tessellator.EmitFill();
            }

            Cover(CommandList.NonZero, first);
        }

        if (run.Strokes && TryGetColor(run.StrokeColor, _strokeAlpha))
        {
            _tessellator.Begin(_commands);
            SyncClip();
            int first = _commands.VertexCount;
            StrokeStyle style = run.StrokeStyle;
            for (int i = 0; i < glyphs.Length; i++)
            {
                PathView outline = run.GetOutline(i);
                if (!outline.IsEmpty)
                {
                    Stroke(outline, run.GetGlyphTransform(i), style);
                }
            }

            Cover(CommandList.Union, first);
        }
    }

    public void DrawShading(in DisplayShading shading) =>
        Stop("BackendShadingRasterized", "A shading (ISO 32000-2 §8.7.4.5) made the software rasterizer draw the tile.");

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) =>
        Stop("BackendShadingRasterized", "A shading pattern (ISO 32000-2 §8.7.4) made the software rasterizer draw the tile.");

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) =>
        Stop("BackendShadingRasterized", "A shading pattern (ISO 32000-2 §8.7.4) made the software rasterizer draw the tile.");

    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) =>
        Stop("BackendTilingPatternRasterized", "A tiling pattern (ISO 32000-2 §8.7.3) made the software rasterizer draw the tile.");

    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) =>
        Stop("BackendTilingPatternRasterized", "A tiling pattern (ISO 32000-2 §8.7.3) made the software rasterizer draw the tile.");

    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
        if (blendMode != BlendMode.Normal)
        {
            Stop("BackendBlendModeRasterized", "A blend mode other than Normal (ISO 32000-2 §11.3.5) made the software rasterizer draw the tile.");
            return;
        }

        _strokeAlpha = Math.Clamp(strokeAlpha, 0, 1);
        _fillAlpha = Math.Clamp(fillAlpha, 0, 1);
    }

    public void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
        // A composite RGB backend ignores overprint and halftones (§8.6.7, §10.6).
    }

    public void SetSoftMask(in DisplaySoftMask mask)
    {
        if (!mask.IsNone)
        {
            Stop("BackendSoftMaskRasterized", "A soft mask in the graphics state (ISO 32000-2 §11.6.5.1) made the software rasterizer draw the tile.");
        }
    }

    public void DrawGroup(in DisplayGroup group)
    {
        if (Fallback is not null)
        {
            return;
        }

        if (group.BlendMode != BlendMode.Normal)
        {
            Stop("BackendBlendModeRasterized", "A transparency group composited in a blend mode other than Normal (ISO 32000-2 §11.3.5, §11.4) made the software rasterizer draw the tile.");
            return;
        }

        if (group.Opacity >= 1 && group.SoftMask.IsNone && !group.IsKnockout)
        {
            // A plain group composites like its content drawn directly: with only Normal blending (any other blend mode stops the
            // recording) source-over is associative, so isolation changes nothing (§11.4.5); like the Skia backend (#124), the group
            // colour space is left to content that blends (§11.4.7).
            SaveState();
            group.Replay(this);
            RestoreState();
            return;
        }

        _rasterizer ??= new SoftwareRasterizer();
        int length = _width * _height * 4;
        byte[] pixels = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            pixels.AsSpan(0, length).Clear();
            using (var layer = new Bitmap(pixels.AsMemory(0, length), _width, _height, _width * 4))
            {
                _rasterizer.Render(group, _document, layer, _device, _ctm, _strokeAlpha, _fillAlpha, BlendMode.Normal);
            }

            Span<byte> rgba = pixels.AsSpan(0, length);
            for (int i = 0; i < length; i += 4)
            {
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            }

            int texture = TextureCache.UploadTemporary(rgba, _width, _height);
            _temporary.Add(texture);
            Report(GroupRasterized);
            SyncClip();
            int first = _commands.VertexCount;
            _commands.Add(0, 0, 0, 0);
            _commands.Add(_width, 0, 1, 0);
            _commands.Add(_width, _height, 1, 1);
            _commands.Add(0, 0, 0, 0);
            _commands.Add(_width, _height, 1, 1);
            _commands.Add(0, _height, 0, 1);
            _color[0] = 1;
            _color[1] = 1;
            _color[2] = 1;
            _color[3] = 1;
            _commands.Texture(CommandList.Image, texture, first, 6, _color);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    public void DisposeRasterizer() => _rasterizer?.Dispose();

    private void Fill(int rule, bool allowDirect)
    {
        SyncClip();
        int first = _commands.VertexCount;
        if (allowDirect && _tessellator.IsSimpleConvex())
        {
            int count = _tessellator.EmitConvex();
            if (count > 0 && Visible())
            {
                _commands.Paint(CommandList.Solid, first, count, _color);
            }

            return;
        }

        _tessellator.EmitFill();
        Cover(rule, first);
    }

    /// <summary>Writes the triangles since <paramref name="first"/> to the stencil and covers their bounds with the current colour.</summary>
    private void Cover(int rule, int first)
    {
        int count = _commands.VertexCount - first;
        if (count == 0 || !Visible())
        {
            return;
        }

        _commands.Command(CommandList.Stencil, rule, first, count);
        int quad = _commands.VertexCount;
        float left = (float)Math.Max(0, Math.Floor(_tessellator.Left) - 1);
        float top = (float)Math.Max(0, Math.Floor(_tessellator.Top) - 1);
        float right = (float)Math.Min(_width, Math.Ceiling(_tessellator.Right) + 1);
        float bottom = (float)Math.Min(_height, Math.Ceiling(_tessellator.Bottom) + 1);
        _commands.AddQuad(left, top, right, bottom);
        _commands.Paint(CommandList.Cover, quad, 6, _color);
    }

    private bool Visible() =>
        _tessellator.Right >= 0 && _tessellator.Bottom >= 0 && _tessellator.Left <= _width && _tessellator.Top <= _height;

    /// <summary>Emits the stroke of <paramref name="path"/> (mapped by <paramref name="toUser"/> to user space) as union triangles.</summary>
    private void Stroke(PathView path, in Matrix toUser, in StrokeStyle style)
    {
        Matrix m = _toDevice;
        double sx = Math.Sqrt((m.A * m.A) + (m.B * m.B));
        double sy = Math.Sqrt((m.C * m.C) + (m.D * m.D));
        double max = Math.Max(sx, sy);
        if (max == 0)
        {
            return;
        }

        double width = style.LineWidth;
        double deviceWidth = width * max;
        Matrix toDevice = toUser * _toDevice;
        if (!(width > 0) || (style.StrokeAdjustment && deviceWidth < 0.5))
        {
            // §8.4.3.2, §10.7.5: the thinnest line, one device pixel wide.
            _tessellator.Flatten(path, toDevice, 0.2);
            _tessellator.EmitStroke(style, 0.5, Matrix.Identity, 1, device: true);
            return;
        }

        if (style.StrokeAdjustment && double.IsFinite(deviceWidth) && _toDevice.TryInvert(out Matrix toPen))
        {
            // §10.7.5: a whole number of pixels, no more than half a pixel from the requested width, edges on pixel boundaries.
            double pixels = Math.Max(1, Math.Round(deviceWidth, MidpointRounding.AwayFromZero));
            _tessellator.Flatten(path, toDevice, 0.2);
            _tessellator.Snap(pixels % 2 == 1 ? 0.5 : 0, toPen);
            _tessellator.EmitStroke(style, pixels / (2 * max), _toDevice, max, device: false);
            return;
        }

        _tessellator.Flatten(path, toUser, 0.2 / max);
        _tessellator.EmitStroke(style, width / 2, _toDevice, max, device: false);
    }

    private void DrawUnitSquare(int op, int texture)
    {
        SyncClip();
        int first = _commands.VertexCount;
        PathPoint p00 = _toDevice.Transform(0, 0);
        PathPoint p10 = _toDevice.Transform(1, 0);
        PathPoint p11 = _toDevice.Transform(1, 1);
        PathPoint p01 = _toDevice.Transform(0, 1);
        double left = Math.Min(Math.Min(p00.X, p10.X), Math.Min(p11.X, p01.X));
        double right = Math.Max(Math.Max(p00.X, p10.X), Math.Max(p11.X, p01.X));
        double top = Math.Min(Math.Min(p00.Y, p10.Y), Math.Min(p11.Y, p01.Y));
        double bottom = Math.Max(Math.Max(p00.Y, p10.Y), Math.Max(p11.Y, p01.Y));
        if (right < 0 || bottom < 0 || left > _width || top > _height)
        {
            return;
        }

        // Image space: the first row is at the top of the unit square (§8.9.4), so user (x, y) samples the texture at (x, 1 - y).
        _commands.Add((float)p00.X, (float)p00.Y, 0, 1);
        _commands.Add((float)p10.X, (float)p10.Y, 1, 1);
        _commands.Add((float)p11.X, (float)p11.Y, 1, 0);
        _commands.Add((float)p00.X, (float)p00.Y, 0, 1);
        _commands.Add((float)p11.X, (float)p11.Y, 1, 0);
        _commands.Add((float)p01.X, (float)p01.Y, 0, 0);
        _commands.Texture(op, texture, first, 6, _color);
    }

    /// <summary>Brings the stencil's clip in line with the current state: appends new clip paths, or resets and replays them all.</summary>
    private void SyncClip()
    {
        int common = 0;
        while (common < _appliedCount && common < _clipCount && _applied[common] == _clips[common].Id)
        {
            common++;
        }

        if (common == _appliedCount && common == _clipCount)
        {
            return;
        }

        if (common < _appliedCount)
        {
            _commands.Command(CommandList.ClipReset);
            common = 0;
        }

        if (_applied.Length < _clipCount)
        {
            Array.Resize(ref _applied, Math.Max(_clipCount, _applied.Length * 2));
        }

        for (int i = common; i < _clipCount; i++)
        {
            ClipPath clip = _clips[i];
            _commands.Command(CommandList.Clip, clip.Rule, clip.First, clip.Count);
            _applied[i] = clip.Id;
        }

        _appliedCount = _clipCount;
    }

    /// <summary>Converts a colour to premultiplied RGBA with <paramref name="alpha"/>; false when it paints nothing (Separation None).</summary>
    private bool TryGetColor(in DisplayColor color, double alpha)
    {
        if (_converter is null || !ReferenceEquals(_space, color.ColorSpace) || !ReferenceEquals(_defaults, color.DefaultColorSpaces) || _conversion != color.Conversion)
        {
            _space = color.ColorSpace;
            _defaults = color.DefaultColorSpaces;
            _conversion = color.Conversion;
            _converter = _document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
        }

        if (_converter.PaintsNothing || alpha <= 0)
        {
            return false;
        }

        Span<float> rgb = stackalloc float[4];
        _converter.Convert(color.Components, rgb);
        float a = (float)alpha;
        _color[0] = Channel(rgb[0]) * a;
        _color[1] = Channel(rgb[1]) * a;
        _color[2] = Channel(rgb[2]) * a;
        _color[3] = a;
        return true;
    }

    // The software rasterizer paints 8-bit colours: quantize the same way so flat areas match exactly.
    private static float Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f) / 255f;

    private void Stop(string code, string message)
    {
        if (Fallback is not null)
        {
            return;
        }

        Fallback = new Diagnostic(code, DiagnosticSeverity.Information, message);
        _stop.Cancel();
    }

    private void Report(Diagnostic diagnostic)
    {
        foreach (Diagnostic known in _diagnostics)
        {
            if (known.Code == diagnostic.Code)
            {
                return;
            }
        }

        _diagnostics.Add(diagnostic);
    }

    private readonly record struct Saved(Matrix Ctm, int ClipCount, double StrokeAlpha, double FillAlpha);

    private readonly record struct ClipPath(int First, int Count, int Rule, int Id);
}
