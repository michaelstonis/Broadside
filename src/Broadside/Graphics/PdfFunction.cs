using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// A function object: a live view over a function dictionary or stream that evaluates the function it describes.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10. Get one with <see cref="PdfDocument.GetFunction"/>; the subclass says which of the four types it is
/// (<see cref="PdfSampledFunction"/>, <see cref="PdfExponentialFunction"/>, <see cref="PdfStitchingFunction"/>,
/// <see cref="PdfPostScriptFunction"/>). Entries such as <see cref="Domain"/> are read from the COS object on every call (ADR 0004).
/// </para>
/// <para>
/// Evaluation runs on a form compiled once from the COS objects: inputs are clipped to the domain, outputs to the range (§7.10.1).
/// When any object it was compiled from changes through the public API, the next evaluation compiles it again. Evaluation is safe
/// from any number of threads while nobody mutates the document, allocates nothing, never returns NaN or infinity, and never throws
/// for anything in the file: a function that cannot be used (<see cref="IsValid"/> is <see langword="false"/>) yields 0 clipped to
/// its range. Deviations are recorded as the document's diagnostics: those found compiling, once; an error met while evaluating (a
/// Type 4 division by zero, for instance), once per compiled function. In strict mode both throw <c>DiagnosticException</c>.
/// </para>
/// </remarks>
public abstract class PdfFunction
{
    private readonly FunctionCache _cache;
    private readonly int _expectedInputs;
    private readonly int _expectedOutputs;
    private CompiledFunction _compiled;

    private protected PdfFunction(FunctionCache cache, CosObject cosObject, CosReference? reference, CompiledFunction compiled, int expectedInputs, int expectedOutputs)
    {
        _cache = cache;
        CosObject = cosObject;
        Reference = reference;
        _compiled = compiled;
        _expectedInputs = expectedInputs;
        _expectedOutputs = expectedOutputs;
    }

    /// <summary>Gets the function object: a <see cref="CosDictionary"/> (Types 2 and 3) or a <see cref="CosStream"/> (Types 0 and 4).</summary>
    public CosObject CosObject { get; }

    /// <summary>Gets the function dictionary: the dictionary itself, or the stream's dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.10.1: "function dictionary" means either.</remarks>
    public CosDictionary Dictionary => CosObject is CosStream stream ? stream.Dictionary : (CosDictionary)CosObject;

    /// <summary>Gets the indirect reference the function was reached through, or <see langword="null"/> for a direct object.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the function's type, its <c>FunctionType</c> entry.</summary>
    /// <remarks>ISO 32000-2 §7.10.1, Table 38.</remarks>
    public abstract PdfFunctionType FunctionType { get; }

    /// <summary>Gets the earliest PDF version that has this type of function: 1.2 for Type 0, 1.3 for the others.</summary>
    /// <remarks>ISO 32000-2 §7.10.1 (ADR 0003: a writer raises the file's version to this).</remarks>
    public PdfVersion MinimumVersion => FunctionType == PdfFunctionType.Sampled ? new PdfVersion(1, 2) : new PdfVersion(1, 3);

    /// <summary>Gets the <c>Domain</c> entry: 2 × m numbers, an interval per input; empty when it is missing or not numbers.</summary>
    /// <remarks>ISO 32000-2 §7.10.1, Table 38. Read from the dictionary on every call.</remarks>
    public IReadOnlyList<double> Domain => ReadNumbers(FunctionNames.Domain) ?? [];

    /// <summary>Gets the <c>Range</c> entry: 2 × n numbers, an interval per output; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §7.10.1, Table 38. Required for Types 0 and 4. Read from the dictionary on every call.</remarks>
    public IReadOnlyList<double>? Range => ReadNumbers(FunctionNames.Range);

    /// <summary>Gets m, the number of input values, as compiled.</summary>
    /// <remarks>ISO 32000-2 §7.10.1. From <c>Domain</c> (or, repaired, from the type: <c>Size</c>, or 1 for Types 2 and 3).</remarks>
    public int InputCount => Evaluator.InputCount;

    /// <summary>Gets n, the number of output values, as compiled.</summary>
    /// <remarks>ISO 32000-2 §7.10.1. From <c>Range</c> (Types 0 and 4), <c>C0</c> and <c>C1</c> (Type 2) or the stitched functions (Type 3).</remarks>
    public int OutputCount => Evaluator.OutputCount;

