using System.Globalization;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Functions;

/// <summary>
/// A document's functions: one <see cref="PdfFunction"/> view per function object (and expected arity), compiled once and shared by
/// every consumer and thread.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10. Keyed by the indirect reference when the function is reached through one, else by the direct object instance,
/// plus its FunctionType (a view stands for one type) and the arity hints (they only matter for repairs). Built through
/// <see cref="OnceCache{TKey, TValue}"/>, so concurrent first uses compile once.
/// </para>
/// <para>
/// Consumers (tint transforms, transfer functions, shadings) call <see cref="GetEvaluator"/> once per content run and evaluate the
/// result per sample; the evaluator reports nothing, so the consumer reports one
/// <see cref="DiagnosticCodes.FunctionEvaluationRepaired"/> per function and page when an evaluation's status is not
/// <see cref="FunctionStatus.Ok"/>.
/// </para>
/// </remarks>
internal sealed class FunctionCache
{
    private readonly PdfDocument _document;
    private readonly DiagnosticSink _diagnostics;
    private readonly OnceCache<Key, PdfFunction?> _functions = new();
    private readonly Func<CosObject, CosObject> _resolve;

    /// <summary>Initializes a new instance of the <see cref="FunctionCache"/> class.</summary>
    /// <param name="document">The document.</param>
    /// <param name="diagnostics">The document's diagnostics.</param>
    public FunctionCache(PdfDocument document, DiagnosticSink diagnostics)
    {
        _document = document;
        _diagnostics = diagnostics;
        _resolve = document.Resolve;
    }

    /// <summary>Returns the view of a function object.</summary>
    /// <param name="value">A function dictionary or stream, or a reference to one.</param>
    /// <param name="expectedInputs">The number of inputs the consumer passes (repairs a missing Domain); 0 when unknown.</param>
    /// <param name="expectedOutputs">The number of outputs the consumer reads (repairs a missing Range); 0 when unknown.</param>
    /// <returns>The view; <see langword="null"/>, with a diagnostic, when <paramref name="value"/> is not a function of a known type.</returns>
    public PdfFunction? Get(CosObject value, int expectedInputs = 0, int expectedOutputs = 0)
    {
        CosObject resolved = _document.Resolve(value);
        var reference = value as CosReference;
        if (FunctionCompiler.ReadType(resolved, _resolve) is not { } type)
        {
            string what = FunctionCompiler.DictionaryOf(resolved) is null ? "is not a dictionary or a stream" : "has no FunctionType 0, 2, 3 or 4";
            _diagnostics.Report(DiagnosticCodes.FunctionInvalid, DiagnosticSeverity.Error, $"The function {what}.", offset: null, reference);
            return null;
        }

        var key = new Key((object?)reference ?? resolved, type, expectedInputs, expectedOutputs);
        return _functions.GetOrCreate(
            key,
            (Cache: this, Resolved: resolved, Reference: reference),
            static (key, state) => new Created<PdfFunction?>(state.Cache.Create(state.Resolved, state.Reference, key)),
            static (_, _) => null);
    }

    /// <summary>Returns an evaluator for an entry that may also hold <c>Identity</c> or an array of functions.</summary>
    /// <param name="value">The entry's value.</param>
    /// <param name="forms">The forms the entry accepts besides a single function.</param>
    /// <param name="expectedInputs">m, when the consumer knows it; 0 otherwise.</param>
    /// <param name="expectedOutputs">n, when the consumer knows it; 0 otherwise.</param>
    /// <returns>
    /// The evaluator (possibly invalid: check <see cref="FunctionEvaluator.IsValid"/>), or <see langword="null"/> with a diagnostic
    /// when the value is not a function in an accepted form.
    /// </returns>
    public FunctionEvaluator? GetEvaluator(CosObject value, FunctionForms forms, int expectedInputs, int expectedOutputs)
    {
        CosObject resolved = _document.Resolve(value);
        var reference = value as CosReference;
        if (resolved is CosName name && name.Equals(FunctionNames.Identity))
        {
            if ((forms & FunctionForms.Identity) != 0)
            {
                return new IdentityEvaluator(Math.Clamp(expectedInputs > 0 ? expectedInputs : expectedOutputs, 1, FunctionEvaluator.MaxArity));
            }

            _diagnostics.Report(DiagnosticCodes.FunctionInvalid, DiagnosticSeverity.Error, "Identity is not allowed here; a function is required.", offset: null, reference);
            return null;
        }

        if (resolved is CosArray array)
        {
            return (forms & FunctionForms.Array) != 0
                ? CombineArray(array, reference, expectedInputs, expectedOutputs)
                : ReportNotAllowed(reference, "An array of functions is not allowed here; a single function is required.");
        }

        return Get(value, expectedInputs, expectedOutputs)?.Evaluator;
    }

