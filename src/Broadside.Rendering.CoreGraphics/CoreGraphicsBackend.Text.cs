using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Text (ISO 32000-2 §9.2.4, §9.3.6, §9.4.3): glyph runs filled by CoreGraphics' own font engine from the program's bytes where it can
/// load them, from the display list's outlines otherwise, and stroked from the outlines.
/// </summary>
internal sealed partial class CoreGraphicsBackend
{
    /// <summary>Glyph positions mapped beyond ±2^24 units of the context, or scaled beyond it, are filled from their outlines.</summary>
    private const double NativeGlyphLimit = 16777216.0;

    private static readonly Diagnostic GlyphsOutlined = new(
        "BackendNativeGlyphsUnavailable",
        DiagnosticSeverity.Information,
        "CoreGraphics could not load an embedded TrueType or OpenType program as this library reads it (ISO 32000-2 §9.9); its glyphs " +
        "were drawn from their outlines.");

    private readonly ConditionalWeakTable<FontProgram, NativeFont> _fonts = new();
    private bool _glyphsOutlinedReported;
    private readonly ushort[] _glyph = new ushort[1];
    private readonly CGPoint[] _origin = new CGPoint[1];

    // A glyph's outline mapped to user space, for stroking with a pen in user space.
    private PathPoint[] _glyphPoints = new PathPoint[64];

    /// <summary>Gets or sets a value indicating whether every glyph is filled from its outline (<see cref="RenderOptions.ForceOutlines"/>).</summary>
    public bool ForceOutlines { get; set; }

