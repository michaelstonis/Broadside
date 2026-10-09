using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Content;

/// <summary>
/// The run a processor is called from: the current graphics state, what is being interpreted, and how to resolve clip handles.
/// One instance serves a whole run; it is valid only during the run's callbacks.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2 (content streams), §7.8.3 (resources), §8.4 (graphics state), §8.3.2 (coordinate spaces).</remarks>
public sealed class ContentContext
{
    private readonly ContentInterpreter _interpreter;

    internal ContentContext(ContentInterpreter interpreter) => _interpreter = interpreter;

    /// <summary>Gets the current graphics state, by read-only reference into the interpreter's stack: copy it to keep it.</summary>
    /// <remarks>ISO 32000-2 §8.4.1.</remarks>
    public ref readonly GraphicsState State => ref _interpreter.State;

    /// <summary>Gets the number of states saved by <c>q</c> (and implicit saves) above the run's initial state.</summary>
    /// <remarks>ISO 32000-2 §8.4.2.</remarks>
    public int StateDepth => _interpreter.StateDepth;

    /// <summary>Gets what kind of content stream is running.</summary>
    public ContentRunKind RunKind { get; internal set; }

    /// <summary>Gets the nesting depth of the running stream: 0 for the top-level run, 1 inside a form it paints, and so on.</summary>
    public int Depth { get; internal set; }

    /// <summary>Gets the document the content belongs to.</summary>
    public PdfDocument Document { get; internal set; } = null!;

    /// <summary>Gets the page being interpreted, or <see langword="null"/> when the run is not over a page.</summary>
    public PdfPage? Page { get; internal set; }

    /// <summary>Gets the resource dictionary names in the running stream resolve against, or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §7.8.3: a page's (inherited) <c>Resources</c>; a form's own, else the page's.</remarks>
    public CosDictionary? Resources { get; internal set; }

    /// <summary>
    /// Gets the CTM at the start of the running stream, the space a pattern named in it is relative to: the identity for a page,
    /// the CTM at <c>Do</c> times the form matrix for a form.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.2: the pattern matrix maps pattern space to the default space of the stream that uses the pattern.</remarks>
    public Matrix StreamBaseMatrix { get; internal set; } = Matrix.Identity;

    /// <summary>Gets a value indicating whether optional content currently hides what is painted.</summary>
    /// <remarks>ISO 32000-2 §8.11.3.1. Always false until optional content is evaluated (issue #76).</remarks>
    public bool IsHidden { get; internal set; }

    /// <summary>
    /// Gets the content stream the current operator is in, or <see langword="null"/> when that stream is a direct object; for a
    /// page whose <c>Contents</c> is an array, the part being read.
    /// </summary>
    public CosReference? CurrentStream => _interpreter.CurrentStream;

    /// <summary>Gets the index in the page's <c>Contents</c> array of the part being read; 0 for a single stream.</summary>
    public int CurrentPartIndex => _interpreter.CurrentPartIndex;

    /// <summary>Gets the token that cancels the run.</summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>
    /// Gets the default colour spaces of the current resources, which replace the device spaces of what is painted now: pass them to
    /// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, PdfDefaultColorSpaces?)"/> with the colour being painted.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.5.6: the look-up uses the resources current when the object is painted, so inside a form XObject the form's
    /// own defaults apply. The initial DeviceGray colour is remapped too.
    /// </remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces => Document.ColorSpaces.GetDefaults(Resources);

    /// <summary>Returns the colour space a value names in the running content stream.</summary>
    /// <param name="colorSpace">
    /// A name, as the operand of <c>CS</c> and <c>cs</c> (DeviceGray, DeviceRGB, DeviceCMYK and Pattern name those spaces; any other
    /// name is looked up in the <c>ColorSpace</c> subdictionary of <see cref="Resources"/>), or a colour space array.
    /// </param>
    /// <returns>The space; DeviceGray, with a diagnostic, when the value names none.</returns>
    /// <remarks>ISO 32000-2 §8.6.8, Table 73; §7.8.3.</remarks>
    public PdfColorSpace GetColorSpace(CosObject colorSpace)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        CosReference? owner = CurrentStream ?? Page?.Reference;
        if (Document.Resolve(colorSpace) is not CosName name)
        {
            return Document.ColorSpaces.Get(colorSpace, owner);
        }

