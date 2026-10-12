namespace Broadside.Graphics.Functions;

/// <summary>Which forms other than a single function object a consumer's entry accepts.</summary>
/// <remarks>
/// ISO 32000-2 §8.4.5 Table 57 (<c>TR</c>, <c>TR2</c>: a function, an array of four functions or the name <c>Identity</c>),
/// §11.6.5.1 Table 142 (soft mask <c>TR</c>: a function or <c>Identity</c>), §8.7.4.5.2-8.7.4.5.4 Tables 78-80 (shading <c>Function</c>: a function
/// or an array of n 1-output functions). Tint transforms (§8.6.6.4, §8.6.6.5) accept a single function only.
/// </remarks>
[Flags]
internal enum FunctionForms
{
    /// <summary>A single function dictionary or stream.</summary>
    Single = 0,

    /// <summary>The name <c>Identity</c> is accepted: an m-in m-out function that returns its inputs.</summary>
    Identity = 1,

    /// <summary>
    /// An array of n functions of m inputs and one output each is accepted, combined into one m-in n-out function. A per-component
    /// array (a <c>TR</c> array of four) is not this form; its consumer evaluates the elements separately.
    /// </summary>
    Array = 2,
}
