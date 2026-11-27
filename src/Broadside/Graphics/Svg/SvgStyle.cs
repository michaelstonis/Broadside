namespace Broadside.Graphics.Svg;

/// <summary>The kind of an SVG paint.</summary>
internal enum SvgPaintKind
{
    /// <summary>Nothing is painted.</summary>
    None,

    /// <summary>A colour.</summary>
    Color,

    /// <summary>The <c>color</c> property of the element painted.</summary>
    CurrentColor,

    /// <summary>A paint server (gradient or pattern) by id, with a fallback.</summary>
    Server,
}

/// <summary>A <c>fill</c> or <c>stroke</c> value.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Color">The colour, for <see cref="SvgPaintKind.Color"/>, or the fallback colour of a server.</param>
/// <param name="Id">The paint server id, for <see cref="SvgPaintKind.Server"/>.</param>
/// <param name="Fallback">The kind of the fallback of a server: none, a colour or <c>currentColor</c>; <see langword="null"/> when none is given.</param>
/// <remarks>SVG 1.1 §11.2 (specifying paint).</remarks>
internal readonly record struct SvgPaint(SvgPaintKind Kind, SvgRgba Color, string? Id, SvgPaintKind? Fallback)
{
    public static SvgPaint None => new(SvgPaintKind.None, default, null, null);

    public static SvgPaint Black => new(SvgPaintKind.Color, SvgRgba.Black, null, null);

    /// <summary>Parses a paint value; <see langword="false"/> when it is not one.</summary>
    public static bool TryParse(string text, out SvgPaint paint)
    {
        ReadOnlySpan<char> value = text.AsSpan().Trim();
        paint = None;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            paint = new SvgPaint(SvgPaintKind.CurrentColor, default, null, null);
            return true;
        }

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int close = value.IndexOf(')');
            if (close < 0 || SvgReference.Id(value[4..close]) is not { } id)
            {
                return false;
            }

            ReadOnlySpan<char> fallback = value[(close + 1)..].Trim();
            if (fallback.IsEmpty)
            {
                paint = new SvgPaint(SvgPaintKind.Server, default, id, null);
                return true;
            }

            if (!TryParse(fallback.ToString(), out SvgPaint fallbackPaint) || fallbackPaint.Kind == SvgPaintKind.Server)
            {
                return false;
            }

            paint = new SvgPaint(SvgPaintKind.Server, fallbackPaint.Color, id, fallbackPaint.Kind);
            return true;
        }

        if (SvgColorParser.TryParse(value, out SvgRgba color))
        {
            paint = new SvgPaint(SvgPaintKind.Color, color, null, null);
            return true;
        }

        return false;
    }
}

/// <summary>Reads IRI references: <c>#id</c> and <c>url(#id)</c>.</summary>
internal static class SvgReference
{
    /// <summary>Returns the id of a local reference <c>#id</c> (optionally quoted), or <see langword="null"/>.</summary>
    public static string? Id(ReadOnlySpan<char> reference)
    {
        reference = reference.Trim();
        if (reference.Length >= 2 && reference[0] is '"' or '\'' && reference[^1] == reference[0])
        {
            reference = reference[1..^1].Trim();
        }

        return reference.Length > 1 && reference[0] == '#' ? reference[1..].ToString() : null;
    }

    /// <summary>Returns the id of a functional reference <c>url(#id)</c>, or <see langword="null"/>.</summary>
    public static string? FromUrl(string? value)
    {
        if (value is null)
        {
            return null;
        }

        ReadOnlySpan<char> text = value.AsSpan().Trim();
        if (!text.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || text[^1] != ')')
        {
            return null;
        }

        return Id(text[4..^1]);
    }
}

/// <summary>
/// The computed values of the styling properties an element is drawn with: the inherited ones copied from the parent, then the
/// element's specified values applied.
/// </summary>
/// <remarks>SVG 1.1 §6.4 (presentation attributes), Appendix N (property index: initial values and which properties inherit).</remarks>
internal sealed class SvgStyle
{
    /// <summary>The initial font size, <c>medium</c>, in user units.</summary>
    public const double DefaultFontSize = 16;

    // Inherited.
    public SvgPaint Fill { get; private set; } = SvgPaint.Black;

    public double FillOpacity { get; private set; } = 1;

    public FillRule FillRule { get; private set; } = FillRule.NonZero;

    public SvgPaint Stroke { get; private set; } = SvgPaint.None;

    public SvgLength StrokeWidth { get; private set; } = new(1, SvgUnit.User);

    public LineCap LineCap { get; private set; } = LineCap.Butt;

    public LineJoin LineJoin { get; private set; } = LineJoin.Miter;

    public double MiterLimit { get; private set; } = 4;

    public string? DashArray { get; private set; }

    public SvgLength DashOffset { get; private set; } = SvgLength.Zero;

