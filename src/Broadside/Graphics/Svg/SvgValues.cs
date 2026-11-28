using System.Globalization;

namespace Broadside.Graphics.Svg;

/// <summary>Reads SVG numbers from spans, one at a time, without allocating.</summary>
/// <remarks>SVG 1.1 §4.2 (basic data types) and §8.3.9 (the path data grammar: numbers may follow one another without a separator when the next starts with a sign or a second decimal point).</remarks>
internal static class SvgNumber
{
    /// <summary>Skips white space and at most one comma.</summary>
    public static void SkipSeparator(ReadOnlySpan<char> text, ref int position)
    {
        SkipWhiteSpace(text, ref position);
        if (position < text.Length && text[position] == ',')
        {
            position++;
            SkipWhiteSpace(text, ref position);
        }
    }

    /// <summary>Skips white space.</summary>
    public static void SkipWhiteSpace(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length && IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    /// <summary>Returns whether <paramref name="c"/> is SVG white space (space, tab, line feed, carriage return, form feed).</summary>
    public static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>Reads a number at <paramref name="position"/> (after separators), advancing past it.</summary>
    /// <returns><see langword="true"/> when a finite number was read.</returns>
    public static bool TryRead(ReadOnlySpan<char> text, ref int position, out double value)
    {
        SkipSeparator(text, ref position);
        int start = position;
        int i = position;
        if (i < text.Length && text[i] is '+' or '-')
        {
            i++;
        }

        int digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            value = 0;
            return false;
        }

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            int exponent = i + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                while (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                {
                    exponent++;
                }

                i = exponent;
            }
        }

        if (!double.TryParse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
        {
            value = 0;
            return false;
        }

        position = i;
        return true;
    }

    /// <summary>Parses all of <paramref name="text"/> as one number (trimmed).</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out double value)
    {
        int position = 0;
        text = text.Trim();
        if (!TryRead(text, ref position, out value))
        {
            return false;
        }

        return position == text.Length;
    }

    /// <summary>Parses a list of numbers separated by white space or commas; stops at the first that cannot be read.</summary>
    public static List<double> ParseList(ReadOnlySpan<char> text)
    {
        var values = new List<double>();
        int position = 0;
        while (TryRead(text, ref position, out double value))
        {
            values.Add(value);
        }

        return values;
    }
}

/// <summary>The unit of an <see cref="SvgLength"/>.</summary>
internal enum SvgUnit
{
    /// <summary>A user unit (no unit, or <c>px</c>).</summary>
    User,

    /// <summary>A percentage of a reference length.</summary>
    Percent,

    /// <summary>The font size.</summary>
    Em,

    /// <summary>Half the font size.</summary>
    Ex,

    /// <summary>An absolute unit already converted to user units.</summary>
    Absolute,
}

/// <summary>A length as written: a number and a unit, resolved against a viewport and a font size when used.</summary>
/// <remarks>SVG 1.1 §7.10 (units): 1in = 96 user units, 1pt = 4/3, 1pc = 16, 1mm = 96/25.4, 1cm = 96/2.54.</remarks>
internal readonly record struct SvgLength(double Value, SvgUnit Unit)
{
    /// <summary>Gets a length of zero user units.</summary>
    public static SvgLength Zero => new(0, SvgUnit.User);

    /// <summary>Parses a length, such as <c>12</c>, <c>50%</c>, <c>2em</c> or <c>1in</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out SvgLength length)
    {
        text = text.Trim();
        int position = 0;
        length = Zero;
        if (!SvgNumber.TryRead(text, ref position, out double value))
        {
            return false;
        }

        ReadOnlySpan<char> unit = text[position..].Trim();
        if (unit.IsEmpty || unit.Equals("px", StringComparison.OrdinalIgnoreCase))
        {
            length = new SvgLength(value, SvgUnit.User);
            return true;
        }

        length = unit switch
        {
            "%" => new SvgLength(value, SvgUnit.Percent),
            _ when unit.Equals("em", StringComparison.OrdinalIgnoreCase) => new SvgLength(value, SvgUnit.Em),
            _ when unit.Equals("rem", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 16, SvgUnit.Absolute),
            _ when unit.Equals("ex", StringComparison.OrdinalIgnoreCase) => new SvgLength(value, SvgUnit.Ex),
            _ when unit.Equals("in", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 96, SvgUnit.Absolute),
            _ when unit.Equals("pt", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 4 / 3, SvgUnit.Absolute),
            _ when unit.Equals("pc", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 16, SvgUnit.Absolute),
            _ when unit.Equals("mm", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 96 / 25.4, SvgUnit.Absolute),
            _ when unit.Equals("cm", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 96 / 2.54, SvgUnit.Absolute),
            _ when unit.Equals("q", StringComparison.OrdinalIgnoreCase) => new SvgLength(value * 96 / 101.6, SvgUnit.Absolute),
            _ => new SvgLength(double.NaN, SvgUnit.User),
        };

        return !double.IsNaN(length.Value);
    }

    /// <summary>Resolves the length to user units.</summary>
    /// <param name="reference">What 100% is.</param>
    /// <param name="fontSize">The font size in user units, for <c>em</c> and <c>ex</c>.</param>
    public double Resolve(double reference, double fontSize) => Unit switch
    {
        SvgUnit.Percent => Value * reference / 100,
        SvgUnit.Em => Value * fontSize,
        SvgUnit.Ex => Value * fontSize / 2,
        _ => Value,
    };
}

