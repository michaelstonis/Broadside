using System.Text;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// The parameters a graphics state parameter dictionary sets, read once per dictionary into typed values, so that <c>gs</c> applies
/// them to the state without reading COS objects or allocating. A field is <see langword="null"/> (or its <c>Has</c> flag false)
/// when the dictionary does not set that parameter.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.4.5, Table 57. Precedences: <c>op</c> wins over <c>OP</c> for nonstroking overprint (<c>OP</c> sets both when
/// <c>op</c> is absent); <c>BG2</c> over <c>BG</c>, <c>UCR2</c> over <c>UCR</c>, <c>TR2</c> over <c>TR</c>, and the name
/// <c>Default</c> of the "2" keys selects the device default. Unknown keys are ignored.
/// </para>
/// <para>
/// Repairs, the first of which <see cref="Problem"/> describes: a negative line width takes its absolute value (as viewers do); a
/// cap or join outside 0 to 2 reads as 0, a miter limit below 1 as 1, alphas, flatness and smoothness are clipped into range; an
/// unknown blend mode is Normal and the first recognised name of a blend-mode array is used; a malformed soft mask, function,
/// font entry or other value leaves its parameter unchanged.
/// </para>
/// </remarks>
internal sealed class ExtGStateParameters
{
    private static readonly CosName LW = new("LW");
    private static readonly CosName LC = new("LC");
    private static readonly CosName LJ = new("LJ");
    private static readonly CosName ML = new("ML");
    private static readonly CosName D = new("D");
    private static readonly CosName RI = new("RI");
    private static readonly CosName OPStroke = new("OP");
    private static readonly CosName OPFill = new("op");
    private static readonly CosName OPM = new("OPM");
    private static readonly CosName FontKey = new("Font");
    private static readonly CosName BG = new("BG");
    private static readonly CosName BG2 = new("BG2");
    private static readonly CosName UCR = new("UCR");
    private static readonly CosName UCR2 = new("UCR2");
    private static readonly CosName TR = new("TR");
    private static readonly CosName TR2 = new("TR2");
    private static readonly CosName HT = new("HT");
    private static readonly CosName FL = new("FL");
    private static readonly CosName SM = new("SM");
    private static readonly CosName SA = new("SA");
    private static readonly CosName BM = new("BM");
    private static readonly CosName SMask = new("SMask");
    private static readonly CosName CAStroke = new("CA");
    private static readonly CosName CAFill = new("ca");
    private static readonly CosName AIS = new("AIS");
    private static readonly CosName TK = new("TK");
    private static readonly CosName UseBlackPtComp = new("UseBlackPtComp");
    private static readonly CosName HTO = new("HTO");
    private static readonly CosName Default = new("Default");
    private static readonly CosName None = new("None");

    private ExtGStateParameters(int version) => Version = version;

    /// <summary>Gets the version of the dictionary these were read from.</summary>
    public int Version { get; }

    /// <summary>Gets the first deviation found, or <see langword="null"/>.</summary>
    public string? Problem { get; private set; }

    public double? LineWidth { get; private set; }

    public LineCap? LineCap { get; private set; }

    public LineJoin? LineJoin { get; private set; }

    public double? MiterLimit { get; private set; }

    /// <summary>Gets the dash array of <c>D</c>, not yet normalized (the interpreter applies the rules of §8.4.3.6).</summary>
    public double[]? DashArray { get; private set; }

    public double DashPhase { get; private set; }

    public RenderingIntent? RenderingIntent { get; private set; }

    public bool? StrokeOverprint { get; private set; }

    public bool? FillOverprint { get; private set; }

    public int? OverprintMode { get; private set; }

    public bool HasFont { get; private set; }

    public PdfFont? Font { get; private set; }

    public double FontSize { get; private set; }

    public bool HasBlackGeneration { get; private set; }

    public PdfFunction? BlackGeneration { get; private set; }

    public bool HasUndercolorRemoval { get; private set; }

    public PdfFunction? UndercolorRemoval { get; private set; }

    public bool HasTransfer { get; private set; }

    public PdfTransferFunction? Transfer { get; private set; }

    public bool HasHalftone { get; private set; }

    public CosObject? Halftone { get; private set; }

    public double? Flatness { get; private set; }

    public double? Smoothness { get; private set; }

    public bool? StrokeAdjustment { get; private set; }

    public BlendMode? BlendMode { get; private set; }