    public double StrokeOpacity { get; private set; } = 1;

    public SvgRgba Color { get; private set; } = SvgRgba.Black;

    public string? FontFamily { get; private set; }

    public double FontSize { get; private set; } = DefaultFontSize;

    public int FontWeight { get; private set; } = 400;

    public bool Italic { get; private set; }

    public string TextAnchor { get; private set; } = "start";

    public bool Visible { get; private set; } = true;

    public FillRule ClipRule { get; private set; } = FillRule.NonZero;

    public bool PreserveSpace { get; private set; }

    // Not inherited.
    public double Opacity { get; private set; } = 1;

    public bool Displayed { get; private set; } = true;

    public string? ClipPath { get; private set; }

    public string? Mask { get; private set; }

    public BlendMode BlendMode { get; private set; } = BlendMode.Normal;

    public SvgRgba StopColor { get; private set; } = SvgRgba.Black;

    public double StopOpacity { get; private set; } = 1;

    public bool OverflowVisible { get; private set; }

    public bool AlphaMask { get; private set; }

    public string? Filter { get; private set; }

    public bool HasMarkers { get; private set; }

    /// <summary>Gets the style of the root: every property at its initial value.</summary>
    public static SvgStyle Initial { get; } = new();

    /// <summary>Returns the style of <paramref name="element"/> as a child of an element with this style.</summary>
    /// <param name="element">The element.</param>
    /// <param name="onInvalid">Receives the name and value of each property whose value cannot be read; it keeps the inherited or initial value.</param>
    public SvgStyle Derive(SvgElement element, Action<string, string>? onInvalid)
    {
        var style = new SvgStyle
        {
            Fill = Fill,
            FillOpacity = FillOpacity,
            FillRule = FillRule,
            Stroke = Stroke,
            StrokeWidth = StrokeWidth,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            DashArray = DashArray,
            DashOffset = DashOffset,
            StrokeOpacity = StrokeOpacity,
            Color = Color,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            Italic = Italic,
            TextAnchor = TextAnchor,
            Visible = Visible,
            ClipRule = ClipRule,
            PreserveSpace = PreserveSpace,
        };

        if (element.Attribute("xml:space") is { } space)
        {
            style.PreserveSpace = space == "preserve";
        }

        IReadOnlyDictionary<string, string> specified = element.Specified;

        // The colour first: currentColor in the other properties refers to it.
        if (specified.TryGetValue("color", out string? colorValue))
        {
            if (!IsInherit(colorValue))
            {
                if (SvgColorParser.TryParse(colorValue, out SvgRgba color))
                {
                    style.Color = color;
                }
                else if (!colorValue.Trim().Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                {
                    onInvalid?.Invoke("color", colorValue);
                }
            }
        }

        if (specified.TryGetValue("font-size", out string? sizeValue) && !IsInherit(sizeValue))
        {
            style.FontSize = FontSizeOf(sizeValue, FontSize, onInvalid);
        }

        foreach ((string name, string raw) in specified)
        {
            string value = raw.Trim();
            if (IsInherit(value) || name is "color" or "font-size")
            {
                continue;
            }

            if (!style.Apply(name, value, this))
            {
                onInvalid?.Invoke(name, raw);
            }
        }

        return style;
    }

    private static bool IsInherit(string value) => value.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase);

    private static double FontSizeOf(string value, double parent, Action<string, string>? onInvalid)
    {
        string text = value.Trim();
        double? keyword = text switch
        {
            "xx-small" => 9,
            "x-small" => 10,
            "small" => 13,
            "medium" => 16,
            "large" => 18,
            "x-large" => 24,
            "xx-large" => 32,
            "larger" => parent * 1.2,
            "smaller" => parent / 1.2,
            _ => null,
        };
        if (keyword is { } size)
        {
            return size;
        }

        if (SvgLength.TryParse(text, out SvgLength length) && length.Value >= 0)
        {
            return length.Unit == SvgUnit.Percent ? parent * length.Value / 100 : length.Resolve(parent, parent);
        }

        onInvalid?.Invoke("font-size", value);
        return parent;
    }

