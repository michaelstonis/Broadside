using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// Type 3 glyphs (ISO 32000-2 §9.6.4): each glyph description is interpreted once per font, code and inherited graphics state in a
/// build, its operations kept as a sub-list, and every show of the glyph replays that sub-list into the list under the show's glyph
/// transform, a <c>d1</c> glyph in the colours of the text that shows it.
/// </summary>
/// <remarks>
/// <para>
/// A sub-list is the run of operations the glyph's nested run recorded, in the default user space of the show that built it. A later
/// show maps every transform it sets (and every shading's target space) by <c>B⁻¹ × B'</c>, where B and B' are the glyph spaces
/// (FontMatrix × text space × CTM) of the building show and of this show, so it draws exactly what running the description again
/// would. Paths, images, masks and glyph outlines are shared, not copied.
/// </para>
/// <para>
/// §9.6.4: aside from the CTM, a description inherits the graphics state of the text that shows it, so a sub-list is reused only
/// for a show whose inherited state is the same; Table 111: a <c>d1</c> glyph's colour is the text's at each show, so its sub-list
/// is reused whatever the colours and its fills, strokes, stencil masks and glyph runs take the colours of the show. A show the
/// builder cannot map (a singular glyph space) runs the description in place, uncached, like a form.
/// </para>
/// </remarks>
internal sealed partial class DisplayListBuilder
{
    // Descriptions built in this build: (font, code) -> index of the newest entry for it; older entries chain through Next.
    private readonly Dictionary<(PdfType3Font Font, byte Code), int> _type3Index = [];
    private Type3Description[] _type3 = new Type3Description[8];
    private int _type3Count;

    // The sub-lists' operations.
    private DisplayOperation[] _type3Operations = new DisplayOperation[32];
    private int _type3OperationCount;

    // What the builder tracks as the backend's compositing while a description records: equal to no real compositing, so the first
    // paint of the sub-list records its own.
    private static readonly DisplayCompositing UnknownCompositing = new(double.NaN, double.NaN, BlendMode.Normal);

    // The glyphs whose descriptions are running, innermost last.
    private Type3Frame[] _type3Frames = new Type3Frame[4];
    private int _type3FrameCount;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §9.6.4: a glyph whose description was built earlier in this list with the same inherited graphics state is
    /// replayed under this show's glyph transform and not run again; otherwise its description runs and is recorded. A code with no
    /// glyph description paints nothing (§9.6.4 step b).
    /// </remarks>
    public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
        Type3GlyphKind kind = glyph.Type3Metrics.Kind;
        if (glyph.Font is not PdfType3Font font || kind == Type3GlyphKind.None)
        {
            return ContentVisit.Skip;
        }

        _runOperation = -1;
        byte code = (byte)glyph.CharacterCode;
        ref readonly GraphicsState state = ref context.State;
        // In the space this builder records in (pattern space inside a tiling cell, #112), like every matrix in a sub-list.
        Matrix glyphSpace = Relative(font.FontMatrix * glyph.TextMatrix * glyph.Ctm);
        bool shapeOnly = kind == Type3GlyphKind.ShapeOnly;
        if (!glyphSpace.TryInvert(out _))
        {
            // No later show could be mapped from this one: run it in place.
            PushType3Frame(new Type3Frame { Records = false, ShapeOnly = shapeOnly });
            return ContentVisit.Enter;
        }

        bool patterned = state.FillColor.ColorSpace.Family == PdfColorSpaceFamily.Pattern || state.StrokeColor.ColorSpace.Family == PdfColorSpaceFamily.Pattern;
        var key = new Type3Key(
            font.Resources ?? context.Resources,
            context.DefaultColorSpaces,
            Recolors: shapeOnly && !patterned,
            SameSpaceOnly: patterned,
            ColorsLocked: InsideShapeOnlyGlyph());

