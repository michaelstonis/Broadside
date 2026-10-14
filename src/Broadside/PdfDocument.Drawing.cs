using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside;

/// <summary>Writing graphics (issue #148): form XObjects, colour spaces and functions a <see cref="PdfCanvas"/> draws with.</summary>
public sealed partial class PdfDocument
{
    private DrawingCache? _drawing;

    /// <summary>Gets the objects the canvas and the factories below created, by what they hold, so the same values are written once.</summary>
    internal DrawingCache Drawing => _drawing ??= new DrawingCache(this);

    /// <summary>Creates an empty form XObject that a <see cref="PdfCanvas"/> can draw on and draw.</summary>
    /// <param name="boundingBox">The form's bounding box (<c>BBox</c>), in form space.</param>
    /// <returns>The form: a new indirect stream <c>&lt;&lt; /Type /XObject /Subtype /Form /BBox [...] /Resources &lt;&lt;&gt;&gt; &gt;&gt;</c> with no content.</returns>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="boundingBox"/> is not finite.</exception>
    /// <remarks>ISO 32000-2 §8.10.1, Table 93 (PDF 1.0). Draw on it with <see cref="PdfFormXObject.CreateCanvas"/>; paint it with <see cref="PdfCanvas.DrawForm"/>. A mutation.</remarks>
    public PdfFormXObject CreateFormXObject(PdfRectangle boundingBox)
    {
        ThrowIfNotFinite(boundingBox.Left, boundingBox.Bottom, boundingBox.Right, boundingBox.Top);
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = new CosName("XObject"),
            [new CosName("Subtype")] = new CosName("Form"),
            [new CosName("BBox")] = DrawingCache.Rectangle(boundingBox),
            [KnownNames.Resources] = new CosDictionary(),
        };
        var stream = new CosStream(dictionary, ReadOnlyMemory<byte>.Empty);
        return new PdfFormXObject(this, stream, AddObject(stream));
    }

    /// <summary>Creates an ICCBased colour space from an ICC profile, or returns the one this document already has for the same profile.</summary>
    /// <param name="profile">The profile's bytes: an input, display, output or colour space profile of a gray, RGB, CMYK or Lab data colour space, version 2 to 4.</param>
    /// <returns>The space: <c>[/ICCBased stream]</c>, the stream holding the profile with <c>N</c> its number of components; array and stream are indirect objects.</returns>
    /// <exception cref="ArgumentException">The profile has no valid header, or PDF does not allow its class, data colour space or version (Table 67).</exception>
    /// <remarks>
    /// ISO 32000-2 §8.6.5.5, Tables 66 and 67 (PDF 1.3). No <c>Alternate</c> is written: a reader uses the device space with the same
    /// number of components. The stream is written unfiltered; a save compresses it. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfIccBasedColorSpace CreateIccBasedColorSpace(ReadOnlySpan<byte> profile)
    {
        if (IccProfileHeader.Parse(profile) is not { IsSupportedForPdf: true } header)
        {
            throw new ArgumentException(
                "The profile is not one PDF allows in an ICCBased colour space: a version 2 to 4 input, display, output or colour space profile of gray, RGB, CMYK or Lab data (ISO 32000-2 §8.6.5.5, Table 67).",
                nameof(profile));
        }

        RequireFeature(VersionedFeatures.IccBasedColorSpace);
        byte[] bytes = profile.ToArray();
        string key = "ICC|" + Convert.ToHexString(SHA256.HashData(bytes));
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray
        {
            ColorSpaceNames.IccBased,
            AddObject(new CosStream(new CosDictionary { [ColorSpaceNames.N] = new CosInteger(header.ComponentCount) }, bytes)),
        });
        return (PdfIccBasedColorSpace)GetColorSpace(reference);
    }

    /// <summary>Creates a Separation colour space whose full tint looks like <paramref name="alternateColor"/>, or returns the one this document already has for the same values.</summary>
    /// <param name="colorant">The colourant's name, such as a spot ink's; <c>All</c> or <c>None</c> for the special colourants.</param>
    /// <param name="alternateColor">
    /// The colour a tint of 1.0 is shown as where the colourant is not available, in a device space, CalGray, CalRGB or a gray, RGB
    /// or CMYK ICCBased space; a tint of 0.0 is no ink (white).
    /// </param>
    /// <returns>The space: <c>[/Separation /colorant alternate tintTransform]</c>, an indirect array.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="colorant"/> is empty, or the alternate space is a special space, a space of another document, or one whose
    /// "no ink" colour is not known (Lab, a Lab ICC profile): give a tint transform with the other overload.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §8.6.6.4 (PDF 1.2). The tint transform is written as an exponential function (§7.10.3, Type 2, PDF 1.3) from the
    /// alternate's white (all 0 for CMYK, all 1 for additive spaces) at tint 0 to <paramref name="alternateColor"/> at tint 1, with
    /// exponent 1. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfSeparationColorSpace CreateSeparationColorSpace(string colorant, PdfColor alternateColor)
    {
        CosName name = Colorant(colorant, nameof(colorant));
        PdfColorSpace alternate = alternateColor.ColorSpace;
        ThrowIfNotAlternate(alternate, nameof(alternateColor));
        bool subtractive = IsSubtractive(alternate, nameof(alternateColor));
        int count = alternate.ComponentCount;
        var c0 = new CosArray();
        var c1 = new CosArray();
        for (int index = 0; index < count; index++)
        {
            c0.Add(new CosInteger(subtractive ? 0 : 1));
            c1.Add(DrawingCache.Number(alternateColor.Components[index]));
        }

        var function = new CosDictionary
        {
            [FunctionNames.FunctionType] = new CosInteger(2),
            [FunctionNames.Domain] = new CosArray { new CosInteger(0), new CosInteger(1) },
            [FunctionNames.C0] = c0,
            [FunctionNames.C1] = c1,
            [FunctionNames.N] = new CosInteger(1),
        };
        RequireFeature(VersionedFeatures.SeparationColorSpace);
        RequireFeature(VersionedFeatures.CalculatedFunction);
        string? key = DrawingCache.Key(alternate) is { } alternateKey
            ? $"Separation|{colorant}|{alternateKey}|{string.Join(' ', alternateColor.Components.ToArray().Select(static c => c.ToString("R", CultureInfo.InvariantCulture)))}"
            : null;
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray { ColorSpaceNames.Separation, name, DrawingCache.Value(alternate), function });
        return (PdfSeparationColorSpace)GetColorSpace(reference);
    }

    /// <summary>Creates a Separation colour space with a tint transform of the caller's, or returns the one this document already has for the same values.</summary>
    /// <param name="colorant">The colourant's name; <c>All</c> or <c>None</c> for the special colourants.</param>
    /// <param name="alternate">The alternate space: a device or CIE-based space of this document, not a special space.</param>
    /// <param name="tintTransform">A function of this document from one tint to the alternate's components (see <see cref="CreateExponentialFunction"/> and <see cref="CreatePostScriptFunction"/>).</param>
    /// <returns>The space, an indirect array.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="colorant"/> is empty; <paramref name="alternate"/> is a special space or another document's; the function is
    /// another document's, cannot be used, or does not take 1 input to the alternate's number of components.
    /// </exception>
    /// <remarks>ISO 32000-2 §8.6.6.4 (PDF 1.2). Records PDF 1.2 and the function's version (§7.10). A mutation.</remarks>
    public PdfSeparationColorSpace CreateSeparationColorSpace(string colorant, PdfColorSpace alternate, PdfFunction tintTransform)
    {
        CosName name = Colorant(colorant, nameof(colorant));
        ArgumentNullException.ThrowIfNull(alternate);
        ThrowIfNotAlternate(alternate, nameof(alternate));
        CosObject function = TintTransform(tintTransform, 1, alternate.ComponentCount);
        RequireFeature(VersionedFeatures.SeparationColorSpace);
        string? key = DrawingCache.Key(alternate) is { } alternateKey && tintTransform.Reference is { } functionReference
            ? $"Separation|{colorant}|{alternateKey}|{DrawingCache.Key(functionReference)}"
            : null;
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray { ColorSpaceNames.Separation, name, DrawingCache.Value(alternate), function });
        return (PdfSeparationColorSpace)GetColorSpace(reference);
    }

    /// <summary>
    /// Creates a DeviceN colour space whose colourants look like <paramref name="alternateColors"/> at full tint, or returns the one
    /// this document already has for the same values.
    /// </summary>
    /// <param name="colorants">The colourants' names, in component order: different from each other except <c>None</c>; 1 to 32 of them.</param>
    /// <param name="alternateColors">
    /// For each colourant, the colour its full tint is shown as where it is not available; all in one space, which is the alternate:
    /// a device space, CalGray, CalRGB or a gray, RGB or CMYK ICCBased space.
    /// </param>
    /// <returns>The space: <c>[/DeviceN [names] alternate tintTransform]</c>, an indirect array.</returns>
    /// <exception cref="ArgumentException">
    /// The counts differ or are outside 1 to 32; a name is empty or repeated; the colours are in different spaces, or in a space the
    /// other overload is needed for (as for <see cref="CreateSeparationColorSpace(string, PdfColor)"/>).
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §8.6.6.5 (PDF 1.3). The tint transform is written as a PostScript calculator function (§7.10.5, Type 4, PDF 1.3)
    /// that mixes the colourants' alternate colours: for CMYK, each component is the sum of the tints times the colourants'
    /// components, at most 1; for an additive alternate, 1 minus the sum of the tints times what each colourant takes away from
    /// white, at least 0. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfDeviceNColorSpace CreateDeviceNColorSpace(IReadOnlyList<string> colorants, IReadOnlyList<PdfColor> alternateColors)
    {
        ArgumentNullException.ThrowIfNull(colorants);
        ArgumentNullException.ThrowIfNull(alternateColors);
        CosArray names = Colorants(colorants);
        if (alternateColors.Count != colorants.Count)
        {
            throw new ArgumentException("Give one alternate colour per colourant.", nameof(alternateColors));
        }

        PdfColorSpace alternate = alternateColors[0].ColorSpace;
        if (alternateColors.Any(color => !ReferenceEquals(color.ColorSpace, alternate)))
        {
            throw new ArgumentException("The alternate colours shall all be in one colour space.", nameof(alternateColors));
        }

        ThrowIfNotAlternate(alternate, nameof(alternateColors));
        bool subtractive = IsSubtractive(alternate, nameof(alternateColors));
        int n = colorants.Count;
        int m = alternate.ComponentCount;
        var program = new StringBuilder("{ ");
        for (int j = 0; j < m; j++)
        {
            program.Append("0 ");
            for (int i = 0; i < n; i++)
            {
                double ink = subtractive ? alternateColors[i].Components[j] : 1 - alternateColors[i].Components[j];
                if (ink != 0)
                {
                    program.Append(CultureInfo.InvariantCulture, $"{n + j - i} index {DrawingCache.Format(ink)} mul add ");
                }
            }

            program.Append(subtractive ? "dup 1 gt { pop 1 } if " : "1 exch sub dup 0 lt { pop 0 } if ");
        }

        program.Append(CultureInfo.InvariantCulture, $"{n + m} {m} roll ");
        for (int i = 0; i < n; i++)
        {
            program.Append("pop ");
        }

        program.Append('}');
        CosStream function = PostScriptFunctionStream(UnitRanges(n), UnitRanges(m), program.ToString());
        RequireFeature(VersionedFeatures.DeviceNColorSpace);
        RequireFeature(VersionedFeatures.CalculatedFunction);
        string? key = DrawingCache.Key(alternate) is { } alternateKey
            ? $"DeviceN|{string.Join('\u0001', colorants)}|{alternateKey}|{program}"
            : null;
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray { ColorSpaceNames.DeviceN, names, DrawingCache.Value(alternate), AddObject(function) });
        return (PdfDeviceNColorSpace)GetColorSpace(reference);
    }

    /// <summary>Creates a DeviceN colour space with a tint transform of the caller's, or returns the one this document already has for the same values.</summary>
    /// <param name="colorants">The colourants' names, in component order: different from each other except <c>None</c>; 1 to 32 of them.</param>
    /// <param name="alternate">The alternate space: a device or CIE-based space of this document, not a special space.</param>
    /// <param name="tintTransform">A function of this document from the tints to the alternate's components.</param>
    /// <returns>The space, an indirect array.</returns>
    /// <exception cref="ArgumentException">
    /// A name is empty or repeated, or there are not 1 to 32; <paramref name="alternate"/> is a special space or another document's;
    /// the function is another document's, cannot be used, or does not take one input per colourant to the alternate's components.
    /// </exception>
    /// <remarks>ISO 32000-2 §8.6.6.5 (PDF 1.3). Records PDF 1.3 and the function's version (§7.10). A mutation.</remarks>
    public PdfDeviceNColorSpace CreateDeviceNColorSpace(IReadOnlyList<string> colorants, PdfColorSpace alternate, PdfFunction tintTransform)
    {
        ArgumentNullException.ThrowIfNull(colorants);
        ArgumentNullException.ThrowIfNull(alternate);
        CosArray names = Colorants(colorants);
        ThrowIfNotAlternate(alternate, nameof(alternate));
        CosObject function = TintTransform(tintTransform, colorants.Count, alternate.ComponentCount);
        RequireFeature(VersionedFeatures.DeviceNColorSpace);
        string? key = DrawingCache.Key(alternate) is { } alternateKey && tintTransform.Reference is { } functionReference
            ? $"DeviceN|{string.Join('\u0001', colorants)}|{alternateKey}|{DrawingCache.Key(functionReference)}"
            : null;
        CosReference reference = Drawing.GetOrAdd(key, () => new CosArray { ColorSpaceNames.DeviceN, names, DrawingCache.Value(alternate), function });
        return (PdfDeviceNColorSpace)GetColorSpace(reference);
    }

    /// <summary>Creates an exponential interpolation function of one input over the domain 0 to 1.</summary>
    /// <param name="c0">The outputs at input 0.</param>
    /// <param name="c1">The outputs at input 1; as many as <paramref name="c0"/>.</param>
    /// <param name="exponent">The interpolation exponent <c>N</c>; 1 for linear interpolation.</param>
    /// <returns>The function: a new indirect dictionary <c>&lt;&lt; /FunctionType 2 /Domain [0 1] /C0 [...] /C1 [...] /N n &gt;&gt;</c>.</returns>
    /// <exception cref="ArgumentException">The arrays are empty or of different lengths, or a value is not finite.</exception>
    /// <remarks>ISO 32000-2 §7.10.3, Table 40 (PDF 1.3). Records PDF 1.3. A mutation.</remarks>
    public PdfExponentialFunction CreateExponentialFunction(ReadOnlySpan<double> c0, ReadOnlySpan<double> c1, double exponent)
    {
        if (c0.IsEmpty || c0.Length != c1.Length)
        {
            throw new ArgumentException("C0 and C1 shall have the same, non-zero number of values.", nameof(c1));
        }

        ThrowIfNotFinite([.. c0, .. c1, exponent]);
        var function = new CosDictionary
        {
            [FunctionNames.FunctionType] = new CosInteger(2),
            [FunctionNames.Domain] = new CosArray { new CosInteger(0), new CosInteger(1) },
            [FunctionNames.C0] = DrawingCache.Numbers(c0),
            [FunctionNames.C1] = DrawingCache.Numbers(c1),
            [FunctionNames.N] = DrawingCache.Number(exponent),
        };
        RequireFeature(VersionedFeatures.CalculatedFunction);
        return (PdfExponentialFunction)GetFunction(AddObject(function))!;
    }

    /// <summary>Creates a PostScript calculator function.</summary>
    /// <param name="domain">The inputs' ranges: a minimum and a maximum per input.</param>
    /// <param name="range">The outputs' ranges: a minimum and a maximum per output.</param>
    /// <param name="program">The program, in braces, such as <c>{ 1 exch sub }</c>; it uses only the operators Table 42 lists.</param>
    /// <returns>The function: a new indirect stream <c>&lt;&lt; /FunctionType 4 /Domain [...] /Range [...] &gt;&gt;</c> holding the program.</returns>
    /// <exception cref="ArgumentException">A range array is empty or of odd length, a value is not finite, or the program does not compile.</exception>
    /// <remarks>ISO 32000-2 §7.10.5, Tables 41 and 42 (PDF 1.3). The program is compiled before the object is added. Records PDF 1.3. A mutation.</remarks>
    public PdfPostScriptFunction CreatePostScriptFunction(ReadOnlySpan<double> domain, ReadOnlySpan<double> range, string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        CosStream function = PostScriptFunctionStream(domain, range, program);
        RequireFeature(VersionedFeatures.CalculatedFunction);
        return (PdfPostScriptFunction)GetFunction(AddObject(function))!;
    }

    private static void ThrowIfNotFinite(params ReadOnlySpan<double> values)
    {
        foreach (double value in values)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Values shall be finite numbers.", nameof(values));
            }
        }
    }

    private static double[] UnitRanges(int count)
    {
        double[] ranges = new double[2 * count];
        for (int index = 0; index < count; index++)
        {
            ranges[(2 * index) + 1] = 1;
        }

        return ranges;
    }

    private static CosName Colorant(string colorant, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(colorant, parameter);
        return new CosName(colorant);
    }

    private static CosArray Colorants(IReadOnlyList<string> colorants)
    {
        if (colorants.Count is < 1 or > PdfColor.MaxComponents)
        {
            throw new ArgumentException($"A DeviceN space has 1 to {PdfColor.MaxComponents} colourants.", nameof(colorants));
        }

        var names = new CosArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string colorant in colorants)
        {
            names.Add(Colorant(colorant, nameof(colorants)));
            if (colorant != "None" && !seen.Add(colorant))
            {
                throw new ArgumentException($"The colourant {colorant} is named twice; names shall be different except None (ISO 32000-2 §8.6.6.5).", nameof(colorants));
            }
        }

        return names;
    }

    /// <summary>Whether a tint of 0 is all 0 (CMYK) or all 1 (additive spaces) in <paramref name="alternate"/>.</summary>
    private static bool IsSubtractive(PdfColorSpace alternate, string parameter) => alternate switch
    {
        { Family: PdfColorSpaceFamily.DeviceCmyk } => true,
        { Family: PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.CalGray or PdfColorSpaceFamily.CalRgb } => false,
        PdfIccBasedColorSpace { IsLabProfile: false, ComponentCount: 4 } => true,
        PdfIccBasedColorSpace { IsLabProfile: false, ComponentCount: 1 or 3 } => false,
        _ => throw new ArgumentException(
            "The alternate space's colour for no ink is not known (a Lab space or a Lab profile): give a tint transform.", parameter),
    };

    private void ThrowIfNotAlternate(PdfColorSpace alternate, string parameter)
    {
        if (alternate.Cache is { } cache && !ReferenceEquals(cache.Document, this))
        {
            throw new ArgumentException("The alternate colour space belongs to another document.", parameter);
        }

        if (!TintSpaces.IsAlternateFamily(alternate))
        {
            throw new ArgumentException(
                "The alternate space shall be a device or CIE-based colour space, not Pattern, Indexed, Separation or DeviceN (ISO 32000-2 §8.6.6.4).", parameter);
        }
    }

    private CosObject TintTransform(PdfFunction function, int inputs, int outputs)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ReferenceEquals(function.Document, this))
        {
            throw new ArgumentException("The tint transform belongs to another document.", nameof(function));
        }

        if (!function.IsValid || function.InputCount != inputs || function.OutputCount != outputs)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The tint transform shall be a usable function of {inputs} input(s) to {outputs} output(s)."),
                nameof(function));
        }

        RequireFeature(function.FunctionType == PdfFunctionType.Sampled ? VersionedFeatures.SampledFunction : VersionedFeatures.CalculatedFunction);
        return (CosObject?)function.Reference ?? function.CosObject;
    }

    /// <summary>Builds a type 4 function stream and compiles it, without recording anything on the document.</summary>
    private CosStream PostScriptFunctionStream(ReadOnlySpan<double> domain, ReadOnlySpan<double> range, string program)
    {
        if (domain.IsEmpty || domain.Length % 2 != 0 || range.IsEmpty || range.Length % 2 != 0)
        {
            throw new ArgumentException("Domain and Range shall hold a minimum and a maximum per input and output.", nameof(range));
        }

        ThrowIfNotFinite([.. domain, .. range]);
        var stream = new CosStream(
            new CosDictionary
            {
                [FunctionNames.FunctionType] = new CosInteger(4),
                [FunctionNames.Domain] = DrawingCache.Numbers(domain),
                [FunctionNames.Range] = DrawingCache.Numbers(range),
            },
            Encoding.ASCII.GetBytes(program));
        var check = new DiagnosticSink(strict: false);
        FunctionEvaluator evaluator = FunctionCompiler.Compile(stream, new FunctionCompilation(this, check), PdfFunctionType.PostScriptCalculator, 0, 0);
        if (check.Snapshot() is [Diagnostic first, ..])
        {
            throw new ArgumentException($"The PostScript calculator program does not compile: {first.Message}", nameof(program));
        }

        if (!evaluator.IsValid)
        {
            throw new ArgumentException("The PostScript calculator program does not compile.", nameof(program));
        }

        return stream;
    }
}
