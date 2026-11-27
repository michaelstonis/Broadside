using Broadside.Diagnostics;

namespace Broadside.Graphics.Svg;

/// <summary>Shapes and paint: geometry of the basic shapes and paths, fills and strokes in colours, gradients and patterns.</summary>
/// <remarks>SVG 1.1 §8, §9, §11 and §13; ISO 32000-2 §8.5, §8.7.3, §8.7.4.5.3 and §8.7.4.5.4.</remarks>
internal sealed partial class SvgImporter
{
    /// <summary>The most periods a repeated or reflected gradient is expanded to.</summary>
    private const int MaxGradientPeriods = 64;

    private void RenderShape(SvgElement element, SvgStyle style)
    {
        if (Geometry(element, style, report: true) is not { } path || path.IsEmpty)
        {
            return;
        }

        if (style.HasMarkers)
        {
            Report(element, SvgDiagnostics.SvgFeatureUnsupported, $"The markers of {element} are not supported and are not drawn.");
        }

        bool fills = style.Fill.Kind != SvgPaintKind.None;
        bool strokes = style.Stroke.Kind != SvgPaintKind.None && StrokeWidth(style) > 0;
        WithEffects(
            element,
            style,
            () => path.Bounds(Matrix.Identity),
            alpha =>
            {
                if (!style.Visible)
                {
                    return;
                }

                SvgRect bounds = path.Bounds(Matrix.Identity);
                if (fills)
                {
                    PaintPath(element, path, style, style.Fill, style.FillOpacity * alpha, bounds, stroke: false);
                }

                if (strokes)
                {
                    PaintPath(element, path, style, style.Stroke, style.StrokeOpacity * alpha, bounds, stroke: true);
                }
            },
            foldable: !(fills && strokes));
    }

    /// <summary>Returns the path of a shape element in its user space, or <see langword="null"/> when it draws nothing (a zero or negative size disables it).</summary>
    private SvgPath? Geometry(SvgElement element, SvgStyle style, bool report)
    {
        var path = new SvgPath();
        switch (element.Name)
        {
            case "path":
                if (element.Attribute("d") is not { } data)
                {
                    return null;
                }

                if (!SvgPathParser.TryParse(data, path) && report)
                {
                    Report(element, SvgDiagnostics.SvgValueInvalid, $"The path data of {element} has an error; the path is drawn up to it.");
                }

                return path;
            case "rect":
                {
                    double width = LengthX(element, "width", 0, style);
                    double height = LengthY(element, "height", 0, style);
                    if (!(width > 0 && height > 0))
                    {
                        return null;
                    }

                    double rx = element.Attribute("rx") is null ? double.NaN : LengthX(element, "rx", 0, style);
                    double ry = element.Attribute("ry") is null ? double.NaN : LengthY(element, "ry", 0, style);
                    if (double.IsNaN(rx) || rx < 0)
                    {
                        rx = double.IsNaN(ry) || ry < 0 ? 0 : ry;
                    }

                    if (double.IsNaN(ry) || ry < 0)
                    {
                        ry = rx;
                    }

                    path.Rectangle(LengthX(element, "x", 0, style), LengthY(element, "y", 0, style), width, height, Math.Min(rx, width / 2), Math.Min(ry, height / 2));
                    return path;
                }

            case "circle":
                {
                    double r = LengthOther(element, "r", 0, style);
                    if (!(r > 0))
                    {
                        return null;
                    }

                    path.Ellipse(LengthX(element, "cx", 0, style), LengthY(element, "cy", 0, style), r, r);
                    return path;
                }

            case "ellipse":
                {
                    double rx = element.Attribute("rx") is null || element.Attribute("rx") == "auto" ? double.NaN : LengthX(element, "rx", 0, style);
                    double ry = element.Attribute("ry") is null || element.Attribute("ry") == "auto" ? double.NaN : LengthY(element, "ry", 0, style);
                    rx = double.IsNaN(rx) ? ry : rx;
                    ry = double.IsNaN(ry) ? rx : ry;
                    if (!(rx > 0 && ry > 0))
                    {
                        return null;
                    }

                    path.Ellipse(LengthX(element, "cx", 0, style), LengthY(element, "cy", 0, style), rx, ry);
                    return path;
                }

            case "line":
                path.MoveTo(LengthX(element, "x1", 0, style), LengthY(element, "y1", 0, style));
                path.LineTo(LengthX(element, "x2", 0, style), LengthY(element, "y2", 0, style));
                return path;
            case "polyline" or "polygon":
                {
                    List<double> points = SvgNumber.ParseList(element.Attribute("points") ?? string.Empty);
                    if (points.Count % 2 == 1 && report)
                    {
                        Report(element, SvgDiagnostics.SvgValueInvalid, $"The points of {element} have an odd number of coordinates; the last is dropped.");
                    }

                    if (points.Count < 4)
                    {
                        return null;
                    }

                    path.MoveTo(points[0], points[1]);
                    for (int i = 2; i + 1 < points.Count; i += 2)
                    {
                        path.LineTo(points[i], points[i + 1]);
                    }

                    if (element.Name == "polygon")
                    {
                        path.Close();
                    }

                    return path;
                }

            default:
                return null;
        }
    }

