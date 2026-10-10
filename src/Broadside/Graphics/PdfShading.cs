using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics.Functions;
using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>
/// A shading: a smooth transition between colours across an area, resolved from its shading dictionary (or stream) into a
/// device-independent model that any backend can draw.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.3 (Table 77, the entries common to all shadings) and §8.7.4.5. Get one with
/// <see cref="PdfDocument.GetShading"/>, from a pattern (<see cref="PdfShadingPattern.Shading"/>) or from the <c>sh</c> operator
/// (<see cref="Content.ShadingEvent.Model"/>). The subclass says which of the seven types it is. Coordinates are in the shading's
/// target space: the current user space for <c>sh</c>, pattern space in a shading pattern.
/// </para>
/// <para>
/// A model is an immutable snapshot of the object when it was read, not a live view: it is cached per document and read again only
/// after the object changes through the public API. Colours stay in the shading's colour space: nothing is converted here. A shading
/// that cannot be used (a missing or Pattern colour space, a missing function, malformed coordinates) has <see cref="IsValid"/>
/// <see langword="false"/>, paints nothing, and the reason is in the document's diagnostics.
/// </para>
/// </remarks>
public abstract class PdfShading
{
    private readonly FunctionEvaluator? _evaluator;
    private readonly ComponentRange[] _ranges;
    private readonly PdfDocument _document;

    private protected PdfShading(ShadingReader reader, PdfShadingType type)
    {
        _document = reader.Document;
        ShadingType = type;
        CosObject = reader.CosObject;
        Dictionary = reader.Dictionary;
        Reference = reader.Reference;
        DiagnosticReference = reader.DiagnosticReference;
        Diagnostics = reader.Diagnostics;

        PdfColorSpace? space = ReadColorSpace(reader);
        ColorSpace = space ?? PdfDeviceGrayColorSpace.Instance;
        InterpolationColorSpace = ColorSpace is PdfIndexedColorSpace indexed ? indexed.Base : ColorSpace;
        _ranges = new ComponentRange[Math.Min(InterpolationColorSpace.ComponentCount, PdfColor.MaxComponents)];
        for (int i = 0; i < _ranges.Length; i++)
        {
            _ranges[i] = InterpolationColorSpace.GetComponentRange(i);
        }

        (_evaluator, Functions) = ReadFunction(reader, type, space);
        if (space is PdfIndexedColorSpace && (type <= PdfShadingType.Radial || _evaluator is not null))
        {
            reader.Invalid(DiagnosticCodes.ShadingColorSpaceInvalid, "An Indexed colour space is not allowed in a shading of Types 1 to 3 or in one with a Function.");
        }

        Background = ReadBackground(reader, ColorSpace) is { } background ? Array.AsReadOnly(background) : null;
        BoundingBox = reader.Rectangle(ShadingNames.BBox, DiagnosticCodes.ShadingEntryInvalid);
        AntiAlias = reader.Boolean(ShadingNames.AntiAlias, false, DiagnosticCodes.ShadingEntryInvalid);
    }

    /// <summary>Gets the shading's type, its <c>ShadingType</c> entry.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77.</remarks>
    public PdfShadingType ShadingType { get; }

    /// <summary>Gets the shading object: a <see cref="CosDictionary"/> (Types 1 to 3) or a <see cref="CosStream"/> (Types 4 to 7).</summary>
    public CosObject CosObject { get; }

    /// <summary>Gets the shading dictionary: the dictionary itself, or the stream's dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference the shading was first reached through, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the earliest PDF version that has shadings: 1.3.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (ADR 0003).</remarks>
    public PdfVersion MinimumVersion { get; } = new(1, 3);

    /// <summary>Gets the colour space the shading's colours are given in (<c>ColorSpace</c>); DeviceGray when it is unusable.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77: any colour space except Pattern.</remarks>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>
    /// Gets the colour space the shading's colours are interpolated in, and the space of every colour this model stores or
    /// <see cref="EvaluateFunction"/> returns: <see cref="ColorSpace"/>, or the base space of an Indexed colour space.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.4: Indexed colour values "shall be immediately converted to the base colour space".</remarks>
    public PdfColorSpace InterpolationColorSpace { get; }