        PdfColorSpace? space = Document.ColorSpaces.FindNamed(Resources, name.Bytes, owner, out NamedLookup lookup);
        ReportLookup(lookup, name);
        return space ?? PdfDeviceGrayColorSpace.Instance;
    }

    /// <summary>Returns the colour space of an inline image's <c>ColorSpace</c> (or <c>CS</c>) entry.</summary>
    /// <param name="colorSpace">The entry's value, such as from <see cref="ContentOperand.ToCosObject"/>.</param>
    /// <returns>The space; DeviceGray, with a diagnostic, when the value names none.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.9.7, Tables 91 and 92: the abbreviations G, RGB, CMYK and I (Indexed, whose base may be abbreviated and whose
    /// lookup table is a string) are allowed, and the full device names; G, RGB, CMYK and the device names never refer to resources,
    /// any other name is a key of the <c>ColorSpace</c> subdictionary of the current resources (PDF 1.2).
    /// </remarks>
    public PdfColorSpace GetInlineImageColorSpace(CosObject colorSpace)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        CosReference? owner = CurrentStream ?? Page?.Reference;
        if (Document.ColorSpaces.FindInline(colorSpace, owner) is { } space)
        {
            return space;
        }

        if (colorSpace is CosName name)
        {
            PdfColorSpace? named = Document.ColorSpaces.FindNamed(Resources, name.Bytes, owner, out NamedLookup lookup);
            ReportLookup(lookup == NamedLookup.Abbreviation ? NamedLookup.Family : lookup, name);
            return named ?? PdfDeviceGrayColorSpace.Instance;
        }

        Document.ColorSpaces.ReportFailure(owner, ColorSpaceFailure.Invalid, "an inline image's colour space");
        return PdfDeviceGrayColorSpace.Instance;
    }

    /// <summary>Returns the pattern a Pattern colour selected, such as <see cref="GraphicsState.FillColor"/> when painting with a pattern.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>
    /// The pattern's model; <see langword="null"/> for a colour that is not a pattern, the initial Pattern colour (which paints
    /// nothing) and an object that is not a pattern.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §8.7.3 and §8.7.4.1. Map pattern space with <see cref="PdfPattern.GetPatternSpace"/>: the pattern matrix followed by
    /// the matrix of the stream that selected the pattern (<see cref="PdfColor.PatternMatrix"/>), never the CTM at the paint (§8.7.2).
    /// A shading pattern gives its <see cref="PdfShadingPattern.Shading"/>, Background and ExtGState; a tiling pattern's cell runs with
    /// <see cref="RunPatternCell"/>.
    /// </remarks>
    public PdfPattern? GetPattern(in PdfColor color) => _interpreter.GetPattern(color);

    /// <summary>
    /// Runs the cell of the tiling pattern a colour selected, reporting its events to <paramref name="processor"/>, in the middle of
    /// the current event: call it from a processor callback, as often as the processor needs (once per pattern and scale, say).
    /// </summary>
    /// <param name="color">The pattern colour, such as <see cref="GraphicsState.FillColor"/> in <see cref="ContentProcessor.PaintPath"/>.</param>
    /// <param name="processor">The processor that receives the cell's events (this run's or another).</param>
    /// <returns>
    /// <see langword="true"/> when the cell ran; <see langword="false"/> when the colour does not select a valid tiling pattern, or
    /// the cell would run inside itself (recorded as <c>ContentPatternRecursion</c>) or deeper than
    /// <see cref="ContentOptions.MaxNestingDepth"/> (<c>ContentNestingTooDeep</c>).
    /// </returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.7.3.1, steps a to d: the cell runs between an implicit save and restore (<see cref="ContentProcessor.SaveState"/>
    /// and <see cref="ContentProcessor.RestoreState"/>), from the graphics state at the beginning of the stream that selected the
    /// pattern, with the CTM set to the pattern space (<see cref="PdfPattern.GetPatternSpace"/>), the clip intersected with the cell's
    /// bounding box (a <see cref="ClipKind.Rectangle"/> clip event), <see cref="RunKind"/> <see cref="ContentRunKind.Pattern"/> and the
    /// pattern's resources (the current ones when it has none). One cell is run, at the origin of pattern space; replicating it every
    /// XStep and YStep is the processor's.
    /// </para>
    /// <para>
    /// §8.7.3.3: for an uncoloured pattern the current colours are the colour given with the pattern, in the underlying colour space,
    /// and colour operators, <c>sh</c> and anything run from the cell that sets colours are ignored with a diagnostic.
    /// </para>
    /// </remarks>
    public bool RunPatternCell(in PdfColor color, ContentProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        if (Document is null)
        {
            throw new InvalidOperationException("A pattern cell can only run during a content run's callbacks.");
        }

        return _interpreter.RunPatternCell(color, processor);
    }

    /// <summary>Returns the clip node a clip handle refers to, such as <see cref="GraphicsState.ClipHandle"/>.</summary>
    /// <param name="handle">The handle; 0 for the run's initial clipping path.</param>
    /// <returns>The node; a node of kind <see cref="ClipKind.Initial"/> for 0 and for a handle this run did not issue.</returns>
    /// <remarks>ISO 32000-2 §8.5.4.</remarks>
    public ClipView GetClip(int handle) => _interpreter.Clips.Get(handle);

    private void ReportLookup(NamedLookup lookup, CosName name)
    {
        CosReference? owner = CurrentStream ?? Page?.Reference;
        if (lookup == NamedLookup.Missing)
        {
            Document.DiagnosticSink.Report(
                DiagnosticCodes.ContentColorSpaceMissing,
                DiagnosticSeverity.Warning,
                $"The colour space /{name.Value} is not in the resources' ColorSpace dictionary; DeviceGray is used.",
                offset: null,
                owner);
        }
        else if (lookup == NamedLookup.Abbreviation)
        {
            Document.DiagnosticSink.Report(
                DiagnosticCodes.ContentColorSpaceAbbreviated,
                DiagnosticSeverity.Warning,
                $"The colour space /{name.Value} is an inline image abbreviation used outside an inline image; it is read as the device space.",
                offset: null,
                owner);
        }
    }
}