    /// <summary>Gets a value indicating whether the function could be compiled. An invalid function evaluates to 0 clipped to its range.</summary>
    public bool IsValid => Evaluator.IsValid;

    /// <summary>Gets the compiled form, compiling again when what it was compiled from has changed.</summary>
    internal FunctionEvaluator Evaluator => Current().Evaluator;

    /// <summary>Evaluates the function at one point.</summary>
    /// <param name="input">The m input values; values beyond <see cref="InputCount"/> are ignored.</param>
    /// <param name="output">Receives the n output values in its first <see cref="OutputCount"/> elements.</param>
    /// <exception cref="ArgumentException"><paramref name="input"/> or <paramref name="output"/> is shorter than the function needs.</exception>
    /// <remarks>ISO 32000-2 §7.10.1: inputs are clipped to the domain and outputs to the range.</remarks>
    public void Evaluate(ReadOnlySpan<float> input, Span<float> output)
    {
        CompiledFunction compiled = Current();
        FunctionEvaluator evaluator = compiled.Evaluator;
        if (input.Length < evaluator.InputCount)
        {
            throw new ArgumentException("The function takes more inputs than were given.", nameof(input));
        }

        if (output.Length < evaluator.OutputCount)
        {
            throw new ArgumentException("The function has more outputs than there is room for.", nameof(output));
        }

        Report(compiled, evaluator.Evaluate(input, output));
    }

    /// <summary>Evaluates the function at <paramref name="count"/> points stored one after the other.</summary>
    /// <param name="inputs">At least <paramref name="count"/> × <see cref="InputCount"/> values: the inputs of each point in turn.</param>
    /// <param name="outputs">At least <paramref name="count"/> × <see cref="OutputCount"/> values: receives the outputs of each point in turn.</param>
    /// <param name="count">The number of points.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="inputs"/> or <paramref name="outputs"/> is too short for <paramref name="count"/> points.</exception>
    /// <remarks>ISO 32000-2 §7.10.1. The same as evaluating each point, with one staleness check for the whole run.</remarks>
    public void Evaluate(ReadOnlySpan<float> inputs, Span<float> outputs, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CompiledFunction compiled = Current();
        FunctionEvaluator evaluator = compiled.Evaluator;
        if (inputs.Length < (long)count * evaluator.InputCount)
        {
            throw new ArgumentException("There are fewer inputs than the points need.", nameof(inputs));
        }

        if (outputs.Length < (long)count * evaluator.OutputCount)
        {
            throw new ArgumentException("There is less room for outputs than the points need.", nameof(outputs));
        }

        Report(compiled, evaluator.Evaluate(inputs, outputs, count));
    }

    /// <summary>Reads an entry of numbers from the dictionary as it is now.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The numbers, or <see langword="null"/> when absent or not an array of numbers.</returns>
    private protected double[]? ReadNumbers(CosName key)
    {
        if (!Dictionary.TryGetValue(key, out CosObject? value) || _cache.Resolve(value) is not CosArray array)
        {
            return null;
        }

        double[] numbers = new double[array.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            if (_cache.Resolve(array[i]) is not CosNumber number)
            {
                return null;
            }

            numbers[i] = number.ToDouble();
        }

        return numbers;
    }

    /// <summary>Reads a number entry from the dictionary as it is now.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The number, or <see langword="null"/> when absent or not a number.</returns>
    private protected double? ReadNumber(CosName key) =>
        Dictionary.TryGetValue(key, out CosObject? value) && _cache.Resolve(value) is CosNumber number ? number.ToDouble() : null;

    /// <summary>Gets the function cache, for views that reach other functions.</summary>
    private protected FunctionCache Cache => _cache;

    private CompiledFunction Current()
    {
        CompiledFunction compiled = Volatile.Read(ref _compiled);
        if (compiled.IsCurrent)
        {
            return compiled;
        }

        compiled = _cache.Compile(CosObject, Reference, FunctionType, _expectedInputs, _expectedOutputs);
        Volatile.Write(ref _compiled, compiled);
        return compiled;
    }

    private void Report(CompiledFunction compiled, FunctionStatus status)
    {
        // An invalid function was reported when it was compiled; only a valid one reports what went wrong while evaluating.
        if (status != FunctionStatus.Ok && compiled.Evaluator.IsValid && compiled.ClaimReport())
        {
            _cache.ReportEvaluation(this, status);
        }
    }
}
