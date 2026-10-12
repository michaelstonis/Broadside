using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Functions;

/// <summary>Turns a function dictionary or stream into a <see cref="FunctionEvaluator"/>, repairing what it can.</summary>
/// <remarks>
/// ISO 32000-2 §7.10. Lenient: every deviation is a diagnostic, and a function that cannot be used compiles to an
/// <see cref="InvalidEvaluator"/>. Strict mode throws from the first diagnostic of severity Warning or Error (ADR 0005).
/// </remarks>
internal static class FunctionCompiler
{
    /// <summary>Reads the FunctionType of a function dictionary or stream.</summary>
    /// <param name="function">A direct object.</param>
    /// <param name="resolve">Resolves an indirect FunctionType.</param>
    /// <returns>The type, or <see langword="null"/> when <paramref name="function"/> is not a function.</returns>
    public static PdfFunctionType? ReadType(CosObject function, Func<CosObject, CosObject> resolve)
    {
        CosDictionary? dictionary = DictionaryOf(function);
        if (dictionary is null || !dictionary.TryGetValue(FunctionNames.FunctionType, out CosObject? value) || resolve(value) is not CosInteger type)
        {
            return null;
        }

        return type.Value switch
        {
            0 => PdfFunctionType.Sampled,
            2 => PdfFunctionType.Exponential,
            3 => PdfFunctionType.Stitching,
            4 => PdfFunctionType.PostScriptCalculator,
            _ => null,
        };
    }

    /// <summary>The dictionary of a function: the dictionary itself, or a stream's dictionary.</summary>
    /// <param name="function">A direct object.</param>
    /// <returns>The dictionary, or <see langword="null"/> for any other object.</returns>
    public static CosDictionary? DictionaryOf(CosObject function) => function switch
    {
        CosStream stream => stream.Dictionary,
        CosDictionary dictionary => dictionary,
        _ => null,
    };

    /// <summary>Compiles a function.</summary>
    /// <param name="function">The function: a dictionary, a stream or a reference to one.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="expectedType">The type the caller's view stands for, or <see langword="null"/> for any.</param>
    /// <param name="expectedInputs">The number of inputs the consumer passes, used only to repair a missing Domain; 0 when unknown.</param>
    /// <param name="expectedOutputs">The number of outputs the consumer reads, used only to repair a missing Range; 0 when unknown.</param>
    /// <returns>The evaluator; an <see cref="InvalidEvaluator"/> when the function cannot be used.</returns>
    public static FunctionEvaluator Compile(
        CosObject function,
        FunctionCompilation compilation,
        PdfFunctionType? expectedType,
        int expectedInputs,
        int expectedOutputs)
    {
        CosReference? outerReference = compilation.Reference;
        if (function is CosReference reference)
        {
            compilation.Reference = reference;
        }

        CosObject resolved = compilation.Resolve(function);
        try
        {
            CosDictionary? dictionary = DictionaryOf(resolved);
            if (dictionary is null)
            {
                compilation.Invalid("The function is not a dictionary or a stream.");
                return Invalid(expectedInputs, expectedOutputs);
            }

            if (resolved is CosStream)
            {
                compilation.Track(dictionary);
            }

            if (compilation.Depth >= FunctionCompilation.MaxDepth)
            {
                compilation.Report(DiagnosticCodes.FunctionTooDeep, DiagnosticSeverity.Error, string.Create(CultureInfo.InvariantCulture, $"Functions nest more than {FunctionCompilation.MaxDepth} deep; the innermost is not used."));
                return Invalid(expectedInputs, expectedOutputs);
            }

            if (!compilation.Enter(resolved))
            {
                compilation.Report(DiagnosticCodes.FunctionCycle, DiagnosticSeverity.Error, "A stitching function contains itself.");
                return Invalid(expectedInputs, expectedOutputs);
            }

            try
            {
                PdfFunctionType? type = ReadType(resolved, compilation.Resolve);
                if (type is null)
                {
                    compilation.Invalid("FunctionType is missing or is not 0, 2, 3 or 4.");
                    return Invalid(expectedInputs, expectedOutputs);
                }

                if (expectedType is { } expected && expected != type)
                {
                    compilation.Invalid("FunctionType changed since the function was first read; read it again with PdfDocument.GetFunction.");
                    return Invalid(expectedInputs, expectedOutputs);
                }

                if (type is PdfFunctionType.Sampled or PdfFunctionType.PostScriptCalculator && resolved is not CosStream)
                {
                    compilation.Invalid("A Type 0 or Type 4 function shall be a stream.");
                    return Invalid(expectedInputs, expectedOutputs);
                }

                return type switch
                {
                    PdfFunctionType.Sampled => SampledCompiler.Compile((CosStream)resolved, compilation, expectedOutputs),
                    PdfFunctionType.Exponential => CompileExponential(dictionary, compilation),
                    PdfFunctionType.Stitching => CompileStitching(dictionary, compilation, expectedOutputs),
                    _ => PostScriptCompiler.Compile((CosStream)resolved, compilation, expectedInputs, expectedOutputs),
                };
            }
            finally
            {
                compilation.Leave(resolved);
            }
        }
        finally
        {
            compilation.Reference = outerReference;
        }
    }

    /// <summary>An invalid function with the arity the consumer expects.</summary>
    /// <param name="inputs">m, or 0.</param>
    /// <param name="outputs">n, or 0.</param>
    /// <param name="range">The range, when known.</param>
    /// <returns>The evaluator.</returns>
    public static FunctionEvaluator Invalid(int inputs, int outputs, double[]? range = null) =>
        new InvalidEvaluator(FunctionCompilation.UnitPairs(Math.Clamp(inputs, 0, FunctionEvaluator.MaxArity)), range, Math.Clamp(outputs, 0, FunctionEvaluator.MaxArity));

