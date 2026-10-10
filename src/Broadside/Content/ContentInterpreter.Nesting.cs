using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// Form XObjects (§8.10.1), Type 3 glyph descriptions (§9.6.4) and soft-mask groups (§11.6.5.1) run as nested streams on the shared
/// nested-run core (<see cref="RunNested"/>): one depth budget and one visited set for every kind, an implicit <c>q</c> that sets the
/// stack's floor, and the stream's own operand arena, path, text object and marked-content floor.
/// </summary>
/// <remarks>
/// <para>
/// <c>Do</c> on a form: a form whose <c>OC</c> is off is not run and has no effect; otherwise the form event is reported with the state
/// at <c>Do</c> (a processor may answer <see cref="ContentVisit.Skip"/> and paint the form its own way through
/// <see cref="ContentContext.RunForm"/>), then the content runs from the state at <c>Do</c> with the form matrix concatenated to the CTM
/// and the clip intersected with the bounding box in form space; that state is the stream's start state, so a tiling pattern selected
/// in the form starts its cell from it (§8.7.3.1). Names resolve in the form's resources, else in the invoking stream's (§7.8.3).
/// Inside a transparency group (§11.6.6, p.438) the blend mode, alphas and soft mask start from their initial values; the outer ones
/// are on the form event.
/// </para>
/// <para>
/// Repairs: a missing or malformed <c>BBox</c> does not clip and a malformed <c>Matrix</c> is the identity (<c>ContentFormInvalid</c>);
/// a form that would run inside itself is not run again (<c>ContentFormCycle</c>) and one nested deeper than
/// <see cref="ContentOptions.MaxNestingDepth"/> is not run (<c>ContentNestingTooDeep</c>); neither is reported as a form event.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private static readonly CosName StructParentsName = new("StructParents");
    private static readonly CosName TransparencyName = new("Transparency");
    private static readonly CosName GroupSubtypeName = new("S");
    private static readonly CosName IsolatedName = new("I");
    private static readonly CosName KnockoutName = new("K");
    private static readonly CosName GroupColorSpaceName = new("CS");
    private static readonly CosName MatrixName = new("Matrix");
    private static readonly CosName RefName = new("Ref");

    /// <summary>Prepares the per-run state of text, marked content and optional content for a top-level run.</summary>
    private void BeginNesting(PdfDocument document, PdfPage? page, ContentOptions options)
    {
        _ = page;
        _clipGlyphCount = 0;
        _textClipStart = 0;
        _textMatricesValid = false;
        _textMatrix = _lineMatrix = Matrix.Identity;
        BeginMarkedContentTracking(document, options);
    }

    /// <summary>Forgets what the run held on to, so the cached interpreter keeps no document objects alive.</summary>
    private void EndNesting()
    {
        _clipGlyphs.AsSpan(0, _clipGlyphCount).Clear();
        _clipGlyphCount = 0;
        EndMarkedContentTracking();
    }

    /// <summary><c>Do</c> with a form XObject (§8.10.1), or <see cref="ContentContext.RunForm"/> with <paramref name="processor"/>.</summary>
    private void PaintForm(PdfFormXObject form, ReadOnlySpan<byte> name, int offset, ContentProcessor processor)
    {
        CosStream stream = form.Stream;
        CosDictionary dictionary = stream.Dictionary;
        if (_tracker is { } tracker && !tracker.IsObjectVisible(dictionary))
        {
            // §8.11.3.3: a form whose OC is off is not drawn at all, and has no effect on the state.
            return;
        }

        if (CanNest(stream) is not NestedRunResult.Ran and var refused)
        {
            ReportNesting(refused, offset);
            return;
        }

        PdfDocument document = _context.Document;
        Matrix matrix = form.Matrix;
        if (dictionary.ContainsKey(MatrixName) && Annotations.AnnotationValues.ReadMatrix(document, dictionary[MatrixName]) is null)
        {
            Report(ContentIssue.FormInvalid, offset, "A form XObject's Matrix is not an array of six numbers; the identity is used.");
        }

        PdfRectangle? box = form.BoundingBox;
        if (box is null)
        {
            Report(ContentIssue.FormInvalid, offset, "A form XObject has no usable BBox (four numbers); its content is not clipped.");
        }

        CosDictionary? group = form.Group;
        bool transparency = group is not null && TransparencyName.Equals(document.Resolve(group.TryGetValue(GroupSubtypeName, out CosObject? s) ? s : null));
        CosDictionary? resources = form.Resources ?? _context.Resources;
        ref readonly GraphicsState outer = ref State;
        var formEvent = new FormEvent
        {
            Stream = stream,
            Reference = form.Reference,
            Form = form,
            ResourceName = name,
            Matrix = matrix,
            BoundingBox = box ?? default,
            HasBoundingBox = box is not null,
            Group = group,
            IsTransparencyGroup = transparency,
            IsIsolated = transparency && Flag(group!, IsolatedName),
            IsKnockout = transparency && Flag(group!, KnockoutName),
            GroupColorSpace = transparency && group!.TryGetValue(GroupColorSpaceName, out CosObject? space) ? document.ColorSpaces.Get(space, form.Reference) : null,
            BlendMode = outer.BlendMode,
            StrokeAlpha = outer.StrokeAlpha,
            FillAlpha = outer.FillAlpha,
            SoftMask = outer.SoftMask,
            SoftMaskMatrix = outer.SoftMaskMatrix,
            AlphaIsShape = outer.AlphaIsShape,
            ReferenceDictionary = document.Resolve(dictionary.TryGetValue(RefName, out CosObject? reference) ? reference : null) as CosDictionary,
            Resources = resources,
            StructParent = form.StructParent,
            StructParents = form.StructParents,
            Depth = _context.Depth + 1,
            IsHidden = _context.IsHidden,
        };

        bool reportsForms = (processor.Events & ContentEvents.Forms) != 0;
        if (reportsForms && processor.BeginForm(formEvent, _context) == ContentVisit.Skip)
        {
            return;
        }

        GraphicsState initial = State;
        initial.Ctm = matrix * initial.Ctm;
        if (transparency)
        {
            initial.BlendMode = BlendMode.Normal;
            initial.StrokeAlpha = 1;
            initial.FillAlpha = 1;
            initial.SoftMask = null;
            initial.SoftMaskMatrix = Matrix.Identity;
        }

        var run = new NestedRun
        {
            Kind = transparency ? ContentRunKind.Group : ContentRunKind.Form,
            Identity = stream,
            Reference = form.Reference,
            Content = document.ContentResources.GetContent(stream),
            Resources = resources,
            InitialState = initial,
            ClipBox = box,
            Processor = processor,
            StructParents = form.StructParents,
        };
        ReportNesting(RunNested(run), offset);
        if (reportsForms)
        {
            processor.EndForm(formEvent, _context);
        }
    }

    /// <summary>Runs a Type 3 glyph's description (§9.6.4) for a processor that entered it: glyph space is the font matrix times the glyph's text matrix.</summary>
    private void RunType3Glyph(in GlyphEvent glyph, PdfType3Font font, byte code, ContentProcessor processor)
    {
        if (processor.BeginType3Glyph(glyph, _context) == ContentVisit.Skip)
        {
            return;
        }

        if (font.GetCharProc(code) is { } procedure)
        {
            GraphicsState initial = State;
            initial.Ctm = font.FontMatrix * glyph.TextMatrix * glyph.Ctm;
            var run = new NestedRun
            {
                Kind = ContentRunKind.Type3Glyph,
                Identity = procedure,
                Content = _context.Document.ContentResources.GetContent(procedure),
                Resources = font.Resources ?? _context.Resources,
                InitialState = initial,
                Processor = processor,
            };
            ReportNesting(RunNested(run), -1);
        }

        processor.EndType3Glyph(glyph, _context);
    }

    /// <summary><see cref="ContentContext.RunForm"/>: the form's events go to another processor.</summary>
    internal void RunFormWith(PdfFormXObject form, ContentProcessor processor)
    {
        EnsureRunning();
        PaintForm(form, [], -1, processor);
    }

    /// <summary><see cref="ContentContext.RunType3Glyph"/>.</summary>
    internal void RunType3GlyphWith(in GlyphEvent glyph, PdfType3Font font, ContentProcessor processor)
    {
        EnsureRunning();
        RunType3Glyph(glyph, font, (byte)glyph.CharacterCode, processor);
    }

    /// <summary>
    /// <see cref="ContentContext.RunSoftMaskGroup"/>: the mask's group from the initial graphics state (within the current clip), in the
    /// coordinate system of the CTM at the <c>gs</c> that set the mask concatenated with the group's matrix (§11.6.5.1).
    /// </summary>
    internal void RunSoftMaskGroupWith(ContentProcessor processor)
    {
        EnsureRunning();
        if (State.SoftMask?.Group is not { } group)
        {
            return;
        }

        GraphicsState initial = GraphicsState.CreateInitial();
        initial.Ctm = group.Matrix * State.SoftMaskMatrix;
        initial.ClipHandle = State.ClipHandle;
        var run = new NestedRun
        {
            Kind = ContentRunKind.SoftMask,
            Identity = group.Stream,
            Reference = group.Reference,
            Content = _context.Document.ContentResources.GetContent(group.Stream),
            Resources = group.Resources ?? _context.Resources,
            InitialState = initial,
            ClipBox = group.BoundingBox,
            Processor = processor,
            StructParents = group.StructParents,
        };
        ReportNesting(RunNested(run), -1);
    }

    /// <summary>Reports why a form, glyph or soft-mask group did not run.</summary>
    private void ReportNesting(NestedRunResult result, int offset)
    {
        if (result == NestedRunResult.Recursive)
        {
            Report(ContentIssue.FormCycle, offset, "A form XObject, glyph description or soft-mask group would run inside itself; it is not run again.");
        }
        else if (result == NestedRunResult.TooDeep)
        {
            Report(ContentIssue.NestingTooDeep, offset, "Form XObjects, Type 3 glyphs, patterns or soft masks nest deeper than the limit; the innermost is not run.");
        }
    }

    private void EnsureRunning()
    {
        if (_processor is null || !_busy)
        {
            throw new InvalidOperationException("Content can be run through the context only during a callback of a running run.");
        }
    }

    private bool Flag(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && _context.Document.Resolve(value) is CosBoolean { Value: true };
}