        if (_type3Index.TryGetValue((font, code), out int candidate))
        {
            for (; candidate >= 0; candidate = _type3[candidate].Next)
            {
                ref readonly Type3Description description = ref _type3[candidate];
                if (description.Key == key
                    && (!key.SameSpaceOnly || description.GlyphSpace == glyphSpace)
                    && SameInheritedState(description.State, state, compareColors: !key.Recolors, compareText: description.ShowsText))
                {
                    ReplayType3(candidate, glyphSpace, context);
                    return ContentVisit.Skip;
                }
            }
        }

        PushType3Frame(new Type3Frame
        {
            Records = true,
            Font = font,
            Code = code,
            Start = _operationCount,
            SavedTransform = _current,
            SavedCompositing = _compositing,
            GlyphSpace = glyphSpace,
            ShapeOnly = shapeOnly,
            Key = key,
            State = state,
        });

        // The sub-list sets every transform and compositing (#113) it paints with, whatever the backend holds where it is replayed;
        // ReplayType3 drops those equal to what the backend holds there.
        _current = new Matrix(double.NaN, 0, 0, 0, 0, 0);
        _compositing = UnknownCompositing;
        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §9.6.4: the recorded description becomes the glyph's sub-list and is replayed at the show that built it. A
    /// description that did not run (refused as recursive or too deep, or empty) is not kept.
    /// </remarks>
    public override void EndType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
        if (_type3FrameCount == 0)
        {
            return;
        }

        Type3Frame frame = _type3Frames[--_type3FrameCount];
        _type3Frames[_type3FrameCount] = default;
        _runOperation = -1;
        if (!frame.Records)
        {
            return;
        }

        int start = frame.Start;
        int count = _operationCount - start;
        _operationCount = start;
        _current = frame.SavedTransform;
        _compositing = frame.SavedCompositing;
        if (!frame.Ran)
        {
            return;
        }

        // The glyph's own save and restore can go when nothing between them outlives them (no clip, nothing unknown).
        ReadOnlySpan<DisplayOperation> recorded = _operations.AsSpan(start, count);
        if (EnclosedOnlyByOwnState(recorded))
        {
            recorded = recorded[1..^1];
        }