    public bool HasSoftMask { get; private set; }

    public PdfSoftMask? SoftMask { get; private set; }

    public double? StrokeAlpha { get; private set; }

    public double? FillAlpha { get; private set; }

    public bool? AlphaIsShape { get; private set; }

    public bool? TextKnockout { get; private set; }

    public BlackPointCompensation? BlackPointCompensation { get; private set; }

    public PathPoint? HalftoneOrigin { get; private set; }

    /// <summary>Gets a value indicating whether the dictionary sets a colour-related parameter: RI, OP, op, OPM, BG, UCR, TR, HT, HTO or UseBlackPtComp.</summary>
    public bool SetsColorParameters =>
        RenderingIntent is not null || StrokeOverprint is not null || FillOverprint is not null || OverprintMode is not null || HasBlackGeneration
        || HasUndercolorRemoval || HasTransfer || HasHalftone || HalftoneOrigin is not null || BlackPointCompensation is not null;

    /// <summary>Reads a graphics state parameter dictionary.</summary>
    public static ExtGStateParameters Read(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _ = reference;
        var parameters = new ExtGStateParameters(dictionary.Version);
        parameters.ReadEntries(document, dictionary);
        return parameters;
    }

    /// <summary>Maps a blend mode name (Table 134 and Table 135; Compatible is Normal, §11.3.5); <see langword="null"/> when not one.</summary>
    internal static BlendMode? ParseBlendMode(ReadOnlySpan<byte> name) => name switch
    {
        _ when name.SequenceEqual("Normal"u8) || name.SequenceEqual("Compatible"u8) => Graphics.BlendMode.Normal,
        _ when name.SequenceEqual("Multiply"u8) => Graphics.BlendMode.Multiply,
        _ when name.SequenceEqual("Screen"u8) => Graphics.BlendMode.Screen,
        _ when name.SequenceEqual("Overlay"u8) => Graphics.BlendMode.Overlay,
        _ when name.SequenceEqual("Darken"u8) => Graphics.BlendMode.Darken,
        _ when name.SequenceEqual("Lighten"u8) => Graphics.BlendMode.Lighten,
        _ when name.SequenceEqual("ColorDodge"u8) => Graphics.BlendMode.ColorDodge,
        _ when name.SequenceEqual("ColorBurn"u8) => Graphics.BlendMode.ColorBurn,
        _ when name.SequenceEqual("HardLight"u8) => Graphics.BlendMode.HardLight,
        _ when name.SequenceEqual("SoftLight"u8) => Graphics.BlendMode.SoftLight,
        _ when name.SequenceEqual("Difference"u8) => Graphics.BlendMode.Difference,
        _ when name.SequenceEqual("Exclusion"u8) => Graphics.BlendMode.Exclusion,
        _ when name.SequenceEqual("Hue"u8) => Graphics.BlendMode.Hue,
        _ when name.SequenceEqual("Saturation"u8) => Graphics.BlendMode.Saturation,
        _ when name.SequenceEqual("Color"u8) => Graphics.BlendMode.Color,
        _ when name.SequenceEqual("Luminosity"u8) => Graphics.BlendMode.Luminosity,
        _ => null,
    };