/// <summary>A colour with straight (not premultiplied) alpha, each component 0 to 1.</summary>
internal readonly record struct SvgRgba(double R, double G, double B, double A)
{
    /// <summary>Gets opaque black.</summary>
    public static SvgRgba Black => new(0, 0, 0, 1);

    /// <summary>Returns the PDF colour in DeviceRGB, alpha dropped.</summary>
    public PdfColor ToPdf() => PdfColor.FromRgb((float)R, (float)G, (float)B);

    /// <summary>Gets the luminance a luminosity soft mask computes for this colour (ISO 32000-2 §11.5.3, for DeviceRGB).</summary>
    public double Luminance => (0.3 * R) + (0.59 * G) + (0.11 * B);
}

/// <summary>Reads CSS colour values: keywords, <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, <c>hsla()</c> and <c>transparent</c>.</summary>
/// <remarks>SVG 1.1 §4.4 (colour) and CSS Color Module Level 3 §4 (the 147 extended colour keywords, HSL).</remarks>
internal static class SvgColorParser
{
    private const string Keywords =
        "aliceblue f0f8ff antiquewhite faebd7 aqua 00ffff aquamarine 7fffd4 azure f0ffff beige f5f5dc bisque ffe4c4 black 000000 " +
        "blanchedalmond ffebcd blue 0000ff blueviolet 8a2be2 brown a52a2a burlywood deb887 cadetblue 5f9ea0 chartreuse 7fff00 " +
        "chocolate d2691e coral ff7f50 cornflowerblue 6495ed cornsilk fff8dc crimson dc143c cyan 00ffff darkblue 00008b darkcyan 008b8b " +
        "darkgoldenrod b8860b darkgray a9a9a9 darkgreen 006400 darkgrey a9a9a9 darkkhaki bdb76b darkmagenta 8b008b darkolivegreen 556b2f " +
        "darkorange ff8c00 darkorchid 9932cc darkred 8b0000 darksalmon e9967a darkseagreen 8fbc8f darkslateblue 483d8b darkslategray 2f4f4f " +
        "darkslategrey 2f4f4f darkturquoise 00ced1 darkviolet 9400d3 deeppink ff1493 deepskyblue 00bfff dimgray 696969 dimgrey 696969 " +
        "dodgerblue 1e90ff firebrick b22222 floralwhite fffaf0 forestgreen 228b22 fuchsia ff00ff gainsboro dcdcdc ghostwhite f8f8ff " +
        "gold ffd700 goldenrod daa520 gray 808080 grey 808080 green 008000 greenyellow adff2f honeydew f0fff0 hotpink ff69b4 " +
        "indianred cd5c5c indigo 4b0082 ivory fffff0 khaki f0e68c lavender e6e6fa lavenderblush fff0f5 lawngreen 7cfc00 lemonchiffon fffacd " +
        "lightblue add8e6 lightcoral f08080 lightcyan e0ffff lightgoldenrodyellow fafad2 lightgray d3d3d3 lightgreen 90ee90 lightgrey d3d3d3 " +
        "lightpink ffb6c1 lightsalmon ffa07a lightseagreen 20b2aa lightskyblue 87cefa lightslategray 778899 lightslategrey 778899 " +
        "lightsteelblue b0c4de lightyellow ffffe0 lime 00ff00 limegreen 32cd32 linen faf0e6 magenta ff00ff maroon 800000 " +
        "mediumaquamarine 66cdaa mediumblue 0000cd mediumorchid ba55d3 mediumpurple 9370db mediumseagreen 3cb371 mediumslateblue 7b68ee " +
        "mediumspringgreen 00fa9a mediumturquoise 48d1cc mediumvioletred c71585 midnightblue 191970 mintcream f5fffa mistyrose ffe4e1 " +
        "moccasin ffe4b5 navajowhite ffdead navy 000080 oldlace fdf5e6 olive 808000 olivedrab 6b8e23 orange ffa500 orangered ff4500 " +
        "orchid da70d6 palegoldenrod eee8aa palegreen 98fb98 paleturquoise afeeee palevioletred db7093 papayawhip ffefd5 peachpuff ffdab9 " +
        "peru cd853f pink ffc0cb plum dda0dd powderblue b0e0e6 purple 800080 rebeccapurple 663399 red ff0000 rosybrown bc8f8f royalblue 4169e1 " +
        "saddlebrown 8b4513 salmon fa8072 sandybrown f4a460 seagreen 2e8b57 seashell fff5ee sienna a0522d silver c0c0c0 skyblue 87ceeb " +
        "slateblue 6a5acd slategray 708090 slategrey 708090 snow fffafa springgreen 00ff7f steelblue 4682b4 tan d2b48c teal 008080 " +
        "thistle d8bfd8 tomato ff6347 turquoise 40e0d0 violet ee82ee wheat f5deb3 white ffffff whitesmoke f5f5f5 yellow ffff00 " +
        "yellowgreen 9acd32";

