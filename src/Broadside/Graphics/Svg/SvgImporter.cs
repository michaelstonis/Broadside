using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Images;

namespace Broadside.Graphics.Svg;

/// <summary>
/// Draws a parsed SVG document into a form XObject through <see cref="PdfCanvas"/>: structure and viewports, transforms, shapes and
/// paths, solid, gradient and pattern paint, clipping paths, masks, group opacity and blend modes, text and images.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.10 (form XObjects), §8.5 (paths), §8.7.3 (tiling patterns), §8.7.4.5.3 and §8.7.4.5.4 (axial and radial shadings),
/// §11.3.5 (blend modes), §11.6.5 (soft masks), §11.6.6 (transparency groups) and §9.4 (text). SVG 1.1 §5 (structure), §7 (coordinate
/// systems), §8 (paths), §9 (basic shapes), §10 (text), §11 (painting), §13 (gradients and patterns) and §14 (clipping and masking).
/// </para>
/// <para>
/// The form's content first flips to SVG's y-down coordinates in points, then fits the root view box into the form. The importer
/// tracks the transformation from the current user space to the default space of the content stream being written, because
/// pattern matrices (gradients and patterns) are given in that default space (ISO 32000-2 §8.7.2). An element with group opacity,
/// a mask or a blend mode is drawn into an isolated transparency group (a form XObject) that is then painted with the constant alpha,
/// soft mask and blend mode set. An import is a mutation of the document: single-threaded.
/// </para>
/// </remarks>
internal sealed partial class SvgImporter
{
    /// <summary>The deepest nesting of references (use, patterns, masks, clip paths, nested SVG images) that is followed.</summary>
    private const int MaxReferenceDepth = 32;

    /// <summary>The most elements drawn, and the most visited to compute bounding boxes: a <c>use</c> that instances groups of <c>use</c> elements grows exponentially.</summary>
    private const int MaxElements = 100_000;

    /// <summary>The deepest nesting of elements drawn, through <c>use</c> elements included.</summary>
    private const int MaxNesting = 256;

    /// <summary>The deepest nesting of SVG images inside SVG images.</summary>
    private const int MaxImageDepth = 8;

    /// <summary>Points per CSS pixel: 96 pixels are 72 points (CSS Values and Units Level 3 §5.2).</summary>
    internal const double PointsPerPixel = 0.75;

    private static readonly HashSet<string> Unsupported =
    [
        "filter", "foreignObject", "script", "animate", "animateColor", "animateMotion", "animateTransform", "set", "textPath",
        "font", "font-face", "glyph", "missing-glyph", "altGlyph", "altGlyphDef", "cursor", "view", "audio", "video", "canvas", "iframe",
        "discard", "feGaussianBlur",
    ];

    private readonly PdfDocument _document;
    private readonly SvgDocument _svg;
    private readonly SvgImportOptions _options;
    private readonly int _imageDepth;
    private readonly HashSet<SvgElement> _active = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(SvgElement, DiagnosticCode)> _reported = [];
    private readonly Dictionary<SvgElement, SvgStyle> _ancestorStyles = new(ReferenceEqualityComparer.Instance);
    private DrawContext _context = null!;
    private readonly Shared _shared;
    private int _nesting;
    private double _viewportWidth;
    private double _viewportHeight;

    private SvgImporter(PdfDocument document, SvgDocument svg, SvgImportOptions options, int imageDepth, Shared shared)
    {
        _document = document;
        _svg = svg;
        _options = options;
        _imageDepth = imageDepth;
        _shared = shared;
    }

    /// <summary>Parses and imports an SVG document as a new form XObject of <paramref name="document"/>.</summary>
    /// <exception cref="ArgumentException">The input is not an SVG document.</exception>
    public static PdfFormXObject Import(PdfDocument document, ReadOnlySpan<byte> bytes, SvgImportOptions options, int depth) =>
        Import(document, bytes, options, depth, new Shared());

    private static PdfFormXObject Import(PdfDocument document, ReadOnlySpan<byte> bytes, SvgImportOptions options, int depth, Shared shared)
    {
        SvgDocument svg = SvgDocument.Parse(bytes, message => document.ReportDiagnostic(new Diagnostic(SvgDiagnostics.SvgStyleInvalid, DiagnosticSeverity.Warning, $"Style sheet: {message}.")));
        return new SvgImporter(document, svg, options, depth, shared).Run();
    }

