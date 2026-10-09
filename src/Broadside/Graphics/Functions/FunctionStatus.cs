namespace Broadside.Graphics.Functions;

/// <summary>How one evaluation of a function went. Evaluation never throws and never reports; the caller reports from this.</summary>
/// <remarks>
/// ISO 32000-2 §7.10. A consumer that evaluates many samples reports at most one diagnostic per function and page
/// (<c>DiagnosticCodes.FunctionEvaluationRepaired</c> through <c>DiagnosticSink.ReportOnce</c>) when the status is not
/// <see cref="Ok"/>.
/// </remarks>
internal enum FunctionStatus
{
    /// <summary>Evaluated as the specification defines.</summary>
    Ok = 0,

    /// <summary>
    /// A deviation was repaired on the way: a Type 4 division by zero, an operand of the wrong type, the wrong number or kind of
    /// results, an undefined result (square root of a negative number), or a non-finite intermediate value.
    /// </summary>
    Repaired = 1,

    /// <summary>
    /// The function could not be evaluated (it is invalid, or a Type 4 program overflowed or underflowed its stack); every output is
    /// 0 clipped to the range.
    /// </summary>
    Failed = 2,
}
