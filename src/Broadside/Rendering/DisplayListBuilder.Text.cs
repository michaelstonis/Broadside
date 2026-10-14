using Broadside.Caching;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Text (ISO 32000-2 §9.4.3) as glyph runs: each shown glyph's program and glyph id, its text matrix and its substitute scale, with
/// outlines interned once per program and glyph id; consecutive glyphs painted alike share one run.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayGlyphRun[] _glyphRuns = new DisplayGlyphRun[8];
    private int _glyphRunCount;
    private DisplayGlyph[] _glyphs = new DisplayGlyph[64];
    private int _glyphCount;

    // Where the open run's DrawGlyphs record is in the operations; −1 when the last operation is not a run that can grow.
    private int _runOperation = -1;

    // Outlines interned in this build: (program, glyph id) -> path index.
    private readonly Dictionary<(FontProgram Program, int GlyphId), int> _outlines = [];
    private GlyphOutline? _outline;

    // The program the last font drew with, looked up once per font per build.
    private PdfFont? _programFont;
    private FontProgram? _program;
    private FontSubstitute? _substitute;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.4.3: each glyph enters the list with the glyph id its font selects (§9.6.5 for simple fonts, §9.7.4.2 and
    /// §9.7.6.3 for composite fonts) in the font's embedded program (§9.9) or, without one, in the substitute the engine's font
    /// resolvers found (§9.6.2.2, ADR 0009), scaled horizontally to the width the PDF gives the glyph; a glyph name the substitute
    /// draws as several glyphs (Adobe Glyph List specification §2, as <c>f_i</c>) enters as its components side by side, together
    /// scaled to that width. Its text matrix places it
    /// (§9.4.4); its rendering mode (§9.3.6) decides whether the run fills with the nonstroking colour, strokes with the stroking
    /// colour and line parameters, both, or neither (mode 3, kept for hit-testing).
    /// </para>
    /// <para>Type 3 glyphs (§9.6.4) and the clip of modes 4 to 7 are not in the list yet; later Phase 3 tickets add them.</para>
    /// </remarks>
    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        if (glyph.Font is not { } font || font is PdfType3Font)
        {
            return;
        }

        FontProgram? program = SelectGlyph(font, glyph, out int glyphId, out double scale, out int[]? components);
        ref readonly GraphicsState state = ref context.State;
        TextRenderingMode mode = glyph.RenderingMode;
        bool fills = mode is TextRenderingMode.Fill or TextRenderingMode.FillStroke or TextRenderingMode.FillClip or TextRenderingMode.FillStrokeClip
            && state.FillColor.ColorSpace.Family != PdfColorSpaceFamily.Pattern;
        bool strokes = mode is TextRenderingMode.Stroke or TextRenderingMode.FillStroke or TextRenderingMode.StrokeClip or TextRenderingMode.FillStrokeClip
            && state.StrokeColor.ColorSpace.Family != PdfColorSpaceFamily.Pattern;
        ColorConversion conversion = ColorConversion.FromState(state, DeviceColorModel.Rgb);
        if (!ContinuesRun(program, mode, fills, strokes, state, context.DefaultColorSpaces, conversion))
        {
            int fill = fills ? AddColor(state.FillColor, context.DefaultColorSpaces, conversion) : -1;
            int stroke = strokes ? AddStroke(AddColor(state.StrokeColor, context.DefaultColorSpaces, conversion), state) : -1;
            SetTransform(state.Ctm);
            _runOperation = _operationCount;
            AddOperation(new DisplayOperation(DisplayOperationKind.DrawGlyphs, default, _glyphRunCount, 0));
            Append(ref _glyphRuns, ref _glyphRunCount, new DisplayGlyphRun(program, program?.FontMatrix ?? Matrix.Identity, mode, _glyphCount, 0, fill, stroke));
        }

        if (components is null)
        {
            // A substitute's missing glyph is not the font's: a code it has no glyph for draws nothing rather than its .notdef box.
            int outline = _substitute is not null && glyphId == 0 ? InternPath(default) : InternOutline(program, glyphId, context.Document.Caches);
            AppendGlyph(new DisplayGlyph(glyphId, glyph.TextMatrix, scale, outline));
            return;
        }

        // A name the substitute draws as several glyphs (Adobe Glyph List specification §2): each component starts where the one before
        // it ends, at the substitute's advances stretched by the same scale, so together they fill the width the PDF gives the code.
        double offset = 0;
        foreach (int component in components)
        {
            Matrix textMatrix = offset == 0 ? glyph.TextMatrix : Matrix.CreateTranslation(offset, 0) * glyph.TextMatrix;
            AppendGlyph(new DisplayGlyph(component, textMatrix, scale, InternOutline(program, component, context.Document.Caches)));
            offset += _substitute!.GetWidth(component) * scale / 1000;
        }
    }

    /// <summary>Appends a glyph to the open run.</summary>
    private void AppendGlyph(in DisplayGlyph entry)
    {
        ref DisplayGlyphRun run = ref _glyphRuns[_glyphRunCount - 1];
        run = run with { GlyphCount = run.GlyphCount + 1 };
        Append(ref _glyphs, ref _glyphCount, entry);
    }

    /// <summary>
    /// Selects the glyph a shown code draws: the program (embedded, else the substitute), the glyph id in it, and the horizontal scale
    /// that fits a substitute's glyph to the PDF width; or, for a code the substitute draws as several glyphs, those glyphs and the
    /// scale that fits them together to the PDF width.
    /// </summary>
    private FontProgram? SelectGlyph(PdfFont font, in GlyphEvent glyph, out int glyphId, out double scale, out int[]? components)
    {
        scale = 1;
        components = null;
        if (!ReferenceEquals(font, _programFont))
        {
            _programFont = font;
            (_program, _substitute) = font switch
            {
                PdfType0Font { DescendantFont: { } descendant } => descendant.Program is { } embedded ? (embedded, null) : (descendant.Substitute?.Program, descendant.Substitute),
                PdfType0Font => (null, null),
                PdfSimpleFont simple => simple.Program is { } embedded ? (embedded, null) : (simple.Substitute?.Program, simple.Substitute),
                _ => (null, null),
            };
        }

        double width;
        switch (font)
        {
            case PdfType0Font composite:
                CidGlyph cid = composite.ReadGlyph(glyph.SourceBytes[glyph.SourceIndex..]);
                glyphId = cid.GlyphId;
                width = composite.IsVertical ? 0 : cid.Width;
                break;
            case PdfSimpleFont simple when _substitute is not null:
                glyphId = simple.GetSubstituteGlyphId((byte)glyph.CharacterCode, out components);
                width = simple.GetWidth((byte)glyph.CharacterCode);
                break;
            case PdfTrueTypeFont trueType:
                glyphId = trueType.GetGlyphId((byte)glyph.CharacterCode);
                width = trueType.GetWidth((byte)glyph.CharacterCode);
                break;
            case PdfType1Font type1:
                glyphId = type1.GetGlyphId((byte)glyph.CharacterCode);
                width = type1.GetWidth((byte)glyph.CharacterCode);
                break;
            default:
                glyphId = 0;
                return null;
        }

        if (_substitute is { } substitute && width > 0)
        {
            double substituteWidth = 0;
            if (components is null)
            {
                substituteWidth = substitute.GetWidth(glyphId);
            }
            else
            {
                foreach (int component in components)
                {
                    substituteWidth += substitute.GetWidth(component);
                }
            }

            if (substituteWidth > 0 && double.IsFinite(width / substituteWidth))
            {
                scale = width / substituteWidth;
            }
        }

        return _program;
    }

    /// <summary>
    /// Returns the index of a glyph's outline in the path table: interned earlier in the build, else copied from the document's glyph
    /// cache (issue #103), else extracted from the program and offered to that cache.
    /// </summary>
    private int InternOutline(FontProgram? program, int glyphId, DocumentCaches caches)
    {
        if (program is null)
        {
            return InternPath(default);
        }

        if (_outlines.TryGetValue((program, glyphId), out int known))
        {
            return known;
        }

        int index;
        if (caches.Glyphs.TryGet((program, glyphId), out GlyphOutlineEntry? cached))
        {
            index = InternPath(cached.Path);
        }
        else
        {
            _outline ??= new GlyphOutline();
            GlyphOutlineStatus status = program.GetOutline(glyphId, _outline);
            PathView path = status == GlyphOutlineStatus.Complete ? _outline.Path : default;
            index = InternPath(path);
            if (GlyphOutlineEntry.BytesOf(path) <= caches.Glyphs.Budget)
            {
                caches.Glyphs.Add((program, glyphId), new GlyphOutlineEntry(path.Verbs.ToArray(), path.Points.ToArray()));
            }
        }

        _outlines[(program, glyphId)] = index;
        return index;
    }

    /// <summary>Whether the glyph can join the run recorded last: nothing came after it and it is painted exactly alike.</summary>
    private bool ContinuesRun(
        FontProgram? program,
        TextRenderingMode mode,
        bool fills,
        bool strokes,
        in GraphicsState state,
        PdfDefaultColorSpaces defaults,
        ColorConversion conversion)
    {
        if (_runOperation < 0 || _runOperation != _operationCount - 1 || state.Ctm != _current)
        {
            return false;
        }

        DisplayGlyphRun run = _glyphRuns[_glyphRunCount - 1];
        if (!ReferenceEquals(run.Program, program) || run.Mode != mode || (run.FillColor >= 0) != fills || (run.Stroke >= 0) != strokes)
        {
            return false;
        }

        if (fills && !SameColor(run.FillColor, state.FillColor, defaults, conversion))
        {
            return false;
        }

        if (strokes)
        {
            DisplayStroke stroke = _strokes[run.Stroke];
            if (!SameColor(stroke.ColorIndex, state.StrokeColor, defaults, conversion)
                || stroke.LineWidth != state.LineWidth
                || stroke.LineCap != state.LineCap
                || stroke.LineJoin != state.LineJoin
                || stroke.MiterLimit != state.MiterLimit
                || stroke.DashPhase != state.DashPhase
                || stroke.StrokeAdjustment != state.StrokeAdjustment
                || !_dashes.AsSpan(stroke.DashStart, stroke.DashCount).SequenceEqual(state.DashArray))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the colour table entry at <paramref name="index"/> is this colour, with these defaults and this conversion.</summary>
    private bool SameColor(int index, in PdfColor color, PdfDefaultColorSpaces defaults, ColorConversion conversion)
    {
        DisplayColorEntry entry = _colors[index];
        return ReferenceEquals(entry.ColorSpace, color.ColorSpace)
            && ReferenceEquals(entry.DefaultColorSpaces, defaults)
            && entry.Conversion == conversion
            && _components.AsSpan(entry.ComponentStart, entry.ComponentCount).SequenceEqual(color.Components);
    }

    /// <summary>Forgets the glyph runs, the interned outlines and the fonts looked up, keeping the tables' capacity.</summary>
    private void ResetText()
    {
        Array.Clear(_glyphRuns, 0, _glyphRunCount);
        _glyphRunCount = 0;
        _glyphCount = 0;
        _runOperation = -1;
        _outlines.Clear();
        _programFont = null;
        _program = null;
        _substitute = null;
    }
}
