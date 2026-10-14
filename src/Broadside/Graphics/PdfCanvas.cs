using System.Globalization;
using Broadside.Content;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside.Graphics;

/// <summary>
/// Draws on a page or a form XObject: paths, fills, strokes, clips, transforms, line attributes, dash patterns, colours and
/// transparency, written as content stream operators. Dispose it to store what was drawn.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.2 (graphics objects), §8.4 (graphics state), §8.5 (path construction and painting) and §8.6 (colour spaces). Get a
/// canvas with <see cref="PdfPage.CreateCanvas"/> or <see cref="PdfFormXObject.CreateCanvas"/>. Each call writes the operator it
/// names; the canvas keeps the order Figure 9 of §8.2 requires: a path object is begun with <see cref="MoveTo"/> or
/// <see cref="Rectangle"/>, continued with path construction, optionally marked as a clip with <see cref="Clip"/>, and ended by
/// exactly one painting call; any other call while a path is being built throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Resources are added to the page's or form's resource dictionary as they are used, each under a name the dictionary does not have
/// yet; using the same colour space, graphics state parameters or form again reuses its name (§7.8.3). Graphics state parameter
/// dictionaries are written once per document for the same values. Using a feature records the PDF version it needs (ADR 0003): a
/// save raises the document's version to it, or refuses in strict mode.
/// </para>
/// <para>
/// <see cref="Dispose"/> ends a path still being built (<c>n</c>), restores every state still saved (<c>Q</c>), and stores the
/// content: on a page, as a new content stream after the page's existing content, which is wrapped in <c>q</c> and <c>Q</c> so the
/// state it leaves does not reach the new content; on a form, appended to the form's own stream the same way. The stream is written
/// unfiltered; a save compresses it (<see cref="PdfSaveOptions.StreamCompression"/>). A canvas is a mutation of its document: no
/// read may run concurrently with it, and it is not thread-safe.
/// </para>
/// </remarks>
public sealed partial class PdfCanvas : IDisposable
{
    private static readonly CosName[] BlendModeNames = [.. Enum.GetNames<BlendMode>().Select(static name => new CosName(name))];
    private static readonly CosName[] IntentNames = [.. Enum.GetNames<RenderingIntent>().Select(static name => new CosName(name))];

    private readonly PdfDocument _document;
    private readonly PdfPage? _page;
    private readonly PdfFormXObject? _form;
    private readonly CanvasResources _resources;
    private readonly ContentWriter _writer = new();
    private readonly Stack<ColorState> _saved = new();
    private ColorState _colors;
    private PathState _path;
    private bool _disposed;

    internal PdfCanvas(PdfDocument document, PdfPage page)
    {
        _document = document;
        _page = page;
        if (page.Resources is not { } resources)
        {
            resources = [];
            page.Dictionary[KnownNames.Resources] = resources;
        }

        _resources = new CanvasResources(document, resources);
    }

    internal PdfCanvas(PdfDocument document, PdfFormXObject form)
    {
        _document = document;
        _form = form;
        if (form.Resources is not { } resources)
        {
            resources = [];
            form.Dictionary[KnownNames.Resources] = resources;
        }

        _resources = new CanvasResources(document, resources);
    }

    private enum PathState
    {
        None,
        Open,
        Clipped,
    }

    /// <summary>Gets the number of graphics states saved with <see cref="SaveState"/> and not yet restored.</summary>
    public int StateDepth => _saved.Count;

    /// <summary>Saves the graphics state (<c>q</c>).</summary>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.2 and §8.4.4, Table 56. Restore it with <see cref="RestoreState"/>.</remarks>
    public void SaveState()
    {
        ThrowIfNotAtPageLevel();
        _saved.Push(_colors);
        _writer.Operator("q");
    }