    private void ReadEntries(PdfDocument document, CosDictionary dictionary)
    {
        CosObject? Get(CosName key) => dictionary.TryGetValue(key, out CosObject? value) ? document.Resolve(value) : null;

        if (Number(Get(LW), "LW") is { } width)
        {
            if (width < 0)
            {
                Fail("LW is negative; its absolute value is used, as viewers do.");
            }

            LineWidth = Math.Abs(width);
        }

        LineCap = Style(Get(LC), "LC") is { } cap ? (LineCap)cap : null;
        LineJoin = Style(Get(LJ), "LJ") is { } join ? (LineJoin)join : null;
        if (Number(Get(ML), "ML") is { } miter)
        {
            if (miter < 1)
            {
                Fail("ML is below 1; 1 is used.");
            }

            MiterLimit = Math.Max(1, miter);
        }

        ReadDash(document, Get(D));
        if (Get(RI) is { } intent)
        {
            if (intent is CosName name)
            {
                RenderingIntent = ContentInterpreter.Intent(name.Bytes);
            }
            else
            {
                Fail("RI is not a name; ignored.");
            }
        }

        StrokeOverprint = Boolean(Get(OPStroke), "OP");
        FillOverprint = Boolean(Get(OPFill), "op") ?? StrokeOverprint;
        if (Get(OPM) is { } mode)
        {
            if (mode is CosInteger { Value: 0 or 1 } integer)
            {
                OverprintMode = (int)integer.Value;
            }
            else
            {
                Fail("OPM is not 0 or 1; ignored.");
            }
        }

        ReadFont(document, Get(FontKey));
        (HasBlackGeneration, BlackGeneration) = ReadDeviceFunction(document, dictionary, BG2, BG);
        (HasUndercolorRemoval, UndercolorRemoval) = ReadDeviceFunction(document, dictionary, UCR2, UCR);
        ReadTransfer(document, dictionary);
        ReadHalftone(Get(HT));
        Flatness = Number(Get(FL), "FL") is { } flatness ? Clamp(flatness, 0, 100, "FL") : null;
        Smoothness = Number(Get(SM), "SM") is { } smoothness ? Clamp(smoothness, 0, 1, "SM") : null;
        StrokeAdjustment = Boolean(Get(SA), "SA");
        ReadBlendMode(document, Get(BM));
        ReadSoftMask(document, dictionary);
        StrokeAlpha = Number(Get(CAStroke), "CA") is { } strokeAlpha ? Clamp(strokeAlpha, 0, 1, "CA") : null;
        FillAlpha = Number(Get(CAFill), "ca") is { } fillAlpha ? Clamp(fillAlpha, 0, 1, "ca") : null;
        AlphaIsShape = Boolean(Get(AIS), "AIS");
        TextKnockout = Boolean(Get(TK), "TK");
        if (Get(UseBlackPtComp) is { } compensation)
        {
            BlackPointCompensation = compensation switch
            {
                CosName { Value: "ON" } => Graphics.BlackPointCompensation.On,
                CosName { Value: "OFF" } => Graphics.BlackPointCompensation.Off,
                CosName { Value: "Default" } => Graphics.BlackPointCompensation.Default,
                _ => null,
            };
            if (BlackPointCompensation is null)
            {
                Fail("UseBlackPtComp is not ON, OFF or Default; ignored.");
            }
        }

        if (Get(HTO) is { } origin)
        {
            Span<double> point = stackalloc double[2];
            if (origin is CosArray { Count: 2 } array && AnnotationValues.TryReadNumbers(document, array, point))
            {
                HalftoneOrigin = new PathPoint(point[0], point[1]);
            }
            else
            {
                Fail("HTO is not an array of two numbers; ignored.");
            }
        }
    }

    private void ReadDash(PdfDocument document, CosObject? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is CosArray { Count: 2 } pattern && document.Resolve(pattern[0]) is CosArray elements
            && AnnotationValues.ReadNumber(document, pattern[1]) is { } phase
            && AnnotationValues.ReadNumbers(document, elements, out bool complete) is { } numbers && complete)
        {
            DashArray = [.. numbers];
            DashPhase = phase;
            return;
        }