    /// <summary>Compiles a Type 2 function (§7.10.3, Table 40).</summary>
    private static FunctionEvaluator CompileExponential(CosDictionary dictionary, FunctionCompilation compilation)
    {
        double[]? domain = OneInput(compilation.ReadDomain(dictionary, inferredInputs: 1), compilation, "Type 2");
        double[] c0 = compilation.ReadNumbers(dictionary, FunctionNames.C0) ?? [0.0];
        double[] c1 = compilation.ReadNumbers(dictionary, FunctionNames.C1) ?? [1.0];
        int outputs = Math.Min(c0.Length, c1.Length);
        if (c0.Length != c1.Length)
        {
            compilation.Repaired(string.Create(CultureInfo.InvariantCulture, $"C0 has {c0.Length} numbers and C1 {c1.Length}; the first {outputs} of each are used."));
        }

        double[]? range = compilation.ReadRange(dictionary);
        if (domain is null || outputs == 0 || outputs > FunctionEvaluator.MaxArity)
        {
            if (domain is not null)
            {
                compilation.Invalid("C0 and C1 give no usable number of outputs.");
            }

            return Invalid(1, outputs, range);
        }

        if (compilation.ReadNumber(dictionary, FunctionNames.N) is not { } exponent || !double.IsFinite(exponent))
        {
            compilation.Invalid("N, the interpolation exponent, is missing or is not a number.");
            return Invalid(1, outputs, range);
        }

        double low = Math.Min(domain[0], domain[1]);
        double high = Math.Max(domain[0], domain[1]);
        if (exponent != Math.Floor(exponent) && low < 0)
        {
            compilation.Repaired("N is not an integer but Domain allows negative inputs; such inputs give C0.");
        }
        else if (exponent < 0 && low <= 0 && high >= 0)
        {
            compilation.Repaired("N is negative but Domain allows the input 0; that input gives C0.");
        }

        return new ExponentialEvaluator(domain, compilation.FitRange(range, outputs), c0[..outputs], c1[..outputs], exponent);
    }

    /// <summary>Compiles a Type 3 function (§7.10.4, Table 41).</summary>
    private static FunctionEvaluator CompileStitching(CosDictionary dictionary, FunctionCompilation compilation, int expectedOutputs)
    {
        double[]? domain = OneInput(compilation.ReadDomain(dictionary, inferredInputs: 1), compilation, "Type 3");
        double[]? range = compilation.ReadRange(dictionary);
        int fallbackOutputs = range is null ? expectedOutputs : range.Length / 2;
        if (domain is null)
        {
            return Invalid(1, fallbackOutputs, range);
        }

        if (!dictionary.TryGetValue(FunctionNames.Functions, out CosObject? value) || compilation.Resolve(value) is not CosArray { Count: > 0 } array)
        {
            compilation.Invalid("Functions is missing, empty or not an array.");
            return Invalid(1, fallbackOutputs, range);
        }

        int k = array.Count;
        var functions = new FunctionEvaluator[k];
        for (int i = 0; i < k; i++)
        {
            FunctionEvaluator function = Compile(array[i], compilation, expectedType: null, expectedInputs: 1, expectedOutputs: fallbackOutputs);
            if (!function.IsValid || function.InputCount != 1 || (i > 0 && function.OutputCount != functions[0].OutputCount))
            {
                compilation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Function {i} of Functions is invalid, does not take one input, or has a different number of outputs from the others."));
                return Invalid(1, fallbackOutputs, range);
            }

            functions[i] = function;
        }

        int outputs = functions[0].OutputCount;
        double[]? bounds = compilation.ReadNumbers(dictionary, FunctionNames.Bounds);
        if (bounds is null && k == 1)
        {
            compilation.Repaired("Bounds is missing; with one function it is empty.");
            bounds = [];
        }

        if (bounds is null || bounds.Length < k - 1)
        {
            compilation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Bounds is missing or has fewer than the {k - 1} numbers {k} functions need."));
            return Invalid(1, outputs, range);
        }

        if (bounds.Length > k - 1)
        {
            compilation.Repaired(string.Create(CultureInfo.InvariantCulture, $"Bounds has {bounds.Length} numbers; the first {k - 1} are used."));
            bounds = bounds[..(k - 1)];
        }

        double low = Math.Min(domain[0], domain[1]);
        double high = Math.Max(domain[0], domain[1]);
        for (int i = 0; i < bounds.Length; i++)
        {
            if (bounds[i] < low || bounds[i] > high || (i > 0 && bounds[i] <= bounds[i - 1]))
            {
                compilation.Repaired("Bounds is not increasing or lies outside Domain; it is used as given.");
                break;
            }
        }

        double[] encode = compilation.ReadNumbers(dictionary, FunctionNames.Encode) ?? [];
        if (encode.Length != 2 * k)
        {
            compilation.Repaired(string.Create(CultureInfo.InvariantCulture, $"Encode has {encode.Length} numbers for {k} functions; missing pairs are [0 1] and extra numbers are ignored."));
            double[] fitted = FunctionCompilation.UnitPairs(k);
            encode.AsSpan(0, Math.Min(encode.Length, fitted.Length)).CopyTo(fitted);
            encode = fitted;
        }

        return new StitchingEvaluator(domain, compilation.FitRange(range, outputs), functions, bounds, encode);
    }

    /// <summary>Keeps the first pair of a 1-input function's Domain.</summary>
    private static double[]? OneInput(double[]? domain, FunctionCompilation compilation, string type)
    {
        if (domain is null || domain.Length == 2)
        {
            return domain;
        }

        compilation.Repaired($"A {type} function takes one input; Domain pairs after the first are ignored.");
        return domain[..2];
    }
}
