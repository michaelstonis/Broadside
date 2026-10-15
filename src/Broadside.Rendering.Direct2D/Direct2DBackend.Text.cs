using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;

namespace Broadside.Rendering.Direct2D;

/// <summary>Glyph runs (ISO 32000-2 §9.4.3, §9.3.6, §9.2.4): natively through DirectWrite, or from the outlines the display list carries.</summary>
/// <remarks>
/// <para>
/// Filled glyphs of a TrueType or OpenType program are drawn by DirectWrite from a font face loaded from the program's bytes
/// (<see cref="DirectWriteFonts"/>), one glyph at a time with <c>DrawGlyphRun</c> under the transform from DirectWrite's em space to
/// the device: the y flip to glyph space, the glyph's transform to user space (font matrix, horizontal scale, text matrix, §9.4.4), the
/// current transform and the device transform. Every other glyph, and every glyph when outlines are forced
/// (<see cref="RenderOptions.ForceOutlines"/>), is filled from its outline by the non-zero winding rule (§9.2.4) as an ordinary path
/// in user space. A sfnt program DirectWrite cannot read as the program does draws from its outlines and is reported once per render
/// (<c>BackendNativeGlyphsUnavailable</c>).
/// </para>
/// <para>
/// Strokes (modes 1, 2, 5 and 6) always outline: each glyph's outline is mapped to user space and stroked like a path with the run's
/// line parameters (§9.3.6, §8.4.3), after the glyph's fill. Mode 3 and 7 runs paint nothing; text clips (modes 4 to 7) arrive as
/// clips (<see cref="IntersectClip"/>), and Type 3 glyphs as the ordinary paints of their descriptions.
/// </para>
/// </remarks>
internal sealed unsafe partial class Direct2DBackend
{
    private static readonly Diagnostic NativeGlyphsUnavailable = new(
        "BackendNativeGlyphsUnavailable",
        DiagnosticSeverity.Information,
        "DirectWrite could not read an embedded TrueType or OpenType program as Broadside reads it (a program that needed a repair, a " +
        "file DirectWrite rejects, or a different glyph count or units per em; ISO 32000-2 §9.9); its glyphs were drawn from their outlines.");

    private DirectWriteFonts? _fonts;

    // A glyph's outline mapped to user space, for outline fills and strokes.
    private PathPoint[] _glyphPoints = new PathPoint[64];

    /// <summary>Gets or sets a value indicating whether every glyph is filled from its outline, never by DirectWrite (<see cref="RenderOptions.ForceOutlines"/>).</summary>
    public bool ForceOutlines { get; set; }

    /// <summary>Draws a glyph run (ISO 32000-2 §9.4.3) in its rendering mode (§9.3.6): each glyph filled, then stroked, before the next.</summary>
    public void DrawGlyphs(in GlyphRun run)
    {
        if (run.Glyphs.IsEmpty || !(run.Fills || run.Strokes))
        {
            return;
        }

        PdfColorConverter? fill = null;
        if (run.Fills && _compositing.FillAlpha != 0)
        {
            fill = Converter(run.FillColor);
            if (fill.PaintsNothing)
            {
                fill = null;
            }
        }

        bool strokes = run.Strokes && _compositing.StrokeAlpha != 0;
        if (fill is null && !strokes)
        {
            return;
        }

        DirectWriteFonts.Face? face = fill is not null ? NativeFace(run.Program) : null;
        bool brushIsFill = false;
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            PathView outline = run.GetOutline(index);
            if (outline.IsEmpty)
            {
                // §9.2.4: a glyph without an outline (a space, a missing glyph) paints nothing, natively or not.
                continue;
            }

            Matrix glyphToUser = run.GetGlyphTransform(index);
            if (fill is not null)
            {
                if (!brushIsFill)
                {
                    SetColor(fill, run.FillColor.Components, _compositing.FillAlpha);
                    brushIsFill = true;
                }

                int glyphId = run.Glyphs[index].GlyphId;
                if (face is null || (uint)glyphId >= (uint)face.GlyphCount || !DrawNative(face, (ushort)glyphId, glyphToUser))
                {
                    FillOutline(ToUserSpace(outline, glyphToUser));
                }
            }

            if (strokes)
            {
                StrokePath(ToUserSpace(outline, glyphToUser), run.StrokeStyle, run.StrokeColor);
                brushIsFill = false;
            }
        }
    }

    /// <summary>Returns the DirectWrite face of the run's program, or null when its glyphs draw from their outlines.</summary>
    private DirectWriteFonts.Face? NativeFace(FontProgram? program)
    {
        if (ForceOutlines || program is null || _fonts is null)
        {
            return null;
        }

        DirectWriteFonts.Face? face = _fonts.GetFace(program, out bool isSfnt);
        if (face is null && isSfnt)
        {
            Skip(SkippedOperations.NativeGlyphs, NativeGlyphsUnavailable);
        }

        return face;
    }

    /// <summary>
    /// Draws one glyph with DirectWrite: at an em size of the face's units per em one DirectWrite unit is one design unit (y down), so
    /// the world transform is the y flip to glyph space times the glyph's transform to user space times the current transform.
    /// </summary>
    private bool DrawNative(DirectWriteFonts.Face face, ushort glyphId, in Matrix glyphToUser)
    {
        Matrix world = new Matrix(1, 0, 0, -1, 0, 0) * glyphToUser * _toDevice;
        if (!(double.IsFinite(world.A) && double.IsFinite(world.B) && double.IsFinite(world.C) && double.IsFinite(world.D)
            && double.IsFinite(world.E) && double.IsFinite(world.F)) || (world.A * world.D) - (world.B * world.C) == 0)
        {
            return false;
        }

        float advance = 0;
        var glyphRun = new DWRITE_GLYPH_RUN
        {
            fontFace = face.Pointer,
            fontEmSize = face.UnitsPerEm,
            glyphCount = 1,
            glyphIndices = &glyphId,
            glyphAdvances = &advance,
            glyphOffsets = null,
            isSideways = false,
            bidiLevel = 0,
        };
        SetWorldTransform(world);
        _context->DrawGlyphRun(default, &glyphRun, null, (ID2D1Brush*)_brush, DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
        return true;
    }

    /// <summary>Fills a glyph outline already in user space by the non-zero winding rule, with no degenerate-subpath pixels (§9.2.4).</summary>
    private void FillOutline(PathView outline)
    {
        ID2D1PathGeometry* geometry = CreateFillGeometry(outline, FillRule.NonZero, collectPixels: false, out bool hasFigures);
        try
        {
            if (hasFigures)
            {
                SetWorldTransform(_toDevice);
                _context->FillGeometry((ID2D1Geometry*)geometry, (ID2D1Brush*)_brush, null);
            }
        }
        finally
        {
            geometry->Release();
        }
    }

    /// <summary>Maps a glyph outline from glyph space to user space into a kept buffer; valid until the next call.</summary>
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

    /// <summary>Starts drawing text: grayscale anti-aliasing with the linear, unhinted parameters of <see cref="DirectWriteFonts"/>.</summary>
    private void BeginText(DirectWriteFonts? fonts)
    {
        _fonts = fonts;
        IDWriteRenderingParams* parameters = fonts is null ? null : fonts.RenderingParameters;
        if (parameters is not null)
        {
            _context->SetTextRenderingParams(parameters);
        }

        _context->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
    }
}