        Fail("D is not an array of a dash array and a phase; ignored.");
    }

    private void ReadFont(PdfDocument document, CosObject? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is CosArray { Count: 2 } entry && AnnotationValues.ReadNumber(document, entry[1]) is { } size
            && document.GetFont(entry[0]) is { } font)
        {
            HasFont = true;
            Font = font;
            FontSize = size;
            return;
        }

        Fail("Font is not an array of an indirect font dictionary and a size; ignored.");
    }

    /// <summary>BG/BG2 or UCR/UCR2: the "2" key wins; its <c>Default</c> is the device default (a null function).</summary>
    private (bool Has, PdfFunction? Function) ReadDeviceFunction(PdfDocument document, CosDictionary dictionary, CosName second, CosName first)
    {
        CosName key = dictionary.ContainsKey(second) ? second : first;
        if (!dictionary.TryGetValue(key, out CosObject? value))
        {
            return (false, null);
        }

        if (key == second && document.Resolve(value) is CosName name && name.Equals(Default))
        {
            return (true, null);
        }

        if (document.Resolve(value) is CosDictionary or CosStream && document.Functions.Get(value, 1, 1) is { IsValid: true } function)
        {
            return (true, function);
        }

        Fail($"{key.Value} is not a function of one input and one output; ignored.");
        return (false, null);
    }

    /// <summary>TR/TR2: a function, an array of four, <c>Identity</c>, or (TR2) <c>Default</c>.</summary>
    private void ReadTransfer(PdfDocument document, CosDictionary dictionary)
    {
        CosName key = dictionary.ContainsKey(TR2) ? TR2 : TR;
        if (!dictionary.TryGetValue(key, out CosObject? value))
        {
            return;
        }

        CosObject resolved = document.Resolve(value);
        if (resolved is CosName name && (name.Equals(FunctionNames.Identity) || (key == TR2 && name.Equals(Default))))
        {
            HasTransfer = true;
            Transfer = name.Equals(Default) ? null : new PdfTransferFunction(resolved, []);
            return;
        }

        if (resolved is CosDictionary or CosStream && document.Functions.Get(value, 1, 1) is { IsValid: true } single)
        {
            HasTransfer = true;
            Transfer = new PdfTransferFunction(resolved, [single]);
            return;
        }

        if (resolved is CosArray { Count: 4 } array)
        {
            var functions = new PdfFunction[4];
            for (int index = 0; index < 4; index++)
            {
                CosObject element = document.Resolve(array[index]);
                if (element is CosDictionary or CosStream && document.Functions.Get(array[index], 1, 1) is { IsValid: true } function)
                {
                    functions[index] = function;
                    continue;
                }

                Fail($"{key.Value} has an element that is not a function of one input and one output; ignored.");
                return;
            }

            HasTransfer = true;
            Transfer = new PdfTransferFunction(resolved, functions);
            return;
        }

        Fail($"{key.Value} is not a function, an array of four functions or Identity; ignored.");
    }

    private void ReadHalftone(CosObject? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is CosName name && name.Equals(Default))
        {
            HasHalftone = true;
            return;
        }

        if (value is CosDictionary or CosStream)
        {
            HasHalftone = true;
            Halftone = value;
            return;
        }

        Fail("HT is not a halftone dictionary, stream or Default; ignored.");
    }

    /// <summary>BM: a name, or (deprecated) an array whose first recognised name is used; unknown names are Normal.</summary>
    private void ReadBlendMode(PdfDocument document, CosObject? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is CosName name)
        {
            BlendMode = ParseBlendMode(name.Bytes);
        }
        else if (value is CosArray array)
        {
            foreach (CosObject element in array)
            {
                if (document.Resolve(element) is CosName candidate && ParseBlendMode(candidate.Bytes) is { } mode)
                {
                    BlendMode = mode;
                    break;
                }
            }
        }

        if (BlendMode is null)
        {
            Fail("BM names no blend mode of Tables 134 and 135; Normal is used.");
            BlendMode = Graphics.BlendMode.Normal;
        }
    }

    private void ReadSoftMask(PdfDocument document, CosDictionary dictionary)
    {
        if (!dictionary.TryGetValue(SMask, out CosObject? value))
        {
            return;
        }

        CosObject resolved = document.Resolve(value);
        if (resolved is CosName name && name.Equals(None))
        {
            HasSoftMask = true;
            return;
        }

        if (resolved is CosDictionary maskDictionary)
        {
            var mask = new PdfSoftMask(document, maskDictionary, value as CosReference);
            if (mask.IsValid)
            {
                HasSoftMask = true;
                SoftMask = mask;
                return;
            }
        }

        Fail("SMask is neither None nor a soft-mask dictionary with S Alpha or Luminosity and a group G; ignored.");
    }

    private double? Number(CosObject? value, string key)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CosNumber number && double.IsFinite(number.ToDouble()))
        {
            return number.ToDouble();
        }

        Fail($"{key} is not a number; ignored.");
        return null;
    }

    private bool? Boolean(CosObject? value, string key)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CosBoolean boolean)
        {
            return boolean.Value;
        }

        Fail($"{key} is not a boolean; ignored.");
        return null;
    }

    private int? Style(CosObject? value, string key)
    {
        if (Number(value, key) is not { } style)
        {
            return null;
        }

        if (style is 0 or 1 or 2)
        {
            return (int)style;
        }

        Fail($"{key} is not 0, 1 or 2; 0 is used.");
        return 0;
    }

    private double Clamp(double value, double minimum, double maximum, string key)
    {
        if (value < minimum || value > maximum)
        {
            Fail(new StringBuilder(key).Append(" is outside ").Append(minimum).Append(" to ").Append(maximum).Append("; clipped into range.").ToString());
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private void Fail(string problem) => Problem ??= problem;
}