        EnsureCapacity(ref _type3Operations, _type3OperationCount + recorded.Length);
        recorded.CopyTo(_type3Operations.AsSpan(_type3OperationCount));
        int index = _type3Count;
        int next = _type3Index.TryGetValue((frame.Font!, frame.Code), out int newest) ? newest : -1;
        Append(ref _type3, ref _type3Count, new Type3Description
        {
            Next = next,
            OperationStart = _type3OperationCount,
            OperationCount = recorded.Length,
            GlyphSpace = frame.GlyphSpace,
            Key = frame.Key,
            ShowsText = frame.ShowsText,
            State = frame.State,
        });
        _type3OperationCount += recorded.Length;
        _type3Index[(frame.Font!, frame.Code)] = index;
        ReplayType3(index, frame.GlyphSpace, context);
    }

    /// <summary>Notes a glyph shown inside running Type 3 descriptions: their sub-lists then depend on the inherited text state.</summary>
    private void NoteType3Text()
    {
        for (int index = 0; index < _type3FrameCount; index++)
        {
            _type3Frames[index].ShowsText = true;
        }
    }

    /// <summary>Notes the first save of the innermost running description: its nested run started (§9.6.4 step c).</summary>
    private void NoteType3Save()
    {
        if (_type3FrameCount > 0)
        {
            _type3Frames[_type3FrameCount - 1].Ran = true;
        }
    }

    /// <summary>
    /// Replays a sub-list into the list under <paramref name="glyphSpace"/>: transforms and shading spaces mapped from the glyph
    /// space it was built in, and, for a <c>d1</c> glyph, every paint in the current colours (Table 111).
    /// </summary>
    private void ReplayType3(int index, in Matrix glyphSpace, ContentContext context)
    {
        Type3Description description = _type3[index];
        bool moved = glyphSpace != description.GlyphSpace;
        Matrix delta = Matrix.Identity;
        if (moved && description.GlyphSpace.TryInvert(out Matrix inverse))
        {
            delta = inverse * glyphSpace;
        }

        bool recolors = description.Key.Recolors;
        int fill = -1;
        int stroke = -1;
        for (int k = description.OperationStart; k < description.OperationStart + description.OperationCount; k++)
        {
            DisplayOperation operation = _type3Operations[k];
            switch (operation.Kind)
            {
                case DisplayOperationKind.SaveState:
                    SaveState(context);
                    break;
                case DisplayOperationKind.RestoreState:
                    RestoreState(context);
                    break;
                case DisplayOperationKind.SetTransform:
                    // Already in the space this builder records in (Relative), as the glyph spaces are, so not mapped again.
                    Matrix transform = _transforms[operation.First];
                    SetRecordedTransform(moved ? transform * delta : transform);
                    break;
                case DisplayOperationKind.SetCompositing:
                    SetCompositing(_compositings[operation.First]);
                    break;
                case DisplayOperationKind.FillPath or DisplayOperationKind.FillImageMask when recolors:
                    AddOperation(operation with { Second = TextFill(ref fill, context) });
                    break;
                case DisplayOperationKind.StrokePath when recolors:
                    AddOperation(operation with { Second = Recolor(operation.Second, TextStroke(ref stroke, context)) });
                    break;
                case DisplayOperationKind.DrawGlyphs when recolors:
                    DisplayGlyphRun run = _glyphRuns[operation.First];
                    run = run with
                    {
                        FillColor = run.FillColor >= 0 ? TextFill(ref fill, context) : -1,
                        Stroke = run.Stroke >= 0 ? Recolor(run.Stroke, TextStroke(ref stroke, context)) : -1,
                    };
                    AddOperation(operation with { First = _glyphRunCount });
                    Append(ref _glyphRuns, ref _glyphRunCount, run);
                    break;
                case DisplayOperationKind.DrawShading when moved:
                    AddOperation(operation with { First = Move(_shadingPaints[operation.First], delta) });
                    break;
                case DisplayOperationKind.FillPathWithShading when moved:
                    AddOperation(operation with { Second = Move(_shadingPaints[operation.Second], delta) });
                    break;
                case DisplayOperationKind.StrokePathWithShading when moved:
                    AddOperation(operation with { Second = Recolor(operation.Second, Move(_shadingPaints[_strokes[operation.Second].ColorIndex], delta)) });
                    break;
                case DisplayOperationKind.FillPathWithTilingPattern when moved:
                    AddOperation(operation with { Second = Move(_tilingPaints[operation.Second], delta) });
                    break;
                case DisplayOperationKind.StrokePathWithTilingPattern when moved:
                    AddOperation(operation with { Second = Recolor(operation.Second, Move(_tilingPaints[_strokes[operation.Second].ColorIndex], delta)) });
                    break;
                default:
                    AddOperation(operation);
                    break;
            }
        }

        _runOperation = -1;

    }

    /// <summary>Returns the colour-table index of the text's nonstroking colour, adding it the first time a replay needs it.</summary>
    private int TextFill(ref int cached, ContentContext context)
    {
        if (cached < 0)
        {
            ref readonly GraphicsState state = ref context.State;
            cached = AddColor(state.FillColor, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        }

        return cached;
    }

    /// <summary>Returns the colour-table index of the text's stroking colour, adding it the first time a replay needs it.</summary>
    private int TextStroke(ref int cached, ContentContext context)
    {
        if (cached < 0)
        {
            ref readonly GraphicsState state = ref context.State;
            cached = AddColor(state.StrokeColor, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        }

        return cached;
    }

    /// <summary>Returns a stroke record with the line parameters of record <paramref name="stroke"/> and another colour (or shading paint) index.</summary>
    private int Recolor(int stroke, int colorIndex)
    {
        if (_strokes[stroke].ColorIndex == colorIndex)
        {
            return stroke;
        }

        DisplayStroke recolored = _strokes[stroke] with { ColorIndex = colorIndex };
        if (_strokeCount == 0 || _strokes[_strokeCount - 1] != recolored)
        {
            Append(ref _strokes, ref _strokeCount, recolored);
        }

        return _strokeCount - 1;
    }

    /// <summary>Returns a shading paint like <paramref name="paint"/> whose target space is mapped by <paramref name="delta"/>.</summary>
    private int Move(in DisplayShadingPaint paint, in Matrix delta)
    {
        Append(ref _shadingPaints, ref _shadingPaintCount, paint with { Matrix = paint.Matrix * delta });
        return _shadingPaintCount - 1;
    }

    /// <summary>Returns a tiling paint like <paramref name="paint"/> (the same cell) whose pattern space is mapped by <paramref name="delta"/>.</summary>
    private int Move(in DisplayTilingPaint paint, in Matrix delta)
    {
        Append(ref _tilingPaints, ref _tilingPaintCount, paint with { Matrix = paint.Matrix * delta });
        return _tilingPaintCount - 1;
    }

    /// <summary>
    /// Whether <paramref name="operations"/> is one save, a restore matching it last, and between them nothing that changes the
    /// state those two keep but saves and restores of its own: transforms, compositing and paints only (the builder tracks the
    /// transform and the compositing, and replays both through that tracking).
    /// </summary>
    private static bool EnclosedOnlyByOwnState(ReadOnlySpan<DisplayOperation> operations)
    {
        if (operations.Length < 2 || operations[0].Kind != DisplayOperationKind.SaveState || operations[^1].Kind != DisplayOperationKind.RestoreState)
        {
            return false;
        }

        int depth = 0;
        for (int index = 1; index < operations.Length - 1; index++)
        {
            switch (operations[index].Kind)
            {
                case DisplayOperationKind.SaveState:
                    depth++;
                    break;
                case DisplayOperationKind.RestoreState:
                    if (--depth < 0)
                    {
                        return false;
                    }

                    break;
                case DisplayOperationKind.SetTransform or DisplayOperationKind.SetCompositing or DisplayOperationKind.FillPath
                    or DisplayOperationKind.StrokePath or DisplayOperationKind.DrawImage or DisplayOperationKind.DrawGlyphs
                    or DisplayOperationKind.DrawShading or DisplayOperationKind.FillPathWithShading or DisplayOperationKind.StrokePathWithShading
                    or DisplayOperationKind.FillImageMask or DisplayOperationKind.FillPathWithTilingPattern
                    or DisplayOperationKind.StrokePathWithTilingPattern:
                    break;
                default:
                    if (depth == 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return depth == 0;
    }

    /// <summary>Whether a running description declared with <c>d1</c> encloses this point, so colour operators are ignored here (§8.6.8).</summary>
    private bool InsideShapeOnlyGlyph()
    {
        for (int index = 0; index < _type3FrameCount; index++)
        {
            if (_type3Frames[index].ShapeOnly)
            {
                return true;
            }
        }

        return false;
    }

    private void PushType3Frame(in Type3Frame frame) => Append(ref _type3Frames, ref _type3FrameCount, frame);

    /// <summary>
    /// Whether two graphics states give a glyph description the same inherited state (§9.6.4): everything but the CTM and the
    /// clip, the colours unless the show recolours them, the text state only when the description shows text.
    /// </summary>
    private static bool SameInheritedState(in GraphicsState built, in GraphicsState shown, bool compareColors, bool compareText) =>
        built.LineWidth == shown.LineWidth
        && built.LineCap == shown.LineCap
        && built.LineJoin == shown.LineJoin
        && built.MiterLimit == shown.MiterLimit
        && built.DashPhase == shown.DashPhase
        && built.DashArray.SequenceEqual(shown.DashArray)
        && built.BlackPointCompensation == shown.BlackPointCompensation
        && built.RenderingIntent == shown.RenderingIntent
        && built.Flatness == shown.Flatness
        && built.StrokeAdjustment == shown.StrokeAdjustment
        && built.BlendMode == shown.BlendMode
        && built.StrokeAlpha == shown.StrokeAlpha
        && built.FillAlpha == shown.FillAlpha
        && built.AlphaIsShape == shown.AlphaIsShape
        && built.TextKnockout == shown.TextKnockout
        && built.StrokeOverprint == shown.StrokeOverprint
        && built.FillOverprint == shown.FillOverprint
        && built.OverprintMode == shown.OverprintMode
        && ReferenceEquals(built.SoftMask, shown.SoftMask)
        && built.SoftMaskMatrix == shown.SoftMaskMatrix
        && ReferenceEquals(built.BlackGeneration, shown.BlackGeneration)
        && ReferenceEquals(built.UndercolorRemoval, shown.UndercolorRemoval)
        && ReferenceEquals(built.TransferFunction, shown.TransferFunction)
        && ReferenceEquals(built.Halftone, shown.Halftone)
        && built.HalftoneOrigin == shown.HalftoneOrigin
        && built.Smoothness == shown.Smoothness
        && (!compareColors || (built.FillColor.Equals(shown.FillColor) && built.StrokeColor.Equals(shown.StrokeColor)))
        && (!compareText
            || (ReferenceEquals(built.Font, shown.Font)
                && built.FontSize == shown.FontSize
                && built.CharacterSpacing == shown.CharacterSpacing
                && built.WordSpacing == shown.WordSpacing
                && built.HorizontalScaling == shown.HorizontalScaling
                && built.Leading == shown.Leading
                && built.TextRenderingMode == shown.TextRenderingMode
                && built.TextRise == shown.TextRise));

    /// <summary>Forgets the sub-lists and the descriptions running, releasing what they referenced.</summary>
    private void ResetType3()
    {
        _type3Index.Clear();
        Array.Clear(_type3, 0, _type3Count);
        _type3Count = 0;
        _type3OperationCount = 0;
        Array.Clear(_type3Frames, 0, _type3FrameCount);
        _type3FrameCount = 0;
    }

    /// <summary>What a sub-list was built under besides the graphics state: it is reused only under the same.</summary>
    /// <param name="Resources">The resources the description's names resolve in (the font's, else the showing stream's).</param>
    /// <param name="Defaults">The default colour spaces of the showing stream.</param>
    /// <param name="Recolors">Whether the glyph is a <c>d1</c> shape painted in the colours of each show.</param>
    /// <param name="SameSpaceOnly">Whether a pattern colour is inherited, whose pattern space is fixed on the page, not in the glyph.</param>
    /// <param name="ColorsLocked">Whether the glyph is shown inside a <c>d1</c> description, where colour operators are ignored.</param>
    private readonly record struct Type3Key(CosDictionary? Resources, PdfDefaultColorSpaces Defaults, bool Recolors, bool SameSpaceOnly, bool ColorsLocked);

    /// <summary>A built glyph description: its sub-list and what it was built under.</summary>
    private struct Type3Description
    {
        public int Next;
        public int OperationStart;
        public int OperationCount;
        public Matrix GlyphSpace;
        public Type3Key Key;
        public bool ShowsText;
        public GraphicsState State;
    }

    /// <summary>A glyph description running: where its operations start and what to restore and keep when it ends.</summary>
    private struct Type3Frame
    {
        public bool Records;
        public bool Ran;
        public bool ShowsText;
        public bool ShapeOnly;
        public PdfType3Font? Font;
        public byte Code;
        public int Start;
        public Matrix SavedTransform;
        public DisplayCompositing SavedCompositing;
        public Matrix GlyphSpace;
        public Type3Key Key;
        public GraphicsState State;
    }
}