    /// <summary>Gets the number of components of a colour of the shading, in <see cref="InterpolationColorSpace"/>.</summary>
    public int ColorComponentCount => _ranges.Length;

    /// <summary>
    /// Gets the colour (in <see cref="ColorSpace"/>) that fills the area outside the shading's bounds when it is used as a shading
    /// pattern, or <see langword="null"/>. The <c>sh</c> operator ignores it.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>Background</c>), and Table 76.</remarks>
    public IReadOnlyList<float>? Background { get; }

    /// <summary>Gets the shading's bounding box in its target space, an extra clip; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>BBox</c>).</remarks>
    public PdfRectangle? BoundingBox { get; }

    /// <summary>Gets a value indicating whether the function should be filtered to prevent aliasing (a hint). Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>AntiAlias</c>).</remarks>
    public bool AntiAlias { get; }

    /// <summary>
    /// Gets the shading's functions (<c>Function</c>): one function with n outputs, or n functions with one output each; empty
    /// when the shading has none (meshes may give colours at their vertices instead).
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.5, Tables 78 to 83.</remarks>
    public IReadOnlyList<PdfFunction> Functions { get; }

    /// <summary>Gets a value indicating whether the shading has a function, so colours come from <see cref="EvaluateFunction"/>.</summary>
    public bool HasFunction => _evaluator is not null;

    /// <summary>Gets a value indicating whether the shading can be painted; when <see langword="false"/> it paints nothing.</summary>
    public bool IsValid { get; private protected set; }

    /// <summary>Gets the reference diagnostics about the shading are recorded on.</summary>
    internal CosReference? DiagnosticReference { get; }

    /// <summary>Gets the recorder of this shading's diagnostics, on <see cref="DiagnosticReference"/>.</summary>
    private protected ObjectDiagnostics Diagnostics { get; }

    /// <summary>Gets the document the shading belongs to.</summary>
    internal PdfDocument Document => _document;

    /// <summary>Evaluates the shading's function: the colour, in <see cref="InterpolationColorSpace"/>, for a parametric value.</summary>
    /// <param name="input">
    /// The function's input: (x, y) in the domain for a function-based shading (Type 1), t for the others.
    /// </param>
    /// <param name="color">Receives <see cref="ColorComponentCount"/> components, each clipped to its component's range.</param>
    /// <exception cref="InvalidOperationException">The shading has no function.</exception>
    /// <exception cref="ArgumentException">A span is too short.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.5: "If the value returned by the function for a given colour component is out of range, it shall be
    /// adjusted to the nearest valid value." Thread-safe and allocation-free. A function that meets an error while evaluating
    /// records one diagnostic and yields its repaired outputs.
    /// </remarks>
    public void EvaluateFunction(ReadOnlySpan<float> input, Span<float> color)
    {
        FunctionEvaluator evaluator = _evaluator ?? throw new InvalidOperationException("The shading has no function.");
        if (input.Length < evaluator.InputCount)
        {
            throw new ArgumentException("The input is shorter than the function's inputs.", nameof(input));
        }

        if (color.Length < _ranges.Length)
        {
            throw new ArgumentException("The destination is shorter than the shading's colour components.", nameof(color));
        }

        Span<float> outputs = stackalloc float[FunctionEvaluator.MaxArity];
        FunctionStatus status = evaluator.Evaluate(input, outputs);
        if (status != FunctionStatus.Ok)
        {
            Diagnostics.ReportOnce(DiagnosticCodes.FunctionEvaluationRepaired, "A shading's function met an error while evaluating; its outputs were repaired.");
        }

        for (int i = 0; i < _ranges.Length; i++)
        {
            color[i] = (float)_ranges[i].Clamp(outputs[i]);
        }
    }