    /// <summary>Compiles a function for a view.</summary>
    /// <param name="function">The function's dictionary or stream (direct).</param>
    /// <param name="reference">Its reference, when it has one.</param>
    /// <param name="type">The view's type.</param>
    /// <param name="expectedInputs">The arity hint for inputs.</param>
    /// <param name="expectedOutputs">The arity hint for outputs.</param>
    /// <returns>The compiled function.</returns>
    internal CompiledFunction Compile(CosObject function, CosReference? reference, PdfFunctionType type, int expectedInputs, int expectedOutputs)
    {
        var compilation = new FunctionCompilation(_document, _diagnostics) { Reference = reference };
        FunctionEvaluator evaluator = FunctionCompiler.Compile(function, compilation, type, expectedInputs, expectedOutputs);
        return new CompiledFunction(evaluator, compilation.Dependencies());
    }

    /// <summary>Resolves an object through the document.</summary>
    /// <param name="value">The object.</param>
    /// <returns>The direct object.</returns>
    internal CosObject Resolve(CosObject? value) => _document.Resolve(value);

    /// <summary>Records the one runtime diagnostic of a compiled function.</summary>
    /// <param name="function">The view.</param>
    /// <param name="status">The status of the evaluation that went wrong.</param>
    internal void ReportEvaluation(PdfFunction function, FunctionStatus status)
    {
        string message = status == FunctionStatus.Failed
            ? "The function could not be evaluated; its outputs are 0, clipped to its range."
            : "The function's evaluation met an error (an undefined result, an operand of the wrong type or the wrong number of results); it was repaired.";
        _diagnostics.Report(
            DiagnosticCodes.FunctionEvaluationRepaired,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"Type {(int)function.FunctionType}: {message}"),
            offset: null,
            function.Reference);
    }

    private PdfFunction Create(CosObject function, CosReference? reference, Key key)
    {
        CompiledFunction compiled = Compile(function, reference, key.Type, key.Inputs, key.Outputs);
        return key.Type switch
        {
            PdfFunctionType.Sampled => new PdfSampledFunction(this, function, reference, compiled, key.Inputs, key.Outputs),
            PdfFunctionType.Exponential => new PdfExponentialFunction(this, function, reference, compiled, key.Inputs, key.Outputs),
            PdfFunctionType.Stitching => new PdfStitchingFunction(this, function, reference, compiled, key.Inputs, key.Outputs),
            _ => new PdfPostScriptFunction(this, function, reference, compiled, key.Inputs, key.Outputs),
        };
    }

    private ArrayEvaluator? CombineArray(CosArray array, CosReference? reference, int expectedInputs, int expectedOutputs)
    {
        if (array.Count == 0 || array.Count > FunctionEvaluator.MaxArity || (expectedOutputs > 0 && array.Count != expectedOutputs))
        {
            return ReportNotAllowed(reference, "The array of functions is empty, too long, or does not have one function per output.");
        }

        var functions = new FunctionEvaluator[array.Count];
        for (int j = 0; j < functions.Length; j++)
        {
            FunctionEvaluator? function = Get(array[j], expectedInputs, expectedOutputs: 1)?.Evaluator;
            if (function is not { IsValid: true, OutputCount: 1 } || (j > 0 && function.InputCount != functions[0].InputCount))
            {
                return ReportNotAllowed(reference, "An element of the array of functions is invalid, does not have one output, or has a different number of inputs.");
            }

            functions[j] = function;
        }

        return new ArrayEvaluator(FunctionCompilation.UnboundedPairs(functions[0].InputCount), functions);
    }

    private ArrayEvaluator? ReportNotAllowed(CosReference? reference, string message)
    {
        _diagnostics.Report(DiagnosticCodes.FunctionInvalid, DiagnosticSeverity.Error, message, offset: null, reference);
        return null;
    }

    /// <summary>The cache key: the function's identity, its type and the arity hints.</summary>
    private readonly record struct Key(object Identity, PdfFunctionType Type, int Inputs, int Outputs);
}