    /// <summary>Restores the graphics state saved last (<c>Q</c>).</summary>
    /// <exception cref="InvalidOperationException">No state is saved, a path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.2 and §8.4.4, Table 56: <c>q</c> and <c>Q</c> shall be balanced.</remarks>
    public void RestoreState()
    {
        ThrowIfNotAtPageLevel();
        if (!_saved.TryPop(out ColorState colors))
        {
            throw new InvalidOperationException("No graphics state is saved; RestoreState shall follow SaveState (ISO 32000-2 §8.4.2).");
        }

        _colors = colors;
        _writer.Operator("Q");
    }

    /// <summary>Concatenates <paramref name="matrix"/> to the current transformation matrix (<c>cm</c>).</summary>
    /// <param name="matrix">The transformation, from the new user space to the current one.</param>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.3.4 and §8.4.4, Table 56.</remarks>
    public void Transform(Matrix matrix)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfNotFinite(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        _writer.Number(matrix.A).Number(matrix.B).Number(matrix.C).Number(matrix.D).Number(matrix.E).Number(matrix.F).Operator("cm");
    }

    /// <summary>Moves the origin of user space by (<paramref name="x"/>, <paramref name="y"/>) (<c>1 0 0 1 x y cm</c>).</summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <remarks>ISO 32000-2 §8.3.3 (translation). As <see cref="Transform"/>.</remarks>
    public void Translate(double x, double y) => Transform(Matrix.CreateTranslation(x, y));

    /// <summary>Scales user space (<c>sx 0 0 sy 0 0 cm</c>).</summary>
    /// <param name="x">The horizontal factor.</param>
    /// <param name="y">The vertical factor.</param>
    /// <remarks>ISO 32000-2 §8.3.3 (scaling). As <see cref="Transform"/>.</remarks>
    public void Scale(double x, double y) => Transform(Matrix.CreateScale(x, y));

    /// <summary>Rotates user space counterclockwise by <paramref name="degrees"/> (<c>cos sin −sin cos 0 0 cm</c>).</summary>
    /// <param name="degrees">The angle, in degrees.</param>
    /// <remarks>ISO 32000-2 §8.3.3 (rotation). As <see cref="Transform"/>.</remarks>
    public void Rotate(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        (double sin, double cos) = Math.SinCos(radians);
        Transform(new Matrix(cos, sin, -sin, cos, 0, 0));
    }