    private double StrokeWidth(SvgStyle style) => style.StrokeWidth.Resolve(Diagonal, style.FontSize);

    /// <summary>Sets the line width, caps, joins, miter limit and dash pattern of <paramref name="style"/> on <paramref name="canvas"/>.</summary>
    private void SetStrokeState(PdfCanvas canvas, SvgElement element, SvgStyle style)
    {
        canvas.SetLineWidth(StrokeWidth(style));
        if (style.LineCap != LineCap.Butt)
        {
            canvas.SetLineCap(style.LineCap);
        }

        if (style.LineJoin != LineJoin.Miter)
        {
            canvas.SetLineJoin(style.LineJoin);
        }

        if (style.MiterLimit != 10)
        {
            canvas.SetMiterLimit(style.MiterLimit);
        }

        if (style.DashArray is { } dashes)
        {
            var values = new List<double>();
            bool valid = true;
            foreach (string part in dashes.Split((char[])[' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SvgLength.TryParse(part, out SvgLength length) || length.Value < 0)
                {
                    valid = false;
                    break;
                }

                values.Add(length.Resolve(Diagonal, style.FontSize));
            }

            if (!valid)
            {
                Report(element, SvgDiagnostics.SvgValueInvalid, $"The stroke-dasharray '{dashes}' of {element} cannot be read; the stroke is solid.");
            }
            else if (values.Count > 0 && values.Sum() > 0)
            {
                if (values.Count % 2 == 1)
                {
                    values.AddRange(values);
                }

                double phase = style.DashOffset.Resolve(Diagonal, style.FontSize);
                double period = values.Sum();
                phase = ((phase % period) + period) % period;
                canvas.SetDashPattern([.. values], phase);
            }
        }
    }

    /// <summary>Fills or strokes <paramref name="path"/> with <paramref name="paint"/> at <paramref name="opacity"/>.</summary>
    private void PaintPath(SvgElement element, SvgPath path, SvgStyle style, SvgPaint paint, double opacity, SvgRect bounds, bool stroke)
    {
        void Draw(PdfCanvas canvas)
        {
            if (stroke)
            {
                SetStrokeState(canvas, element, style);
            }

            if (path.Emit(canvas, Matrix.Identity))
            {
                if (stroke)
                {
                    canvas.Stroke();
                }
                else
                {
                    canvas.Fill(style.FillRule);
                }
            }
        }

        Paint(element, style, paint, opacity, bounds, stroke, Draw);
    }

    /// <summary>
    /// Selects <paramref name="paint"/> for filling or stroking in a saved state and runs <paramref name="draw"/>, which writes the
    /// path or text and paints it; a gradient with transparent stops is painted through a luminosity soft mask of its alpha.
    /// </summary>
    private void Paint(SvgElement element, SvgStyle style, SvgPaint paint, double opacity, SvgRect bounds, bool stroke, Action<PdfCanvas> draw)
    {
        if (opacity <= 0)
        {
            return;
        }

        SvgRgba color;
        switch (paint.Kind)
        {
            case SvgPaintKind.None:
                return;
            case SvgPaintKind.Color:
                color = paint.Color;
                break;
            case SvgPaintKind.CurrentColor:
                color = style.Color;
                break;
            default:
                if (_svg.Find(paint.Id!) is { Name: "linearGradient" or "radialGradient" or "pattern" } server)
                {
                    if (server.Name == "pattern")
                    {
                        PaintWithPattern(element, server, opacity, bounds, stroke, draw);
                    }
                    else
                    {
                        PaintWithGradient(element, server, opacity, bounds, stroke, draw);
                    }

                    return;
                }

                if (paint.Fallback is null)
                {
                    Report(element, SvgDiagnostics.SvgReferenceMissing, $"The paint server '#{paint.Id}' of {element} is not a gradient or pattern of the document; nothing is painted.");
                    return;
                }

                if (paint.Fallback == SvgPaintKind.None)
                {
                    return;
                }

                color = paint.Fallback == SvgPaintKind.CurrentColor ? style.Color : paint.Color;
                break;
        }

        PaintSolid(color, opacity, stroke, draw);
    }

    private void PaintSolid(SvgRgba color, double opacity, bool stroke, Action<PdfCanvas> draw)
    {
        double alpha = color.A * opacity;
        if (alpha <= 0)
        {
            return;
        }

        Canvas.SaveState();
        if (stroke)
        {
            Canvas.SetStrokeColor(color.ToPdf());
            if (alpha < 1)
            {
                Canvas.SetStrokeAlpha(alpha);
            }
        }
        else
        {
            Canvas.SetFillColor(color.ToPdf());
            if (alpha < 1)
            {
                Canvas.SetFillAlpha(alpha);
            }
        }

        draw(Canvas);
        Canvas.RestoreState();
    }

    /// <summary>A gradient's attributes and stops, after following its <c>href</c> chain.</summary>
    private sealed record Gradient(
        bool Radial,
        bool ObjectBoundingBox,
        Matrix Transform,
        string Spread,
        List<(double Offset, SvgRgba Color)> Stops,
        SvgElement[] Chain);

    private Gradient ReadGradient(SvgElement server)
    {
        var chain = new List<SvgElement>();
        for (SvgElement? current = server; current is not null && chain.Count < MaxReferenceDepth && !chain.Contains(current); )
        {
            chain.Add(current);
            current = current.Attribute("href") is { } href && SvgReference.Id(href) is { } id && _svg.Find(id) is { Name: "linearGradient" or "radialGradient" } next ? next : null;
        }

        string? Attribute(string name) => chain.Select(element => element.Attribute(name)).FirstOrDefault(static value => value is not null);

        Matrix transform = Attribute("gradientTransform") is { } text && SvgTransformParser.TryParse(text, out Matrix parsed) ? parsed : Matrix.Identity;
        var stops = new List<(double Offset, SvgRgba Color)>();
        SvgElement? withStops = chain.FirstOrDefault(static element => element.Children.Any(static child => child.Name == "stop"));
        if (withStops is not null)
        {
            double previous = 0;
            foreach (SvgElement stop in withStops.Children.Where(static child => child.Name == "stop"))
            {
                SvgStyle stopStyle = AncestorStyle(stop);
                string offsetText = stop.Attribute("offset") ?? "0";
                ReadOnlySpan<char> trimmed = offsetText.AsSpan().Trim();
                bool percent = trimmed.EndsWith('%');
                double offset = SvgNumber.TryParse(percent ? trimmed[..^1] : trimmed, out double value) ? (percent ? value / 100 : value) : 0;
                offset = Math.Max(previous, Math.Clamp(offset, 0, 1));
                previous = offset;
                SvgRgba color = stopStyle.StopColor;
                stops.Add((offset, color with { A = color.A * stopStyle.StopOpacity }));
            }
        }

        return new Gradient(
            server.Name == "radialGradient",
            Attribute("gradientUnits") != "userSpaceOnUse",
            transform,
            Attribute("spreadMethod") ?? "pad",
            stops,
            [.. chain]);
    }

    private static string? GradientAttribute(Gradient gradient, string name) =>
        gradient.Chain.Select(element => element.Attribute(name)).FirstOrDefault(static value => value is not null);

    /// <summary>Reads a gradient coordinate: a fraction of the box in bounding box units, else a length against the viewport.</summary>
    private static double GradientValue(Gradient gradient, string name, double fallbackFraction, double reference)
    {
        string? value = GradientAttribute(gradient, name);
        if (value is null || !SvgLength.TryParse(value, out SvgLength length))
        {
            return gradient.ObjectBoundingBox ? fallbackFraction : fallbackFraction * reference;
        }

        if (gradient.ObjectBoundingBox)
        {
            return length.Unit == SvgUnit.Percent ? length.Value / 100 : length.Value;
        }

        return length.Resolve(reference, SvgStyle.DefaultFontSize);
    }

    private void PaintWithGradient(SvgElement element, SvgElement server, double opacity, SvgRect bounds, bool stroke, Action<PdfCanvas> draw)
    {
        Gradient gradient = ReadGradient(server);
        List<(double Offset, SvgRgba Color)> stops = gradient.Stops;
        if (stops.Count == 0)
        {
            return;
        }

        if (stops.Count == 1)
        {
            PaintSolid(stops[0].Color, opacity, stroke, draw);
            return;
        }

        Matrix units = Matrix.Identity;
        if (gradient.ObjectBoundingBox)
        {
            if (!(bounds.Width > 0 && bounds.Height > 0))
            {
                return;
            }

            units = new Matrix(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y);
        }

        Matrix toUser = gradient.Transform * units;
        if (!toUser.TryInvert(out Matrix fromUser))
        {
            return;
        }

        SvgRect area = bounds.HasArea ? bounds : VisibleArea();
        if (stroke)
        {
            area = VisibleArea();
        }

        SvgRect gradientArea = area.Transform(fromUser);
        PdfShadingOptions extend = new PdfShadingOptions().WithExtend(true, true);
        List<(double Offset, SvgRgba Color)> expanded;
        Func<IReadOnlyList<PdfGradientStop>, PdfShading> create;
        if (!gradient.Radial)
        {
            double x1 = GradientValue(gradient, "x1", 0, _viewportWidth);
            double y1 = GradientValue(gradient, "y1", 0, _viewportHeight);
            double x2 = GradientValue(gradient, "x2", 1, _viewportWidth);
            double y2 = GradientValue(gradient, "y2", 0, _viewportHeight);
            if (x1 == x2 && y1 == y2)
            {
                PaintSolid(stops[^1].Color, opacity, stroke, draw);
                return;
            }

            (int first, int last) = Periods(gradient.Spread, gradientArea, (px, py) =>
            {
                double vx = x2 - x1, vy = y2 - y1;
                return (((px - x1) * vx) + ((py - y1) * vy)) / ((vx * vx) + (vy * vy));
            });
            expanded = Expand(stops, gradient.Spread, first, last);
            var start = new PathPoint(x1 + ((x2 - x1) * first), y1 + ((y2 - y1) * first));
            var end = new PathPoint(x1 + ((x2 - x1) * last), y1 + ((y2 - y1) * last));
            create = list => _document.CreateAxialShading(start, end, list, extend);
        }
        else
        {
            double cx = GradientValue(gradient, "cx", 0.5, _viewportWidth);
            double cy = GradientValue(gradient, "cy", 0.5, _viewportHeight);
            double r = GradientValue(gradient, "r", 0.5, Diagonal);
            double fx = GradientAttribute(gradient, "fx") is null ? cx : GradientValue(gradient, "fx", 0.5, _viewportWidth);
            double fy = GradientAttribute(gradient, "fy") is null ? cy : GradientValue(gradient, "fy", 0.5, _viewportHeight);
            double fr = Math.Max(0, GradientValue(gradient, "fr", 0, Diagonal));
            if (!(r > 0))
            {
                PaintSolid(stops[^1].Color, opacity, stroke, draw);
                return;
            }

            int periods = 1;
            if (gradient.Spread is "repeat" or "reflect")
            {
                double farthest = 0;
                foreach ((double px, double py) in Corners(gradientArea))
                {
                    farthest = Math.Max(farthest, Math.Sqrt(((px - fx) * (px - fx)) + ((py - fy) * (py - fy))));
                }

                periods = Math.Clamp((int)Math.Ceiling(farthest / Math.Max(r - fr, r * 1e-3)) + 1, 1, MaxGradientPeriods);
            }

            expanded = Expand(stops, periods == 1 ? "pad" : gradient.Spread, 0, periods);
            var focal = new PathPoint(fx, fy);
            var center = new PathPoint(fx + ((cx - fx) * periods), fy + ((cy - fy) * periods));
            double radius = fr + ((r - fr) * periods);
            create = list => _document.CreateRadialShading(focal, fr, center, radius, list, extend);
        }

        PdfShadingPattern colorPattern = _document.CreateShadingPattern(
            create([.. expanded.Select(static stop => new PdfGradientStop(stop.Offset, stop.Color.ToPdf()))]),
            toUser * _context.Ctm);
        bool translucent = expanded.Any(static stop => stop.Color.A < 1);
        PdfSoftMask? mask = null;
        if (translucent)
        {
            // Shadings carry no alpha: the stops' opacity becomes a luminosity mask painted with the same geometry.
            PdfShadingPattern alphaPattern = _document.CreateShadingPattern(
                create([.. expanded.Select(static stop => new PdfGradientStop(stop.Offset, PdfColor.FromGray((float)stop.Color.A)))]),
                toUser);
            SvgRect visible = VisibleArea();
            PdfFormXObject maskForm = _document.CreateFormXObject(visible.ToPdf());
            maskForm.CreateTransparencyGroup().ColorSpace = PdfColor.FromGray(0).ColorSpace;
            using (PdfCanvas maskCanvas = maskForm.CreateCanvas())
            {
                if (stroke)
                {
                    maskCanvas.SetStrokePattern(alphaPattern);
                }
                else
                {
                    maskCanvas.SetFillPattern(alphaPattern);
                }

                draw(maskCanvas);
            }

            mask = _document.CreateSoftMask(PdfSoftMaskType.Luminosity, maskForm);
        }

        Canvas.SaveState();
        if (mask is not null)
        {
            Canvas.SetSoftMask(mask);
        }

        if (stroke)
        {
            Canvas.SetStrokePattern(colorPattern);
            if (opacity < 1)
            {
                Canvas.SetStrokeAlpha(opacity);
            }
        }
        else
        {
            Canvas.SetFillPattern(colorPattern);
            if (opacity < 1)
            {
                Canvas.SetFillAlpha(opacity);
            }
        }

        draw(Canvas);
        Canvas.RestoreState();
    }

    private static IEnumerable<(double X, double Y)> Corners(SvgRect area) =>
        [(area.X, area.Y), (area.Right, area.Y), (area.X, area.Bottom), (area.Right, area.Bottom)];

    /// <summary>Returns the whole periods of a repeated or reflected axial gradient that cover <paramref name="area"/> (0 and 1 for pad).</summary>
    private static (int First, int Last) Periods(string spread, SvgRect area, Func<double, double, double> parameter)
    {
        if (spread is not ("repeat" or "reflect"))
        {
            return (0, 1);
        }

        double low = 0, high = 1;
        foreach ((double x, double y) in Corners(area))
        {
            double t = parameter(x, y);
            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }

        int first = (int)Math.Floor(Math.Max(low, -MaxGradientPeriods));
        int last = (int)Math.Ceiling(Math.Min(high, MaxGradientPeriods));
        if (last - first > MaxGradientPeriods)
        {
            last = first + MaxGradientPeriods;
        }

        return (first, Math.Max(first + 1, last));
    }

    /// <summary>Expands stops over the periods <paramref name="first"/> to <paramref name="last"/>, mirroring odd periods for reflect, into offsets 0 to 1.</summary>
    private static List<(double Offset, SvgRgba Color)> Expand(List<(double Offset, SvgRgba Color)> stops, string spread, int first, int last)
    {
        if (last - first == 1 && first == 0)
        {
            return stops;
        }

        var expanded = new List<(double Offset, SvgRgba Color)>();
        double span = last - first;
        for (int period = first; period < last; period++)
        {
            bool mirrored = spread == "reflect" && ((period % 2) + 2) % 2 == 1;
            for (int i = 0; i < stops.Count; i++)
            {
                (double offset, SvgRgba color) = mirrored ? (1 - stops[stops.Count - 1 - i].Offset, stops[stops.Count - 1 - i].Color) : stops[i];
                double value = (period - first + offset) / span;
                if (expanded.Count > 0 && value < expanded[^1].Offset)
                {
                    value = expanded[^1].Offset;
                }

                expanded.Add((Math.Clamp(value, 0, 1), color));
            }
        }

        return expanded;
    }

    private void PaintWithPattern(SvgElement element, SvgElement server, double opacity, SvgRect bounds, bool stroke, Action<PdfCanvas> draw)
    {
        var chain = new List<SvgElement>();
        for (SvgElement? current = server; current is not null && chain.Count < MaxReferenceDepth && !chain.Contains(current); )
        {
            chain.Add(current);
            current = current.Attribute("href") is { } href && SvgReference.Id(href) is { } id && _svg.Find(id) is { Name: "pattern" } next ? next : null;
        }

        string? Attribute(string name) => chain.Select(e => e.Attribute(name)).FirstOrDefault(static value => value is not null);
        SvgElement content = chain.FirstOrDefault(static e => e.Children.Count > 0) ?? server;
        if (_active.Contains(content) || _active.Count >= MaxReferenceDepth)
        {
            Report(element, SvgDiagnostics.SvgReferenceCycle, $"The pattern '#{server.Id}' of {element} refers back to itself; nothing is painted.");
            return;
        }

        bool boxUnits = Attribute("patternUnits") != "userSpaceOnUse";
        bool boxContent = Attribute("patternContentUnits") == "objectBoundingBox";
        if ((boxUnits || boxContent) && !(bounds.Width > 0 && bounds.Height > 0))
        {
            return;
        }

        double Value(string name, double reference)
        {
            string? text = Attribute(name);
            if (text is null || !SvgLength.TryParse(text, out SvgLength length))
            {
                return 0;
            }

            if (boxUnits)
            {
                double fraction = length.Unit == SvgUnit.Percent ? length.Value / 100 : length.Value;
                return name is "x" or "width" ? fraction * bounds.Width : fraction * bounds.Height;
            }

            return length.Resolve(reference, SvgStyle.DefaultFontSize);
        }

        double x = Value("x", _viewportWidth) + (boxUnits ? bounds.X : 0);
        double y = Value("y", _viewportHeight) + (boxUnits ? bounds.Y : 0);
        double width = Value("width", _viewportWidth);
        double height = Value("height", _viewportHeight);
        if (!(width > 0 && height > 0))
        {
            return;
        }

        Matrix patternTransform = Attribute("patternTransform") is { } text && SvgTransformParser.TryParse(text, out Matrix parsed) ? parsed : Matrix.Identity;
        PdfTilingPattern pattern = _document.CreateTilingPattern(
            new PdfRectangle(0, 0, width, height),
            width,
            height,
            PdfTilingPaintType.Colored,
            Matrix.CreateTranslation(x, y) * patternTransform * _context.Ctm);
        using (PdfCanvas cell = pattern.CreateCanvas())
        {
            WithContext(cell, new SvgRect(0, 0, width, height), () =>
            {
                Matrix contentTransform;
                double viewportWidth = _viewportWidth, viewportHeight = _viewportHeight;
                if (SvgRect.TryParseViewBox(Attribute("viewBox"), out SvgRect viewBox))
                {
                    contentTransform = SvgAspectRatio.Parse(Attribute("preserveAspectRatio")).Fit(viewBox, new SvgRect(0, 0, width, height));
                    (viewportWidth, viewportHeight) = (viewBox.Width, viewBox.Height);
                }
                else if (boxContent)
                {
                    contentTransform = new Matrix(bounds.Width, 0, 0, bounds.Height, bounds.X - x, bounds.Y - y);
                }
                else
                {
                    contentTransform = Matrix.CreateTranslation(-x, -y);
                }

                Transform(contentTransform);
                WithViewport(viewportWidth, viewportHeight, () => Follow(content, () => RenderChildren(content, AncestorStyle(content))));
            });
        }

        Canvas.SaveState();
        if (stroke)
        {
            Canvas.SetStrokePattern(pattern);
            if (opacity < 1)
            {
                Canvas.SetStrokeAlpha(opacity);
            }
        }
        else
        {
            Canvas.SetFillPattern(pattern);
            if (opacity < 1)
            {
                Canvas.SetFillAlpha(opacity);
            }
        }

        draw(Canvas);
        Canvas.RestoreState();
    }
}