    /// <summary>What an import and the SVG images nested in it share: the element budgets and the imported SVG images by <c>href</c>.</summary>
    private sealed class Shared
    {
        public int Drawn { get; set; }

        public int Measured { get; set; }

        public Dictionary<string, PdfFormXObject> SvgImages { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Returns the size the root element asks for, in CSS pixels, and whether it gives none (so 300 by 150 is used).</summary>
    internal static (double Width, double Height, bool Missing) IntrinsicSize(SvgElement root)
    {
        bool hasViewBox = SvgRect.TryParseViewBox(root.Attribute("viewBox"), out SvgRect viewBox);
        double? width = AbsoluteLength(root.Attribute("width"));
        double? height = AbsoluteLength(root.Attribute("height"));
        if (width is { } w && height is { } h)
        {
            return (w, h, false);
        }

        if (hasViewBox)
        {
            double ratio = viewBox.Height / viewBox.Width;
            return (width, height) switch
            {
                ({ } only, null) => (only, only * ratio, false),
                (null, { } only) => (only / ratio, only, false),
                _ => (viewBox.Width, viewBox.Height, false),
            };
        }

        return (width ?? 300, height ?? 150, width is null && height is null);

        static double? AbsoluteLength(string? value) =>
            value is not null && SvgLength.TryParse(value, out SvgLength length) && length.Unit != SvgUnit.Percent && length.Value > 0
                ? length.Resolve(0, SvgStyle.DefaultFontSize)
                : null;
    }

    private PdfFormXObject Run()
    {
        SvgElement root = _svg.Root;
        if (_svg.Truncated)
        {
            Report(root, SvgDiagnostics.SvgReferenceCycle, $"Elements nested deeper than {SvgDocument.MaxDepth} levels are not drawn.");
        }

        (double widthPx, double heightPx, bool missing) = IntrinsicSize(root);
        if (missing)
        {
            Report(root, SvgDiagnostics.SvgSizeMissing, "The root svg element has no width, height or viewBox; it is imported at 300 by 150 pixels.");
        }

        double width = _options.Width ?? (widthPx * PointsPerPixel);
        double height = _options.Height ?? (heightPx * PointsPerPixel);
        SvgRect viewBox = SvgRect.TryParseViewBox(root.Attribute("viewBox"), out SvgRect box) ? box : new SvgRect(0, 0, widthPx, heightPx);
        PdfFormXObject form = _document.CreateFormXObject(new PdfRectangle(0, 0, width, height));
        using PdfCanvas canvas = form.CreateCanvas();
        _context = new DrawContext(canvas, new SvgRect(0, 0, width, height));
        Transform(new Matrix(1, 0, 0, -1, 0, height));
        Transform(SvgAspectRatio.Parse(root.Attribute("preserveAspectRatio")).Fit(viewBox, new SvgRect(0, 0, width, height)));
        _viewportWidth = viewBox.Width;
        _viewportHeight = viewBox.Height;
        SvgStyle style = StyleOf(root, SvgStyle.Initial);
        if (style.Displayed)
        {
            WithEffects(root, style, () => GroupBounds(root, style), _ => RenderChildren(root, style), foldable: false, applyTransform: false);
        }

        return form;
    }

    /// <summary>The canvas being written, the transformation from user space to its default space, and its bounds in that space.</summary>
    private sealed class DrawContext(PdfCanvas canvas, SvgRect bounds)
    {
        public PdfCanvas Canvas { get; } = canvas;

        public SvgRect Bounds { get; } = bounds;

        public Matrix Ctm { get; set; } = Matrix.Identity;

        public Stack<Matrix> Saved { get; } = new();
    }

    private PdfCanvas Canvas => _context.Canvas;

    private void Save()
    {
        Canvas.SaveState();
        _context.Saved.Push(_context.Ctm);
    }

    private void Restore()
    {
        Canvas.RestoreState();
        _context.Ctm = _context.Saved.Pop();
    }

    private void Transform(Matrix matrix)
    {
        if (matrix.IsIdentity)
        {
            return;
        }

        Canvas.Transform(matrix);
        _context.Ctm = matrix * _context.Ctm;
    }

    /// <summary>Returns the part of the current user space the content stream shows: its bounds mapped back into user space.</summary>
    private SvgRect VisibleArea() => _context.Ctm.TryInvert(out Matrix inverse) ? _context.Bounds.Transform(inverse) : default;

    /// <summary>Runs <paramref name="draw"/> with a new content stream (a form or pattern cell) as the current one.</summary>
    private void WithContext(PdfCanvas canvas, SvgRect bounds, Action draw)
    {
        DrawContext saved = _context;
        _context = new DrawContext(canvas, bounds);
        try
        {
            draw();
        }
        finally
        {
            _context = saved;
        }
    }

    private void WithViewport(double width, double height, Action draw)
    {
        (double savedWidth, double savedHeight) = (_viewportWidth, _viewportHeight);
        (_viewportWidth, _viewportHeight) = (width, height);
        try
        {
            draw();
        }
        finally
        {
            (_viewportWidth, _viewportHeight) = (savedWidth, savedHeight);
        }
    }

    private void Report(SvgElement element, DiagnosticCode code, string message)
    {
        if (_reported.Add((element, code)))
        {
            _document.ReportDiagnostic(new Diagnostic(code, code.DefaultSeverity, message));
        }
    }

    private SvgStyle StyleOf(SvgElement element, SvgStyle parent) =>
        parent.Derive(element, (name, value) => Report(element, SvgDiagnostics.SvgValueInvalid, $"The {name} value '{value}' of {element} cannot be read; the inherited value is used."));

    /// <summary>Returns the style of a referenced element (gradient stop, clip path, mask or pattern content) from its own ancestors, not the referencing element's.</summary>
    private SvgStyle AncestorStyle(SvgElement element)
    {
        if (_ancestorStyles.TryGetValue(element, out SvgStyle? style))
        {
            return style;
        }

        style = StyleOf(element, element.Parent is { } parent ? AncestorStyle(parent) : SvgStyle.Initial);
        _ancestorStyles[element] = style;
        return style;
    }

    private void RenderChildren(SvgElement element, SvgStyle style)
    {
        foreach (SvgElement child in element.Children)
        {
            RenderNode(child, style);
        }
    }

    private void RenderNode(SvgElement element, SvgStyle parentStyle)
    {
        if (Unsupported.Contains(element.Name) || element.Name.StartsWith("fe", StringComparison.Ordinal) && element.Name.Length > 2 && char.IsAsciiLetterUpper(element.Name[2]))
        {
            Report(element, SvgDiagnostics.SvgFeatureUnsupported, $"The {element.Name} element is not supported and is skipped.");
            return;
        }

        if (element.Name is not ("g" or "a" or "switch" or "svg" or "use" or "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text" or "image"))
        {
            // defs, symbol, gradients, patterns, clipPath, mask, marker, style, title, desc, metadata and unknown elements draw nothing where they are.
            return;
        }

        if (++_shared.Drawn > MaxElements || _nesting >= MaxNesting)
        {
            Report(_svg.Root, SvgDiagnostics.SvgReferenceCycle, $"The image draws more than {MaxElements} elements or nests deeper than {MaxNesting} levels through use; the rest is not drawn.");
            return;
        }

        SvgStyle style = StyleOf(element, parentStyle);
        if (!style.Displayed)
        {
            return;
        }

        _nesting++;
        try
        {
            RenderElement(element, style);
        }
        finally
        {
            _nesting--;
        }
    }

    private void RenderElement(SvgElement element, SvgStyle style)
    {
        switch (element.Name)
        {
            case "g" or "a":
                WithEffects(element, style, () => GroupBounds(element, style), _ => RenderChildren(element, style), foldable: false);
                break;
            case "switch":
                if (element.Children.FirstOrDefault(static child => child.Attribute("requiredExtensions") is null && child.Attribute("systemLanguage") is null) is { } chosen)
                {
                    WithEffects(element, style, () => GroupBounds(element, style), _ => RenderNode(chosen, style), foldable: false);
                }

                break;
            case "svg":
                RenderNestedSvg(element, style, null, null);
                break;
            case "use":
                RenderUse(element, style);
                break;
            case "text":
                RenderText(element, style);
                break;
            case "image":
                RenderImage(element, style);
                break;
            default:
                RenderShape(element, style);
                break;
        }
    }

    /// <summary>
    /// Draws an element with its transform, clipping path, mask, opacity and blend mode: <paramref name="body"/> draws the element
    /// itself and receives the alpha to multiply its paint by (the opacity, when it is folded into the paint). When
    /// <c>foldable</c>, the element paints at most once, so its opacity is applied to its paint instead of through a group.
    /// </summary>
    private void WithEffects(SvgElement element, SvgStyle style, Func<SvgRect> bounds, Action<double> body, bool foldable, bool applyTransform = true)
    {
        Matrix transform = Matrix.Identity;
        if (applyTransform && element.Attribute("transform") is { } transformText)
        {
            if (!SvgTransformParser.TryParse(transformText, out transform))
            {
                Report(element, SvgDiagnostics.SvgValueInvalid, $"The transform '{transformText}' of {element} cannot be read; it is ignored.");
                transform = Matrix.Identity;
            }

            if (transform.Determinant == 0 || !double.IsFinite(transform.Determinant))
            {
                return;
            }
        }

        if (style.Filter is not null)
        {
            Report(element, SvgDiagnostics.SvgFeatureUnsupported, $"The filter of {element} is not supported; the element is drawn unfiltered.");
        }

        Save();
        try
        {
            Transform(transform);
            SvgRect? box = null;
            SvgRect Box() => box ??= bounds();
            if (style.ClipPath is { } clip && !ApplyClipPath(element, clip, Box))
            {
                return;
            }

            bool group = style.Mask is not null || style.BlendMode != BlendMode.Normal || (style.Opacity < 1 && !foldable);
            if (!group)
            {
                if (style.Opacity > 0)
                {
                    body(style.Opacity);
                }

                return;
            }

            if (style.Opacity <= 0)
            {
                return;
            }

            SvgRect area = VisibleArea();
            if (!area.HasArea)
            {
                return;
            }

            PdfSoftMask? mask = null;
            if (style.Mask is { } maskReference)
            {
                if (!TryBuildMask(element, maskReference, Box, area, out mask))
                {
                    return;
                }
            }

            PdfFormXObject form = _document.CreateFormXObject(area.ToPdf());
            form.CreateTransparencyGroup().IsIsolated = true;
            using (PdfCanvas canvas = form.CreateCanvas())
            {
                WithContext(canvas, area, () => body(1));
            }

            if (mask is not null)
            {
                Canvas.SetSoftMask(mask);
            }

            if (style.Opacity < 1)
            {
                Canvas.SetFillAlpha(style.Opacity);
                Canvas.SetStrokeAlpha(style.Opacity);
            }

            if (style.BlendMode != BlendMode.Normal)
            {
                Canvas.SetBlendMode(style.BlendMode);
            }

            Canvas.DrawForm(form);
        }
        finally
        {
            Restore();
        }
    }

    private void RenderNestedSvg(SvgElement element, SvgStyle style, double? useWidth, double? useHeight)
    {
        double x = LengthX(element, "x", 0, style);
        double y = LengthY(element, "y", 0, style);
        double width = useWidth ?? LengthX(element, "width", _viewportWidth, style, percentDefault: 100);
        double height = useHeight ?? LengthY(element, "height", _viewportHeight, style, percentDefault: 100);
        WithEffects(element, style, () => new SvgRect(x, y, width, height), _ => RenderViewport(element, style, new SvgRect(x, y, width, height)), foldable: false);
    }

    /// <summary>Establishes the viewport of a nested <c>svg</c> or an instanced <c>symbol</c> and draws its children in it.</summary>
    private void RenderViewport(SvgElement element, SvgStyle style, SvgRect viewport)
    {
        if (!viewport.HasArea)
        {
            return;
        }

        if (!style.OverflowVisible)
        {
            Canvas.Rectangle(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            Canvas.Clip();
            Canvas.EndPath();
        }

        if (SvgRect.TryParseViewBox(element.Attribute("viewBox"), out SvgRect viewBox))
        {
            Transform(SvgAspectRatio.Parse(element.Attribute("preserveAspectRatio")).Fit(viewBox, viewport));
            WithViewport(viewBox.Width, viewBox.Height, () => RenderChildren(element, style));
        }
        else
        {
            Transform(Matrix.CreateTranslation(viewport.X, viewport.Y));
            WithViewport(viewport.Width, viewport.Height, () => RenderChildren(element, style));
        }
    }

    private void RenderUse(SvgElement element, SvgStyle style)
    {
        if (!TryReference(element, element.Attribute("href"), out SvgElement? target))
        {
            return;
        }

        double x = LengthX(element, "x", 0, style);
        double y = LengthY(element, "y", 0, style);
        WithEffects(
            element,
            style,
            () => GroupBounds(element, style),
            _ =>
            {
                Transform(Matrix.CreateTranslation(x, y));
                Follow(target, () =>
                {
                    if (target.Name == "symbol")
                    {
                        SvgStyle symbolStyle = StyleOf(target, style);
                        if (symbolStyle.Displayed)
                        {
                            double width = LengthX(element, "width", _viewportWidth, style, percentDefault: 100);
                            double height = LengthY(element, "height", _viewportHeight, style, percentDefault: 100);
                            WithEffects(target, symbolStyle, () => GroupBounds(target, symbolStyle), _ => RenderViewport(target, symbolStyle, new SvgRect(0, 0, width, height)), foldable: false);
                        }
                    }
                    else if (target.Name == "svg")
                    {
                        SvgStyle svgStyle = StyleOf(target, style);
                        if (svgStyle.Displayed)
                        {
                            double? width = element.Attribute("width") is null ? null : LengthX(element, "width", _viewportWidth, style);
                            double? height = element.Attribute("height") is null ? null : LengthY(element, "height", _viewportHeight, style);
                            RenderNestedSvg(target, svgStyle, width, height);
                        }
                    }
                    else
                    {
                        RenderNode(target, style);
                    }
                });
            },
            foldable: false);
    }

    /// <summary>Finds the element a local <c>href</c> names; records a diagnostic when there is none, or when following it would cycle.</summary>
    private bool TryReference(SvgElement element, string? href, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SvgElement? target)
    {
        target = href is null ? null : SvgReference.Id(href) is { } id ? _svg.Find(id) : null;
        if (target is null)
        {
            Report(element, SvgDiagnostics.SvgReferenceMissing, $"The reference '{href}' of {element} names no element of the document; it is ignored.");
            return false;
        }

        if (_active.Contains(target) || _active.Count >= MaxReferenceDepth || IsAncestor(target, element))
        {
            Report(element, SvgDiagnostics.SvgReferenceCycle, $"The reference '{href}' of {element} refers back to an element that contains it, or nests too deeply; it is not drawn.");
            target = null;
            return false;
        }

        return true;
    }

    private static bool IsAncestor(SvgElement candidate, SvgElement element)
    {
        for (SvgElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs <paramref name="draw"/> with <paramref name="target"/> marked as being drawn, so references back to it are refused.</summary>
    private void Follow(SvgElement target, Action draw)
    {
        _ = _active.Add(target);
        try
        {
            draw();
        }
        finally
        {
            _ = _active.Remove(target);
        }
    }

    /// <summary>Returns the bounding box of a container's children in its user space (before its own transform).</summary>
    private SvgRect GroupBounds(SvgElement element, SvgStyle style)
    {
        SvgRect? union = null;
        foreach (SvgElement child in element.Children)
        {
            if (ElementBounds(child, style, depth: 0) is { } bounds && (bounds.Width > 0 || bounds.Height > 0))
            {
                union = union is { } current ? current.Union(bounds) : bounds;
            }
        }

        return union ?? default;
    }

    /// <summary>Returns an element's bounding box in its parent's user space (after its transform), or <see langword="null"/> when it has none.</summary>
    private SvgRect? ElementBounds(SvgElement element, SvgStyle parentStyle, int depth)
    {
        if (depth > MaxNesting || ++_shared.Measured > MaxElements)
        {
            return null;
        }

        SvgStyle style = parentStyle.Derive(element, null);
        if (!style.Displayed)
        {
            return null;
        }

        Matrix transform = element.Attribute("transform") is { } text && SvgTransformParser.TryParse(text, out Matrix parsed) ? parsed : Matrix.Identity;
        SvgRect? local = null;
        switch (element.Name)
        {
            case "g" or "a" or "switch":
                foreach (SvgElement child in element.Children)
                {
                    if (ElementBounds(child, style, depth + 1) is { } bounds)
                    {
                        local = local is { } current ? current.Union(bounds) : bounds;
                    }
                }

                break;
            case "use":
                if (element.Attribute("href") is { } href && SvgReference.Id(href) is { } id && _svg.Find(id) is { } target && !IsAncestor(target, element))
                {
                    double x = LengthX(element, "x", 0, style);
                    double y = LengthY(element, "y", 0, style);
                    SvgRect? inner = target.Name is "symbol" or "svg"
                        ? new SvgRect(0, 0, LengthX(element, "width", _viewportWidth, style, percentDefault: 100), LengthY(element, "height", _viewportHeight, style, percentDefault: 100))
                        : ElementBounds(target, style, depth + 1);
                    local = inner?.Transform(Matrix.CreateTranslation(x, y));
                }

                break;
            case "svg" or "image":
                local = new SvgRect(LengthX(element, "x", 0, style), LengthY(element, "y", 0, style), LengthX(element, "width", 0, style), LengthY(element, "height", 0, style));
                break;
            case "text":
                local = TextBounds(element, style);
                break;
            default:
                if (Geometry(element, style, report: false) is { } path && !path.IsEmpty)
                {
                    local = path.Bounds(Matrix.Identity);
                }

                break;
        }

        return local?.Transform(transform);
    }

    private double LengthX(SvgElement element, string name, double fallback, SvgStyle style, double? percentDefault = null) =>
        Length(element, name, fallback, style, _viewportWidth, percentDefault);

    private double LengthY(SvgElement element, string name, double fallback, SvgStyle style, double? percentDefault = null) =>
        Length(element, name, fallback, style, _viewportHeight, percentDefault);

    private double LengthOther(SvgElement element, string name, double fallback, SvgStyle style) =>
        Length(element, name, fallback, style, Diagonal, null);

    /// <summary>Gets the reference length of percentages that are neither horizontal nor vertical (SVG 1.1 §7.10).</summary>
    private double Diagonal => Math.Sqrt(((_viewportWidth * _viewportWidth) + (_viewportHeight * _viewportHeight)) / 2);

    private double Length(SvgElement element, string name, double fallback, SvgStyle style, double reference, double? percentDefault)
    {
        string? value = element.Attribute(name);
        if (value is null || value.Trim() == "auto")
        {
            return percentDefault is { } percent ? reference * percent / 100 : fallback;
        }

        string? first = FirstListItem(value);
        if (first is null || !SvgLength.TryParse(first, out SvgLength length))
        {
            Report(element, SvgDiagnostics.SvgValueInvalid, $"The {name} value '{value}' of {element} cannot be read; {fallback.ToString(CultureInfo.InvariantCulture)} is used.");
            return percentDefault is { } percent2 ? reference * percent2 / 100 : fallback;
        }

        return length.Resolve(reference, style.FontSize);
    }

    private static string? FirstListItem(string value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        int end = text.IndexOfAny(" ,\t\n\r");
        return (end < 0 ? text : text[..end]).ToString() is { Length: > 0 } first ? first : null;
    }

    /// <summary>Reads a coordinate of an <c>objectBoundingBox</c> unit system: a number or a percentage, as a fraction of the box.</summary>
    private static double Fraction(SvgElement element, string name, double fallback)
    {
        string? value = element.Attribute(name);
        if (value is null || !SvgLength.TryParse(value, out SvgLength length))
        {
            return fallback;
        }

        return length.Unit == SvgUnit.Percent ? length.Value / 100 : length.Value;
    }

    /// <summary>The image bytes of a PNG, JPEG or SVG reference, cached per <c>href</c>.</summary>
    private readonly Dictionary<string, PdfImage> _images = new(StringComparer.Ordinal);
}