    /// <summary>Sets the line width (<c>w</c>).</summary>
    /// <param name="width">The width in user space units; 0 is the thinnest line the device can render.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.3.2 and Table 56.</remarks>
    public void SetLineWidth(double width)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfNegativeOrNotFinite(width, nameof(width));
        _writer.Number(width).Operator("w");
    }

    /// <summary>Sets the line cap style (<c>J</c>).</summary>
    /// <param name="cap">The style.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not a defined style.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.3.3, Table 53.</remarks>
    public void SetLineCap(LineCap cap)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfUndefined(cap);
        _writer.Number((int)cap).Operator("J");
    }

    /// <summary>Sets the line join style (<c>j</c>).</summary>
    /// <param name="join">The style.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="join"/> is not a defined style.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.3.4, Table 54.</remarks>
    public void SetLineJoin(LineJoin join)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfUndefined(join);
        _writer.Number((int)join).Operator("j");
    }

    /// <summary>Sets the miter limit (<c>M</c>).</summary>
    /// <param name="limit">The limit on the ratio of a miter's length to the line width; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1 or not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.3.5: the ratio of the miter length to the line width is at least 1, so a limit below 1 cannot be met.</remarks>
    public void SetMiterLimit(double limit)
    {
        ThrowIfNotAtPageLevel();
        if (!double.IsFinite(limit) || limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The miter limit shall be a finite number of at least 1.");
        }

        _writer.Number(limit).Operator("M");
    }

    /// <summary>Sets the line dash pattern (<c>d</c>); an empty <paramref name="dashArray"/> draws solid lines.</summary>
    /// <param name="dashArray">The lengths of alternating dashes and gaps, in user space units: non-negative and not all zero.</param>
    /// <param name="phase">The distance into the pattern at which the dash starts; non-negative.</param>
    /// <exception cref="ArgumentException">A length or the phase is negative or not finite, or every length is zero.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.3.6: the dash array's elements shall be non-negative numbers, not all zero.</remarks>
    public void SetDashPattern(ReadOnlySpan<double> dashArray, double phase)
    {
        ThrowIfNotAtPageLevel();
        bool allZero = true;
        foreach (double length in dashArray)
        {
            if (!double.IsFinite(length) || length < 0)
            {
                throw new ArgumentException("The dash lengths shall be finite and non-negative.", nameof(dashArray));
            }

            allZero &= length == 0;
        }

        if (!dashArray.IsEmpty && allZero)
        {
            throw new ArgumentException("The dash lengths shall not all be zero (ISO 32000-2 §8.4.3.6).", nameof(dashArray));
        }

        ThrowIfNegativeOrNotFinite(phase, nameof(phase));
        _writer.NumberArray(dashArray);
        _writer.Number(phase).Operator("d");
    }

    /// <summary>Sets the flatness tolerance (<c>i</c>).</summary>
    /// <param name="flatness">0 to 100; 0 selects the device's default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="flatness"/> is outside 0 to 100.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.4, Table 56, and §10.7.2.</remarks>
    public void SetFlatness(double flatness)
    {
        ThrowIfNotAtPageLevel();
        if (!(flatness is >= 0 and <= 100))
        {
            throw new ArgumentOutOfRangeException(nameof(flatness), flatness, "The flatness tolerance shall be in the range 0 to 100.");
        }

        _writer.Number(flatness).Operator("i");
    }

    /// <summary>Sets the colour rendering intent (<c>ri</c>, PDF 1.1).</summary>
    /// <param name="intent">The intent.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="intent"/> is not a defined intent.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.4, Table 56, and §8.6.5.8, Table 69.</remarks>
    public void SetRenderingIntent(RenderingIntent intent)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfUndefined(intent);
        ThrowIfStencil();
        _document.RequireFeature(VersionedFeatures.RenderingIntentOperator);
        _writer.Name(IntentNames[(int)intent]).Operator("ri");
    }

    /// <summary>Sets the colour used for filling (<c>g</c>, <c>rg</c>, <c>k</c>, or <c>cs</c> and <c>sc</c>/<c>scn</c>).</summary>
    /// <param name="color">The colour: a value in a device space, or in a space of this document (<see cref="PdfDocument.GetColorSpace"/>, <see cref="PdfDocument.CreateIccBasedColorSpace"/>, <see cref="PdfDocument.CreateSeparationColorSpace(string, PdfColor)"/>, <see cref="PdfDocument.CreateDeviceNColorSpace(IReadOnlyList{string}, IReadOnlyList{PdfColor})"/>).</param>
    /// <exception cref="ArgumentException">The colour's space belongs to another document, or is a Pattern space; the colour does not have one component per component of its space, or one is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.6.8, Table 73. Device colours use the operator of their family, which also selects the space. Any other space is
    /// named in the <c>ColorSpace</c> resources and selected with <c>cs</c> when it is not the current one; its components are set with
    /// <c>scn</c> (ICCBased, Separation, DeviceN) or <c>sc</c>. The space's minimum version is recorded (Table 61).
    /// </remarks>
    public void SetFillColor(PdfColor color) => SetColor(color, stroking: false);

    /// <summary>Sets the colour used for stroking (<c>G</c>, <c>RG</c>, <c>K</c>, or <c>CS</c> and <c>SC</c>/<c>SCN</c>).</summary>
    /// <param name="color">The colour, as for <see cref="SetFillColor"/>.</param>
    /// <exception cref="ArgumentException">The colour's space belongs to another document, or is a Pattern space; the colour does not have one component per component of its space, or one is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.6.8, Table 73. As <see cref="SetFillColor"/>, for the stroking colour.</remarks>
    public void SetStrokeColor(PdfColor color) => SetColor(color, stroking: true);

    /// <summary>Sets the constant opacity of fills (<c>ca</c> through a graphics state parameter dictionary and <c>gs</c>).</summary>
    /// <param name="alpha">0 (transparent) to 1 (opaque).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is outside 0 to 1.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.5, Table 57 (<c>ca</c>, PDF 1.4), and §11.6.4.4. Raises the document to PDF 1.4.</remarks>
    public void SetFillAlpha(double alpha) => SetAlpha(alpha, "ca");

    /// <summary>Sets the constant opacity of strokes (<c>CA</c> through a graphics state parameter dictionary and <c>gs</c>).</summary>
    /// <param name="alpha">0 (transparent) to 1 (opaque).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is outside 0 to 1.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.5, Table 57 (<c>CA</c>, PDF 1.4), and §11.6.4.4. Raises the document to PDF 1.4.</remarks>
    public void SetStrokeAlpha(double alpha) => SetAlpha(alpha, "CA");

    /// <summary>Sets the blend mode (<c>BM</c> through a graphics state parameter dictionary and <c>gs</c>).</summary>
    /// <param name="mode">The mode.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.4.5, Table 57 (<c>BM</c>, PDF 1.4), and §11.3.5, Tables 134 and 135. Raises the document to PDF 1.4.</remarks>
    public void SetBlendMode(BlendMode mode)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfUndefined(mode);
        SetGraphicsStateParameter("BM", BlendModeNames[(int)mode]);
    }

    /// <summary>Begins a new subpath at (<paramref name="x"/>, <paramref name="y"/>) (<c>m</c>), beginning a path when none is being built.</summary>
    /// <param name="x">The horizontal coordinate, in user space.</param>
    /// <param name="y">The vertical coordinate, in user space.</param>
    /// <exception cref="ArgumentException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The path is already marked as a clip, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.2.1, Table 58.</remarks>
    public void MoveTo(double x, double y)
    {
        ThrowIfCannotConstruct();
        ThrowIfNotFinite(x, y);
        _writer.Number(x).Number(y).Operator("m");
        _path = PathState.Open;
    }

    /// <summary>Appends a straight line to (<paramref name="x"/>, <paramref name="y"/>) (<c>l</c>).</summary>
    /// <param name="x">The horizontal coordinate, in user space.</param>
    /// <param name="y">The vertical coordinate, in user space.</param>
    /// <exception cref="ArgumentException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">No path is being built (no current point), the path is marked as a clip, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.2.1, Table 58.</remarks>
    public void LineTo(double x, double y)
    {
        ThrowIfNoCurrentPoint();
        ThrowIfNotFinite(x, y);
        _writer.Number(x).Number(y).Operator("l");
    }

    /// <summary>Appends a cubic Bézier curve to (<paramref name="x3"/>, <paramref name="y3"/>) with two control points (<c>c</c>).</summary>
    /// <param name="x1">The first control point's horizontal coordinate.</param>
    /// <param name="y1">The first control point's vertical coordinate.</param>
    /// <param name="x2">The second control point's horizontal coordinate.</param>
    /// <param name="y2">The second control point's vertical coordinate.</param>
    /// <param name="x3">The end point's horizontal coordinate.</param>
    /// <param name="y3">The end point's vertical coordinate.</param>
    /// <exception cref="ArgumentException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">No path is being built (no current point), the path is marked as a clip, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.2.2, Table 58.</remarks>
    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        ThrowIfNoCurrentPoint();
        ThrowIfNotFinite(x1, y1, x2, y2, x3, y3);
        _writer.Number(x1).Number(y1).Number(x2).Number(y2).Number(x3).Number(y3).Operator("c");
    }

    /// <summary>Closes the current subpath with a straight line to its start (<c>h</c>).</summary>
    /// <exception cref="InvalidOperationException">No path is being built, the path is marked as a clip, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.2.1, Table 58.</remarks>
    public void ClosePath()
    {
        ThrowIfNoCurrentPoint();
        _writer.Operator("h");
    }

    /// <summary>Appends a rectangle as a complete closed subpath (<c>re</c>), beginning a path when none is being built.</summary>
    /// <param name="x">The lower-left corner's horizontal coordinate.</param>
    /// <param name="y">The lower-left corner's vertical coordinate.</param>
    /// <param name="width">The width (negative extends to the left).</param>
    /// <param name="height">The height (negative extends downwards).</param>
    /// <exception cref="ArgumentException">A value is not finite.</exception>
    /// <exception cref="InvalidOperationException">The path is already marked as a clip, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.2.1, Table 58.</remarks>
    public void Rectangle(double x, double y, double width, double height)
    {
        ThrowIfCannotConstruct();
        ThrowIfNotFinite(x, y, width, height);
        _writer.Rectangle(x, y, width, height);
        _path = PathState.Open;
    }

    /// <summary>Marks the path being built as a clip (<c>W</c> or <c>W*</c>); the painting call that ends the path then narrows the clipping path.</summary>
    /// <param name="rule">The rule that decides what is inside.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rule"/> is not a defined rule.</exception>
    /// <exception cref="InvalidOperationException">No path is being built, it is already marked, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.5.4, Table 60: the clip takes effect after the painting operator, so the path itself is painted unclipped.
    /// <see cref="EndPath"/> narrows the clip without painting.
    /// </remarks>
    public void Clip(FillRule rule = FillRule.NonZero)
    {
        ThrowIfDisposed();
        ThrowIfUndefined(rule);
        if (_path != PathState.Open)
        {
            throw new InvalidOperationException(_path == PathState.None
                ? "No path is being built; build one before marking it as a clip (ISO 32000-2 §8.5.4)."
                : "The path is already marked as a clip.");
        }

        _writer.Operator(rule == FillRule.EvenOdd ? "W*" : "W");
        _path = PathState.Clipped;
    }

    /// <summary>Fills the path (<c>f</c> or <c>f*</c>), closing open subpaths, and ends it.</summary>
    /// <param name="rule">The rule that decides what is inside.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rule"/> is not a defined rule.</exception>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.1 and §8.5.3.3, Table 59.</remarks>
    public void Fill(FillRule rule = FillRule.NonZero)
    {
        ThrowIfUndefined(rule);
        Paint(rule == FillRule.EvenOdd ? "f*" : "f");
    }

    /// <summary>Strokes the path (<c>S</c>) and ends it.</summary>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.2, Table 59.</remarks>
    public void Stroke() => Paint("S");

    /// <summary>Closes the last subpath and strokes the path (<c>s</c>), and ends it.</summary>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.2, Table 59.</remarks>
    public void CloseAndStroke() => Paint("s");

    /// <summary>Fills, then strokes the path (<c>B</c> or <c>B*</c>), and ends it.</summary>
    /// <param name="rule">The rule that decides what is filled.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rule"/> is not a defined rule.</exception>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.1, Table 59.</remarks>
    public void FillAndStroke(FillRule rule = FillRule.NonZero)
    {
        ThrowIfUndefined(rule);
        Paint(rule == FillRule.EvenOdd ? "B*" : "B");
    }

    /// <summary>Closes the last subpath, fills, then strokes the path (<c>b</c> or <c>b*</c>), and ends it.</summary>
    /// <param name="rule">The rule that decides what is filled.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rule"/> is not a defined rule.</exception>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.1, Table 59.</remarks>
    public void CloseFillAndStroke(FillRule rule = FillRule.NonZero)
    {
        ThrowIfUndefined(rule);
        Paint(rule == FillRule.EvenOdd ? "b*" : "b");
    }

    /// <summary>Ends the path without painting it (<c>n</c>): with <see cref="Clip"/>, narrows the clipping path only.</summary>
    /// <exception cref="InvalidOperationException">No path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.5.3.1, Table 59, and §8.5.4.</remarks>
    public void EndPath() => Paint("n");

    /// <summary>Paints a form XObject of this document (<c>Do</c>), with the form's matrix and bounding box.</summary>
    /// <param name="form">The form: <see cref="PdfDocument.CreateFormXObject"/>, or one read from the document.</param>
    /// <exception cref="ArgumentException"><paramref name="form"/> belongs to another document, or has no indirect reference (a form XObject shall be an indirect stream).</exception>
    /// <exception cref="InvalidOperationException">A path is being built, the form is the one this canvas draws on, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §8.8 (<c>Do</c>, Table 86) and §8.10.1. The form is named in the <c>XObject</c> resources.</remarks>
    public void DrawForm(PdfFormXObject form)
    {
        ArgumentNullException.ThrowIfNull(form);
        ThrowIfNotAtPageLevel();
        if (form.Reference is not { } reference)
        {
            throw new ArgumentException("A form XObject drawn with Do shall be an indirect object (ISO 32000-2 §8.10.1).", nameof(form));
        }

        if (!ReferenceEquals(form.Document, _document))
        {
            throw new ArgumentException("The form belongs to another document.", nameof(form));
        }

        if (_form is not null && ReferenceEquals(_form.Stream, form.Stream))
        {
            throw new InvalidOperationException("A form XObject cannot draw itself.");
        }

        _writer.Name(_resources.Name(CanvasResources.XObject, "Fm", reference)).Operator("Do");
    }

    /// <summary>Ends a path still being built, restores every saved state and stores what was drawn; nothing is stored when nothing was drawn.</summary>
    /// <remarks>ISO 32000-2 §7.8.2 and §8.4.2. See the class remarks for where the content goes. Disposing twice does nothing.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_path != PathState.None)
        {
            _writer.Operator("n");
            _path = PathState.None;
        }

        while (_saved.TryPop(out _))
        {
            _writer.Operator("Q");
        }

        if (_writer.Length == 0)
        {
            return;
        }

        if (_page is not null)
        {
            StoreOnPage(_page);
        }
        else
        {
            StoreInStream(_form?.Stream ?? _cell!);
        }
    }

    /// <summary>The colour spaces selected for filling and stroking, saved and restored with the graphics state.</summary>
    private record struct ColorState(PdfColorSpace? Fill, PdfColorSpace? Stroke);

    private static void ThrowIfNotFinite(params ReadOnlySpan<double> values)
    {
        foreach (double value in values)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Coordinates and matrix components shall be finite numbers.", nameof(values));
            }
        }
    }

    private static void ThrowIfNegativeOrNotFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value shall be a finite, non-negative number.");
        }
    }

    private static void ThrowIfUndefined<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Not a defined {typeof(T).Name}.");
        }
    }

    private static bool UsesScn(PdfColorSpaceFamily family) =>
        family is PdfColorSpaceFamily.IccBased or PdfColorSpaceFamily.Separation or PdfColorSpaceFamily.DeviceN;

    private void SetColor(PdfColor color, bool stroking)
    {
        ThrowIfNotAtPageLevel();
        ThrowIfStencil();
        PdfColorSpace space = color.ColorSpace;
        if (space.Family == PdfColorSpaceFamily.Pattern)
        {
            throw new ArgumentException("A Pattern colour is set with SetFillPattern or SetStrokePattern.", nameof(color));
        }

        if (space.Cache is { } cache && !ReferenceEquals(cache.Document, _document))
        {
            throw new ArgumentException("The colour space belongs to another document.", nameof(color));
        }

        if (color.ComponentCount != space.ComponentCount)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The colour has {color.ComponentCount} components; its space has {space.ComponentCount}."),
                nameof(color));
        }

        foreach (float component in color.Components)
        {
            if (!float.IsFinite(component))
            {
                throw new ArgumentException("Colour components shall be finite numbers.", nameof(color));
            }
        }

        string? device = space.Family switch
        {
            PdfColorSpaceFamily.DeviceGray => stroking ? "G" : "g",
            PdfColorSpaceFamily.DeviceRgb => stroking ? "RG" : "rg",
            PdfColorSpaceFamily.DeviceCmyk => stroking ? "K" : "k",
            _ => null,
        };
        if (device is null)
        {
            if (VersionedFeatures.ColorSpace(space) is { } feature)
            {
                _document.RequireFeature(feature);
            }

            PdfColorSpace? current = stroking ? _colors.Stroke : _colors.Fill;
            if (!ReferenceEquals(current, space))
            {
                CosName name = _resources.Name(CanvasResources.ColorSpace, "CS", (CosObject?)space.Reference ?? space.CosObject);
                _writer.Name(name).Operator(stroking ? "CS" : "cs");
            }
        }

        foreach (float component in color.Components)
        {
            _writer.Number(component);
        }

        _writer.Operator(device ?? (UsesScn(space.Family) ? (stroking ? "SCN" : "scn") : (stroking ? "SC" : "sc")));
        _colors = stroking ? _colors with { Stroke = space } : _colors with { Fill = space };
    }

    private void SetAlpha(double alpha, string key)
    {
        ThrowIfNotAtPageLevel();
        if (!(alpha is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "An opacity shall be in the range 0 to 1.");
        }

        SetGraphicsStateParameter(key, new CosReal(alpha));
    }

    private void SetGraphicsStateParameter(string key, CosObject value)
    {
        _document.RequireFeature(VersionedFeatures.GraphicsStateParameters);
        _document.RequireFeature(VersionedFeatures.TransparencyParameters);
        CosReference reference = _document.Drawing.GraphicsState(key, value);
        _writer.Name(_resources.Name(CanvasResources.ExtGState, "GS", reference)).Operator("gs");
    }

    private void Paint(string op)
    {
        ThrowIfDisposed();
        if (_path == PathState.None)
        {
            throw new InvalidOperationException("No path is being built; begin one with MoveTo or Rectangle (ISO 32000-2 §8.2, Figure 9).");
        }

        _writer.Operator(op);
        _path = PathState.None;
    }

    private void StoreOnPage(PdfPage page)
    {
        CosDictionary dictionary = page.Dictionary;
        CosObject? entry = dictionary.TryGetValue(KnownNames.Contents, out CosObject? value) ? value : null;
        CosObject existing = _document.Resolve(entry);
        if (existing is not (CosStream or CosArray { Count: > 0 }))
        {
            dictionary[KnownNames.Contents] = _document.AddObject(new CosStream([], _writer.ToArray()));
            return;
        }

        var parts = new CosArray { _document.AddObject(new CosStream([], "q\n"u8.ToArray())) };
        if (existing is CosArray array)
        {
            foreach (CosObject part in array)
            {
                parts.Add(part);
            }
        }
        else
        {
            parts.Add(entry!);
        }

        byte[] data = new byte[_writer.Length + 2];
        "Q\n"u8.CopyTo(data);
        _writer.Written.CopyTo(data.AsSpan(2));
        parts.Add(_document.AddObject(new CosStream([], data)));
        dictionary[KnownNames.Contents] = parts;
    }

    private void StoreInStream(CosStream stream)
    {
        ReadOnlySpan<byte> existing = _document.DecodeStream(stream).Span;
        if (existing.IsEmpty)
        {
            ContentRewriting.StoreUnfiltered(stream, _writer.Written);
            return;
        }

        var content = new ContentWriter();
        content.Write("q\n"u8);
        content.Write(existing);
        content.Write("\nQ\n"u8);
        content.Write(_writer.Written);
        ContentRewriting.StoreUnfiltered(stream, content.Written);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ThrowIfNotAtPageLevel()
    {
        ThrowIfDisposed();
        if (_path != PathState.None)
        {
            throw new InvalidOperationException(
                "A path is being built: only path construction, Clip and a painting call may follow until it is painted or ended (ISO 32000-2 §8.2, Figure 9).");
        }
    }

    private void ThrowIfCannotConstruct()
    {
        ThrowIfDisposed();
        if (_path == PathState.Clipped)
        {
            throw new InvalidOperationException("The path is marked as a clip: a painting call shall follow (ISO 32000-2 §8.5.4).");
        }
    }

    private void ThrowIfNoCurrentPoint()
    {
        ThrowIfCannotConstruct();
        if (_path == PathState.None)
        {
            throw new InvalidOperationException("There is no current point; begin a path with MoveTo or Rectangle (ISO 32000-2 §8.5.2.1).");
        }
    }
}
