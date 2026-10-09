using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Functions;

/// <summary>
/// The state of compiling one function (and the functions it contains): the document it reads through, the diagnostics it reports,
/// the containers it read (for staleness), and the stitching functions being compiled (for cycles and depth).
/// </summary>
/// <remarks>ISO 32000-2 §7.10. Used on one thread; a compilation is never shared.</remarks>
internal sealed class FunctionCompilation
{
    /// <summary>How deeply stitching functions may nest before the innermost is rejected.</summary>
    public const int MaxDepth = 8;

    private readonly PdfDocument _document;
    private readonly DiagnosticSink _diagnostics;
    private readonly List<FunctionDependency> _dependencies = [];
    private readonly HashSet<CosObject> _active = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="FunctionCompilation"/> class.</summary>
    /// <param name="document">The document the function belongs to.</param>
    /// <param name="diagnostics">The document's diagnostics.</param>
    public FunctionCompilation(PdfDocument document, DiagnosticSink diagnostics)
    {
        _document = document;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets or sets the reference of the function being compiled, for diagnostics; <see langword="null"/> for a direct one.</summary>
    public CosReference? Reference { get; set; }

    /// <summary>Gets the number of functions being compiled, outermost first.</summary>
    public int Depth => _active.Count;

    /// <summary>Gets the containers read so far, with their versions.</summary>
    /// <returns>The dependencies.</returns>
    public FunctionDependency[] Dependencies() => [.. _dependencies];

    /// <summary>Marks <paramref name="function"/> as being compiled.</summary>
    /// <param name="function">The function's dictionary or stream.</param>
    /// <returns><see langword="false"/> when it is already being compiled further out: the function contains itself.</returns>
    public bool Enter(CosObject function) => _active.Add(function);

    /// <summary>Marks <paramref name="function"/> as compiled.</summary>
    /// <param name="function">The function's dictionary or stream.</param>
    public void Leave(CosObject function) => _active.Remove(function);

    /// <summary>Resolves <paramref name="value"/> through the document and records it when it is a container.</summary>
    /// <param name="value">A direct object or a reference.</param>
    /// <returns>The direct object; <see cref="CosNull"/> for a missing one.</returns>
    public CosObject Resolve(CosObject? value)
    {
        CosObject resolved = _document.Resolve(value);
        Track(resolved);
        return resolved;
    }

    /// <summary>Records <paramref name="value"/> as read, when it is a container.</summary>
    /// <param name="value">The object.</param>
    public void Track(CosObject value)
    {
        if (value is CosDictionary or CosArray or CosStream)
        {
            _dependencies.Add(new FunctionDependency(value, FunctionDependency.VersionOf(value)));
        }
    }

    /// <summary>Decodes a stream's data through its filters.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The decoded data.</returns>
    public ReadOnlyMemory<byte> Decode(CosStream stream) => _document.DecodeStream(stream);

    /// <summary>Records a diagnostic against the function being compiled.</summary>
    /// <param name="code">The code.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="message">What was wrong and what was done.</param>
    public void Report(string code, DiagnosticSeverity severity, string message) =>
        _diagnostics.Report(code, severity, message, offset: null, Reference);

    /// <summary>Records a repaired entry (<see cref="DiagnosticCodes.FunctionEntryInvalid"/>, Warning).</summary>
    /// <param name="message">What was wrong and what was done.</param>
    public void Repaired(string message) => Report(DiagnosticCodes.FunctionEntryInvalid, DiagnosticSeverity.Warning, message);

    /// <summary>Records that the function cannot be used (<see cref="DiagnosticCodes.FunctionInvalid"/>, Error).</summary>
    /// <param name="message">Why.</param>
    public void Invalid(string message) => Report(DiagnosticCodes.FunctionInvalid, DiagnosticSeverity.Error, message);

    /// <summary>Reads an entry that holds an array of numbers.</summary>
    /// <param name="dictionary">The function dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The numbers; <see langword="null"/> when absent, or (with a diagnostic) when not an array of numbers.</returns>
    public double[]? ReadNumbers(CosDictionary dictionary, CosName key)
    {
        if (!dictionary.TryGetValue(key, out CosObject? value))
        {
            return null;
        }

        if (Resolve(value) is not CosArray array)
        {
            Repaired($"{key.Value} is not an array; it is ignored.");
            return null;
        }

        double[] numbers = new double[array.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            if (Resolve(array[i]) is not CosNumber number)
            {
                Repaired($"{key.Value} holds something other than a number; it is ignored.");
                return null;
            }

            numbers[i] = number.ToDouble();
        }

        return numbers;
    }

    /// <summary>Reads an entry that holds a number.</summary>
    /// <param name="dictionary">The function dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number; <see langword="null"/> when absent or not a number.</returns>
    public double? ReadNumber(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && Resolve(value) is CosNumber number ? number.ToDouble() : null;

    /// <summary>Reads the Domain entry (Table 38).</summary>
    /// <param name="dictionary">The function dictionary.</param>
    /// <param name="inferredInputs">The number of inputs to assume, with a diagnostic, when Domain is missing; 0 when it cannot be inferred.</param>
    /// <returns>2 × m numbers; <see langword="null"/> (with a diagnostic) when there is no usable domain.</returns>
    public double[]? ReadDomain(CosDictionary dictionary, int inferredInputs)
    {
        double[]? domain = ReadPairs(dictionary, FunctionNames.Domain);
        if (domain is { Length: > 0 })
        {
            if (domain.Length / 2 > FunctionEvaluator.MaxArity)
            {
                Invalid(string.Create(CultureInfo.InvariantCulture, $"Domain has {domain.Length / 2} inputs; at most {FunctionEvaluator.MaxArity} are supported."));
                return null;
            }

            return domain;
        }

        if (inferredInputs <= 0)
        {
            Invalid("Domain is missing and the number of inputs cannot be inferred.");
            return null;
        }

        Repaired(string.Create(CultureInfo.InvariantCulture, $"Domain is missing or empty; [0 1] is assumed for each of {inferredInputs} inputs."));
        return UnitPairs(inferredInputs);
    }

    /// <summary>Reads the Range entry (Table 38).</summary>
    /// <param name="dictionary">The function dictionary.</param>
    /// <returns>2 × n numbers, or <see langword="null"/> when absent.</returns>
    public double[]? ReadRange(CosDictionary dictionary)
    {
        double[]? range = ReadPairs(dictionary, FunctionNames.Range);
        return range is { Length: > 0 } ? range : null;
    }

    /// <summary>Fits a range to <paramref name="outputs"/> outputs: extra pairs are dropped and missing ones do not clip.</summary>
    /// <param name="range">The range read, or <see langword="null"/>.</param>
    /// <param name="outputs">n.</param>
    /// <returns>The fitted range, or <see langword="null"/> when there is none.</returns>
    public double[]? FitRange(double[]? range, int outputs)
    {
        if (range is null || range.Length == 2 * outputs)
        {
            return range;
        }

        Repaired(string.Create(CultureInfo.InvariantCulture, $"Range has {range.Length / 2} pairs for {outputs} outputs; it is fitted to the outputs."));
        double[] fitted = new double[2 * outputs];
        for (int j = 0; j < outputs; j++)
        {
            bool present = (2 * j) + 1 < range.Length;
            fitted[2 * j] = present ? range[2 * j] : double.NegativeInfinity;
            fitted[(2 * j) + 1] = present ? range[(2 * j) + 1] : double.PositiveInfinity;
        }

        return fitted;
    }

    /// <summary><paramref name="count"/> pairs [0 1].</summary>
    /// <param name="count">The number of pairs.</param>
    /// <returns>The pairs.</returns>
    public static double[] UnitPairs(int count)
    {
        double[] pairs = new double[2 * count];
        for (int i = 0; i < count; i++)
        {
            pairs[(2 * i) + 1] = 1;
        }

        return pairs;
    }

    /// <summary><paramref name="count"/> pairs [−∞ +∞], which clip nothing.</summary>
    /// <param name="count">The number of pairs.</param>
    /// <returns>The pairs.</returns>
    public static double[] UnboundedPairs(int count)
    {
        double[] pairs = new double[2 * count];
        for (int i = 0; i < count; i++)
        {
            pairs[2 * i] = double.NegativeInfinity;
            pairs[(2 * i) + 1] = double.PositiveInfinity;
        }

        return pairs;
    }

    /// <summary>Reads an array of pairs: an odd last element is dropped and a decreasing pair is noted.</summary>
    private double[]? ReadPairs(CosDictionary dictionary, CosName key)
    {
        double[]? numbers = ReadNumbers(dictionary, key);
        if (numbers is null)
        {
            return null;
        }

        if (numbers.Length % 2 != 0)
        {
            Repaired($"{key.Value} has an odd number of elements; the last is ignored.");
            numbers = numbers[..^1];
        }

        for (int i = 0; i < numbers.Length; i += 2)
        {
            if (numbers[i] > numbers[i + 1])
            {
                Repaired($"{key.Value} has a pair whose first number is greater than its second; values are clipped to the interval between them.");
                break;
            }
        }

        return numbers;
    }
}