    /// <summary>Applies one specified value; returns <see langword="false"/> when it cannot be read.</summary>
    private bool Apply(string name, string value, SvgStyle parent)
    {
        switch (name)
        {
            case "fill":
                if (!SvgPaint.TryParse(value, out SvgPaint fill))
                {
                    return false;
                }

                Fill = fill;
                return true;
            case "stroke":
                if (!SvgPaint.TryParse(value, out SvgPaint stroke))
                {
                    return false;
                }

                Stroke = stroke;
                return true;
            case "fill-opacity":
                return TryOpacity(value, out double fillOpacity) && Set(() => FillOpacity = fillOpacity);
            case "stroke-opacity":
                return TryOpacity(value, out double strokeOpacity) && Set(() => StrokeOpacity = strokeOpacity);
            case "opacity":
                return TryOpacity(value, out double opacity) && Set(() => Opacity = opacity);
            case "stop-opacity":
                return TryOpacity(value, out double stopOpacity) && Set(() => StopOpacity = stopOpacity);
            case "fill-rule":
                return TryRule(value, out FillRule fillRule) && Set(() => FillRule = fillRule);
            case "clip-rule":
                return TryRule(value, out FillRule clipRule) && Set(() => ClipRule = clipRule);
            case "stroke-width":
                return SvgLength.TryParse(value, out SvgLength width) && width.Value >= 0 && Set(() => StrokeWidth = width);
            case "stroke-linecap":
                LineCap? cap = value switch { "butt" => LineCap.Butt, "round" => LineCap.Round, "square" => LineCap.ProjectingSquare, _ => null };
                return cap is { } c && Set(() => LineCap = c);
            case "stroke-linejoin":
                LineJoin? join = value switch
                {
                    "miter" or "miter-clip" or "arcs" => LineJoin.Miter,
                    "round" => LineJoin.Round,
                    "bevel" => LineJoin.Bevel,
                    _ => null,
                };
                return join is { } j && Set(() => LineJoin = j);
            case "stroke-miterlimit":
                return SvgNumber.TryParse(value, out double limit) && limit >= 1 && Set(() => MiterLimit = limit);
            case "stroke-dasharray":
                DashArray = value == "none" ? null : value;
                return true;
            case "stroke-dashoffset":
                return SvgLength.TryParse(value, out SvgLength offset) && Set(() => DashOffset = offset);
            case "font-family":
                FontFamily = value;
                return true;
            case "font-weight":
                int? weight = value switch
                {
                    "normal" => 400,
                    "bold" => 700,
                    "bolder" => Math.Min(900, parent.FontWeight + 300),
                    "lighter" => Math.Max(100, parent.FontWeight - 300),
                    _ => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) && number is >= 1 and <= 1000 ? number : null,
                };
                return weight is { } w && Set(() => FontWeight = w);
            case "font-style":
                Italic = value is "italic" or "oblique";
                return value is "normal" or "italic" or "oblique";
            case "text-anchor":
                if (value is not ("start" or "middle" or "end"))
                {
                    return false;
                }

                TextAnchor = value;
                return true;
            case "visibility":
                Visible = value is "visible";
                return value is "visible" or "hidden" or "collapse";
            case "display":
                Displayed = value != "none";
                return true;
            case "clip-path":
                ClipPath = value == "none" ? null : value;
                return true;
            case "mask":
                Mask = value == "none" ? null : value;
                return true;
            case "filter":
                Filter = value == "none" ? null : value;
                return true;
            case "marker" or "marker-start" or "marker-mid" or "marker-end":
                HasMarkers |= value != "none";
                return true;
            case "mix-blend-mode":
                BlendMode? mode = value switch
                {
                    "normal" => BlendMode.Normal,
                    "multiply" => BlendMode.Multiply,
                    "screen" => BlendMode.Screen,
                    "overlay" => BlendMode.Overlay,
                    "darken" => BlendMode.Darken,
                    "lighten" => BlendMode.Lighten,
                    "color-dodge" => BlendMode.ColorDodge,
                    "color-burn" => BlendMode.ColorBurn,
                    "hard-light" => BlendMode.HardLight,
                    "soft-light" => BlendMode.SoftLight,
                    "difference" => BlendMode.Difference,
                    "exclusion" => BlendMode.Exclusion,
                    "hue" => BlendMode.Hue,
                    "saturation" => BlendMode.Saturation,
                    "color" => BlendMode.Color,
                    "luminosity" => BlendMode.Luminosity,
                    _ => null,
                };
                return mode is { } m && Set(() => BlendMode = m);
            case "stop-color":
                if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                {
                    StopColor = Color;
                    return true;
                }

                return SvgColorParser.TryParse(value, out SvgRgba stopColor) && Set(() => StopColor = stopColor);
            case "overflow":
                OverflowVisible = value is "visible" or "auto";
                return true;
            case "mask-type":
                AlphaMask = value == "alpha";
                return value is "alpha" or "luminance";
            default:
                return true;
        }
    }

    private static bool Set(Action set)
    {
        set();
        return true;
    }

    private static bool TryOpacity(string value, out double opacity)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        bool percent = text.EndsWith('%');
        if (!SvgNumber.TryParse(percent ? text[..^1] : text, out opacity))
        {
            return false;
        }

        opacity = Math.Clamp(percent ? opacity / 100 : opacity, 0, 1);
        return true;
    }

    private static bool TryRule(string value, out FillRule rule)
    {
        rule = value == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
        return value is "evenodd" or "nonzero";
    }
}