    private static readonly Lazy<Dictionary<string, int>> Table = new(BuildTable);

    /// <summary>Parses a colour value.</summary>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a colour.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out SvgRgba color)
    {
        text = text.Trim();
        color = SvgRgba.Black;
        if (text.IsEmpty)
        {
            return false;
        }

        if (text[0] == '#')
        {
            return TryParseHex(text[1..], out color);
        }

        int open = text.IndexOf('(');
        if (open > 0 && text[^1] == ')')
        {
            ReadOnlySpan<char> function = text[..open].Trim();
            ReadOnlySpan<char> arguments = text[(open + 1)..^1];
            if (function.Equals("rgb", StringComparison.OrdinalIgnoreCase) || function.Equals("rgba", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseFunction(arguments, hsl: false, out color);
            }

            if (function.Equals("hsl", StringComparison.OrdinalIgnoreCase) || function.Equals("hsla", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseFunction(arguments, hsl: true, out color);
            }

            return false;
        }

        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = new SvgRgba(0, 0, 0, 0);
            return true;
        }

        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup = Table.Value.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out int rgb))
        {
            color = new SvgRgba(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, 1);
            return true;
        }

        return false;
    }

    private static Dictionary<string, int> BuildTable()
    {
        var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] parts = Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            table[parts[i]] = int.Parse(parts[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return table;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out SvgRgba color)
    {
        color = SvgRgba.Black;
        Span<int> digits = stackalloc int[8];
        if (hex.Length is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        for (int i = 0; i < hex.Length; i++)
        {
            int digit = HexValue(hex[i]);
            if (digit < 0)
            {
                return false;
            }

            digits[i] = digit;
        }

        if (hex.Length <= 4)
        {
            double alpha = hex.Length == 4 ? digits[3] * 17 / 255.0 : 1;
            color = new SvgRgba(digits[0] * 17 / 255.0, digits[1] * 17 / 255.0, digits[2] * 17 / 255.0, alpha);
            return true;
        }

        double a = hex.Length == 8 ? ((digits[6] * 16) + digits[7]) / 255.0 : 1;
        color = new SvgRgba(((digits[0] * 16) + digits[1]) / 255.0, ((digits[2] * 16) + digits[3]) / 255.0, ((digits[4] * 16) + digits[5]) / 255.0, a);
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool TryParseFunction(ReadOnlySpan<char> arguments, bool hsl, out SvgRgba color)
    {
        color = SvgRgba.Black;
        Span<double> values = stackalloc double[4];
        Span<bool> percent = stackalloc bool[4];
        int count = 0;
        int position = 0;
        while (count < 4)
        {
            SvgNumber.SkipWhiteSpace(arguments, ref position);
            if (position < arguments.Length && arguments[position] is ',' or '/')
            {
                position++;
            }

            if (!SvgNumber.TryRead(arguments, ref position, out double value))
            {
                break;
            }

            SvgNumber.SkipWhiteSpace(arguments, ref position);
            bool isPercent = position < arguments.Length && arguments[position] == '%';
            if (isPercent)
            {
                position++;
            }
            else if (hsl && count == 0)
            {
                // A hue may carry an angle unit; degrees are the default.
                ReadOnlySpan<char> rest = arguments[position..];
                if (rest.StartsWith("deg", StringComparison.OrdinalIgnoreCase))
                {
                    position += 3;
                }
                else if (rest.StartsWith("turn", StringComparison.OrdinalIgnoreCase))
                {
                    value *= 360;
                    position += 4;
                }
                else if (rest.StartsWith("rad", StringComparison.OrdinalIgnoreCase))
                {
                    value *= 180 / Math.PI;
                    position += 3;
                }
            }

            values[count] = value;
            percent[count] = isPercent;
            count++;
        }

        SvgNumber.SkipWhiteSpace(arguments, ref position);
        if (count < 3 || position != arguments.Length)
        {
            return false;
        }

        double alpha = count == 4 ? Math.Clamp(percent[3] ? values[3] / 100 : values[3], 0, 1) : 1;
        if (hsl)
        {
            double hue = ((values[0] % 360) + 360) % 360 / 360;
            double saturation = Math.Clamp(values[1] / 100, 0, 1);
            double lightness = Math.Clamp(values[2] / 100, 0, 1);
            double m2 = lightness <= 0.5 ? lightness * (saturation + 1) : lightness + saturation - (lightness * saturation);
            double m1 = (lightness * 2) - m2;
            color = new SvgRgba(Hue(m1, m2, hue + (1.0 / 3)), Hue(m1, m2, hue), Hue(m1, m2, hue - (1.0 / 3)), alpha);
            return true;
        }

        color = new SvgRgba(Channel(values[0], percent[0]), Channel(values[1], percent[1]), Channel(values[2], percent[2]), alpha);
        return true;
    }

    private static double Channel(double value, bool percent) => Math.Clamp(percent ? value / 100 : value / 255, 0, 1);

    private static double Hue(double m1, double m2, double h)
    {
        if (h < 0)
        {
            h += 1;
        }

        if (h > 1)
        {
            h -= 1;
        }

        if (h * 6 < 1)
        {
            return m1 + ((m2 - m1) * h * 6);
        }

        if (h * 2 < 1)
        {
            return m2;
        }

        if (h * 3 < 2)
        {
            return m1 + ((m2 - m1) * ((2.0 / 3) - h) * 6);
        }

        return m1;
    }
}

/// <summary>Reads the <c>transform</c> attribute: a list of <c>matrix</c>, <c>translate</c>, <c>scale</c>, <c>rotate</c>, <c>skewX</c> and <c>skewY</c>.</summary>
/// <remarks>SVG 1.1 §7.6: the list applies right to left, so the first transform is the outermost.</remarks>
internal static class SvgTransformParser
{
    /// <summary>Parses a transform list.</summary>
    /// <param name="text">The list.</param>
    /// <param name="matrix">The product, from the element's user space to its parent's; the transforms read before an error when it fails.</param>
    /// <returns><see langword="true"/> when the whole list was read.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        int position = 0;
        Span<double> arguments = stackalloc double[6];
        while (true)
        {
            SvgNumber.SkipSeparator(text, ref position);
            if (position >= text.Length)
            {
                return true;
            }

            int nameStart = position;
            while (position < text.Length && char.IsAsciiLetter(text[position]))
            {
                position++;
            }

            ReadOnlySpan<char> name = text[nameStart..position];
            SvgNumber.SkipWhiteSpace(text, ref position);
            if (name.IsEmpty || position >= text.Length || text[position] != '(')
            {
                return false;
            }

            position++;
            int count = 0;
            while (count < 6 && SvgNumber.TryRead(text, ref position, out double value))
            {
                arguments[count++] = value;
            }

            SvgNumber.SkipWhiteSpace(text, ref position);
            if (position >= text.Length || text[position] != ')')
            {
                return false;
            }

            position++;
            Matrix? next = Create(name, arguments[..count]);
            if (next is not { } transform)
            {
                return false;
            }

            matrix = transform * matrix;
        }
    }

    private static Matrix? Create(ReadOnlySpan<char> name, ReadOnlySpan<double> a)
    {
        switch (name)
        {
            case "matrix" when a.Length == 6:
                return new Matrix(a[0], a[1], a[2], a[3], a[4], a[5]);
            case "translate" when a.Length is 1 or 2:
                return Matrix.CreateTranslation(a[0], a.Length == 2 ? a[1] : 0);
            case "scale" when a.Length is 1 or 2:
                return Matrix.CreateScale(a[0], a.Length == 2 ? a[1] : a[0]);
            case "rotate" when a.Length is 1 or 3:
                double radians = a[0] * Math.PI / 180;
                var rotation = new Matrix(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
                return a.Length == 1
                    ? rotation
                    : Matrix.CreateTranslation(-a[1], -a[2]) * rotation * Matrix.CreateTranslation(a[1], a[2]);
            case "skewX" when a.Length == 1:
                return new Matrix(1, 0, Math.Tan(a[0] * Math.PI / 180), 1, 0, 0);
            case "skewY" when a.Length == 1:
                return new Matrix(1, Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0);
            default:
                return null;
        }
    }
}

/// <summary>The <c>preserveAspectRatio</c> attribute: how a view box is fitted into a viewport.</summary>
/// <remarks>SVG 1.1 §7.8.</remarks>
internal readonly record struct SvgAspectRatio(int AlignX, int AlignY, bool Slice, bool None)
{
    /// <summary>Gets the default, <c>xMidYMid meet</c>.</summary>
    public static SvgAspectRatio Default => new(1, 1, false, false);

    /// <summary>Parses the attribute; anything unreadable is the default.</summary>
    public static SvgAspectRatio Parse(string? text)
    {
        if (text is null)
        {
            return Default;
        }

        string[] parts = text.Split((char[])[' ', '\t', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries);
        int index = 0;
        if (index < parts.Length && parts[index] == "defer")
        {
            index++;
        }

        if (index >= parts.Length)
        {
            return Default;
        }

        string align = parts[index++];
        bool slice = index < parts.Length && parts[index] == "slice";
        if (align == "none")
        {
            return new SvgAspectRatio(0, 0, false, true);
        }

        if (align.Length != 8 || !align.StartsWith('x') || align[4] != 'Y')
        {
            return Default;
        }

        int x = Position(align.AsSpan(1, 3));
        int y = Position(align.AsSpan(5, 3));
        return x < 0 || y < 0 ? Default : new SvgAspectRatio(x, y, slice, false);
    }

    /// <summary>Returns the transform that fits <paramref name="viewBox"/> into the viewport.</summary>
    public Matrix Fit(SvgRect viewBox, SvgRect viewport)
    {
        double sx = viewport.Width / viewBox.Width;
        double sy = viewport.Height / viewBox.Height;
        if (None)
        {
            return Matrix.CreateTranslation(-viewBox.X, -viewBox.Y) * Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(viewport.X, viewport.Y);
        }

        double scale = Slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
        double tx = viewport.X + ((viewport.Width - (viewBox.Width * scale)) * AlignX / 2);
        double ty = viewport.Y + ((viewport.Height - (viewBox.Height * scale)) * AlignY / 2);
        return Matrix.CreateTranslation(-viewBox.X, -viewBox.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tx, ty);
    }

    private static int Position(ReadOnlySpan<char> name) => name switch
    {
        "Min" => 0,
        "Mid" => 1,
        "Max" => 2,
        _ => -1,
    };
}

/// <summary>A rectangle in SVG coordinates (y down): origin and size.</summary>
internal readonly record struct SvgRect(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets a value indicating whether the rectangle has a positive width and height.</summary>
    public bool HasArea => Width > 0 && Height > 0;

    /// <summary>Parses a <c>viewBox</c>: four numbers, a positive width and height.</summary>
    public static bool TryParseViewBox(string? text, out SvgRect box)
    {
        box = default;
        if (text is null)
        {
            return false;
        }

        List<double> values = SvgNumber.ParseList(text);
        if (values.Count != 4 || !(values[2] > 0) || !(values[3] > 0))
        {
            return false;
        }

        box = new SvgRect(values[0], values[1], values[2], values[3]);
        return true;
    }

    /// <summary>Returns the bounding box of this rectangle after <paramref name="matrix"/>.</summary>
    public SvgRect Transform(Matrix matrix)
    {
        PathPoint a = matrix.Transform(X, Y);
        PathPoint b = matrix.Transform(Right, Y);
        PathPoint c = matrix.Transform(X, Bottom);
        PathPoint d = matrix.Transform(Right, Bottom);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new SvgRect(left, top, right - left, bottom - top);
    }

    /// <summary>Returns the smallest rectangle that holds both.</summary>
    public SvgRect Union(SvgRect other)
    {
        double left = Math.Min(X, other.X);
        double top = Math.Min(Y, other.Y);
        return new SvgRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    /// <summary>Returns the PDF rectangle with the same corners.</summary>
    public PdfRectangle ToPdf() => new(X, Y, Right, Bottom);
}