    /// <summary>Clips a colour component to its range in <see cref="InterpolationColorSpace"/>.</summary>
    internal float ClampComponent(int index, double value) => (float)_ranges[index].Clamp(value);

    private static PdfColorSpace? ReadColorSpace(ShadingReader reader)
    {
        CosObject? value = reader.Get(ShadingNames.ColorSpace);
        if (value is null && reader.Get(ShadingNames.CS) is { } abbreviated)
        {
            reader.Report(DiagnosticCodes.ShadingColorSpaceAbbreviated, "The shading names its colour space with CS instead of ColorSpace; it is used.");
            value = abbreviated;
        }

        if (value is null)
        {
            reader.Invalid(DiagnosticCodes.ShadingColorSpaceInvalid, "The shading has no ColorSpace entry.");
            return null;
        }

        PdfColorSpace? space = reader.Document.ColorSpaces.Find(value, reader.DiagnosticReference, inline: false, out _);
        if (space is null)
        {
            reader.Invalid(DiagnosticCodes.ShadingColorSpaceInvalid, "The shading's ColorSpace is not a colour space.");
            return null;
        }

        if (space.Family == PdfColorSpaceFamily.Pattern)
        {
            reader.Invalid(DiagnosticCodes.ShadingColorSpaceInvalid, "A shading's colour space shall not be a Pattern colour space.");
            return null;
        }

        if (space.ComponentCount > PdfColor.MaxComponents)
        {
            reader.Invalid(DiagnosticCodes.ShadingColorSpaceInvalid, "The shading's colour space has more than 32 components.");
            return null;
        }

        return space;
    }

    private static (FunctionEvaluator? Evaluator, IReadOnlyList<PdfFunction> Functions) ReadFunction(ShadingReader reader, PdfShadingType type, PdfColorSpace? space)
    {
        CosObject? value = reader.Get(ShadingNames.Function);
        if (value is null)
        {
            if (type <= PdfShadingType.Radial)
            {
                reader.Invalid(DiagnosticCodes.ShadingFunctionInvalid, "The shading has no Function entry, which its type requires.");
            }

            return (null, []);
        }

        if (space is null or PdfIndexedColorSpace)
        {
            return (null, []);
        }

        int inputs = type == PdfShadingType.FunctionBased ? 2 : 1;
        int outputs = space.ComponentCount;
        FunctionCache functions = reader.Document.Functions;
        FunctionEvaluator? evaluator = functions.GetEvaluator(value, FunctionForms.Array, inputs, outputs);
        if (evaluator is not { IsValid: true } || evaluator.InputCount != inputs || evaluator.OutputCount < outputs)
        {
            reader.Invalid(
                DiagnosticCodes.ShadingFunctionInvalid,
                string.Create(CultureInfo.InvariantCulture, $"The shading's Function is not usable as a function of {inputs} input(s) and {outputs} output(s)."));
            return (null, []);
        }

        if (evaluator.OutputCount > outputs)
        {
            reader.Report(DiagnosticCodes.ShadingFunctionInvalid, "The shading's Function has more outputs than its colour space has components; the first ones are used.");
        }

        IReadOnlyList<PdfFunction> list = reader.Resolve(value) is CosArray array
            ? [.. array.Select(element => functions.Get(element, inputs, 1)).OfType<PdfFunction>()]
            : functions.Get(value, inputs, outputs) is { } single ? [single] : [];
        return (evaluator, list);
    }

    private static float[]? ReadBackground(ShadingReader reader, PdfColorSpace space)
    {
        if (reader.Get(ShadingNames.Background) is null)
        {
            return null;
        }

        double[]? numbers = reader.Numbers(ShadingNames.Background);
        if (numbers is null || numbers.Length != space.ComponentCount)
        {
            reader.Report(DiagnosticCodes.ShadingBackgroundInvalid, "The shading's Background is not one number per component of its colour space; it is ignored.");
            return null;
        }

        return [.. numbers.Select(number => (float)number)];
    }
}