    /// <summary>
    /// Draws each glyph of the run: filled (modes 0, 2, 4, 6) by CoreGraphics from the run's TrueType or OpenType program at the glyph's
    /// transform, or from its outline by the non-zero rule when the program cannot be drawn natively or outlines are forced; stroked
    /// (modes 1, 2, 5, 6) from its outline mapped to user space with the run's line parameters, after its fill, glyph by glyph.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.2.4 (glyph space, the font matrix, outlines filled non-zero), §9.3.6 (Table 104), §8.4.3 (text strokes use the
    /// graphics state's line parameters in user space) and §9.9 (embedded programs). A glyph whose outline is empty draws nothing,
    /// as in the software rasterizer, so a substitute's missing glyph or a glyph the parser could not read is not drawn natively
    /// either. Modes 4 to 7 add to the clip through <see cref="IntersectClip"/>, which the list records at the end of the text object.
    /// </remarks>
    public void DrawGlyphs(in GlyphRun run)
    {
        if (run.Glyphs.IsEmpty || !(run.Fills || run.Strokes))
        {
            return;
        }

        CGContext context = _context!;
        bool fills = run.Fills && _compositing.FillAlpha != 0 && TryLoadColor(run.FillColor, _compositing.FillAlpha);
        if (fills)
        {
            context.SetFillColor(_color);
        }

        bool strokes = run.Strokes && _compositing.StrokeAlpha != 0 && TryLoadColor(run.StrokeColor, _compositing.StrokeAlpha);
        if (strokes)
        {
            context.SetStrokeColor(_color);
        }

        if (!fills && !strokes)
        {
            return;
        }

        NativeFont? font = fills && !ForceOutlines && run.Program is { } program ? GetNativeFont(program, run.FontMatrix) : null;
        if (font is not null)
        {
            context.SetFont(font.Font);
            SetFontSize(context.Handle, 1);
            context.SetTextDrawingMode(CGTextDrawingMode.Fill);
        }

        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            PathView outline = run.GetOutline(index);
            if (outline.IsEmpty)
            {
                continue;
            }

            Matrix glyphToUser = run.GetGlyphTransform(index);
            if (fills && (font is null || !TryShowGlyph(context, font, run.Glyphs[index].GlyphId, glyphToUser)))
            {
                context.BeginPath();
                if (AddMappedPath(context, outline, glyphToUser * _toDevice, degenerateDots: false))
                {
                    context.FillPath();
                }
            }

            if (strokes)
            {
                _stroker.Stroke(context, ToUserSpace(outline, glyphToUser), run.StrokeStyle, _toDevice);
            }
        }
    }

    /// <summary>
    /// Shows one glyph with the context's font: the text matrix takes CoreGraphics' em square (glyph units over units per em at font
    /// size 1) to the glyph's place, <c>[unitsPerEm 0 0 unitsPerEm 0 0] × glyph space → user space × user space → the context</c>.
    /// </summary>
    /// <returns>False when the glyph is better filled from its outline: an id CoreGraphics' font lacks, or a transform that is
    /// singular, not finite or beyond <see cref="NativeGlyphLimit"/>.</returns>
    private bool TryShowGlyph(CGContext context, NativeFont font, int glyphId, in Matrix glyphToUser)
    {
        if ((uint)glyphId >= (uint)font.GlyphCount)
        {
            return false;
        }

        Matrix m = Matrix.CreateScale(font.UnitsPerEm, font.UnitsPerEm) * glyphToUser * _toDevice;
        double determinant = m.Determinant;
        if (!(Math.Abs(determinant) > 1e-12) || !double.IsFinite(determinant)
            || !(Math.Abs(m.A) + Math.Abs(m.B) + Math.Abs(m.C) + Math.Abs(m.D) < NativeGlyphLimit)
            || !(Math.Abs(m.E) < NativeGlyphLimit) || !(Math.Abs(m.F) < NativeGlyphLimit))
        {
            return false;
        }

        context.TextMatrix = new CGAffineTransform((nfloat)m.A, (nfloat)m.B, (nfloat)m.C, (nfloat)m.D, (nfloat)m.E, (nfloat)m.F);
        _glyph[0] = (ushort)glyphId;
        context.ShowGlyphsAtPositions(_glyph, _origin, 1);
        return true;
    }

    /// <summary>
    /// The CoreGraphics font of a program, created once per program for this renderer's lifetime; <see langword="null"/> when the
    /// program has no sfnt bytes to hand over (<see cref="FontProgram.TryGetSfntData"/>: not TrueType or OpenType, or repaired), is a
    /// face of a collection other than the first, CoreGraphics cannot load it,
    /// or the font it loads is not the program's (another glyph count, or an em square the font matrix does not scale by).
    /// </summary>
    private NativeFont? GetNativeFont(FontProgram program, in Matrix fontMatrix)
    {
        if (!_fonts.TryGetValue(program, out NativeFont? font))
        {
            font = NativeFont.Create(program);
            _fonts.AddOrUpdate(program, font);
        }

        if (font.Refused && !_glyphsOutlinedReported)
        {
            // ADR 0005: once per replay, as the other backends report it.
            _glyphsOutlinedReported = true;
            Diagnostics.Add(GlyphsOutlined);
        }

        // The run's font matrix is the program's; a font matrix that is not CoreGraphics' em scaling (an OpenType CFF table whose
        // FontMatrix differs from the "head" table's unitsPerEm) is drawn from the outlines, which follow the font matrix.
        double scale = 1.0 / font.UnitsPerEm;
        return font.Font is not null
            && Math.Abs(fontMatrix.A - scale) <= scale * 1e-9 && Math.Abs(fontMatrix.D - scale) <= scale * 1e-9
            && fontMatrix.B == 0 && fontMatrix.C == 0 && fontMatrix.E == 0 && fontMatrix.F == 0
            ? font
            : null;
    }

    /// <summary>Drops the fonts loaded so far; their native memory is released when nothing draws with them any more.</summary>
    public void ClearFonts() => _fonts.Clear();

    /// <summary>Maps a glyph outline's points to user space, into a buffer kept for the next glyph.</summary>
    private PathView ToUserSpace(PathView outline, in Matrix glyphToUser)
    {
        ReadOnlySpan<PathPoint> points = outline.Points;
        if (_glyphPoints.Length < points.Length)
        {
            _glyphPoints = new PathPoint[Math.Max(points.Length, _glyphPoints.Length * 2)];
        }

        for (int index = 0; index < points.Length; index++)
        {
            _glyphPoints[index] = glyphToUser.Transform(points[index]);
        }

        return new PathView(outline.Verbs, _glyphPoints.AsSpan(0, points.Length));
    }

    // CGContextSetFontSize has no managed binding; it is part of the graphics state, so RestoreState restores it.
    [DllImport(ObjCRuntime.Constants.CoreGraphicsLibrary, EntryPoint = "CGContextSetFontSize")]
    private static extern void SetFontSize(IntPtr context, nfloat size);

    // CGContextSetShouldSubpixelQuantizeFonts (graphics state) has no managed binding either.
    [DllImport(ObjCRuntime.Constants.CoreGraphicsLibrary, EntryPoint = "CGContextSetShouldSubpixelQuantizeFonts")]
    private static extern void SetShouldSubpixelQuantizeFonts(IntPtr context, [MarshalAs(UnmanagedType.I1)] bool quantize);

    /// <summary>A program's CoreGraphics font, or the record that it has none, with what <see cref="TryShowGlyph"/> needs of it.</summary>
    private sealed class NativeFont
    {
        private NativeFont(CGFont? font, int glyphCount, double unitsPerEm)
        {
            Font = font;
            GlyphCount = glyphCount;
            UnitsPerEm = unitsPerEm;
        }

        /// <summary>A program whose sfnt bytes CoreGraphics did not load as this library reads them: drawn from its outlines, and reported.</summary>
        private static readonly NativeFont RefusedFont = new(null, 0, 1) { Refused = true };

        /// <summary>A program with no sfnt bytes to hand over: drawn from its outlines.</summary>
        private static readonly NativeFont None = new(null, 0, 1);

        /// <summary>Gets the font; <see langword="null"/> when the program is drawn from its outlines.</summary>
        public CGFont? Font { get; }

        /// <summary>Gets a value indicating whether the program had sfnt bytes that CoreGraphics did not load as this library reads them.</summary>
        public bool Refused { get; private init; }

        /// <summary>Gets the number of glyphs both the program and the font have.</summary>
        public int GlyphCount { get; }

        /// <summary>Gets the font's units per em (OpenType "head").</summary>
        public double UnitsPerEm { get; }

        /// <summary>Loads a program's bytes into a CoreGraphics font and checks that it is the same font.</summary>
        public static NativeFont Create(FontProgram program)
        {
            // CGFontCreateWithDataProvider reads the first font of a collection only, so another face is drawn from its outlines.
            if (!program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex) || data.IsEmpty)
            {
                return None;
            }

            if (faceIndex != 0)
            {
                return RefusedFont;
            }

            using CGDataProvider provider = MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
                ? new CGDataProvider(segment.Array!, segment.Offset, segment.Count)
                : new CGDataProvider(data.ToArray());
            CGFont? font = CGFont.CreateFromProvider(provider);
            try
            {
                int units = font is null ? 0 : (int)font.UnitsPerEm;
                if (font is null || (int)font.NumberOfGlyphs != program.GlyphCount || units <= 0)
                {
                    return RefusedFont;
                }

                var native = new NativeFont(font, Math.Min(program.GlyphCount, ushort.MaxValue + 1), units);
                font = null;
                return native;
            }
            finally
            {
                font?.Dispose();
            }
        }
    }
}
