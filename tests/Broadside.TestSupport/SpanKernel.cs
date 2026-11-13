namespace Broadside.TestSupport;

/// <summary>A row kernel: reads <paramref name="source"/> and writes <paramref name="destination"/>.</summary>
/// <typeparam name="TIn">The input element.</typeparam>
/// <typeparam name="TOut">The output element.</typeparam>
/// <param name="source">The input row.</param>
/// <param name="destination">The output row, sized exactly for the input.</param>
public delegate void SpanKernel<TIn, TOut>(ReadOnlySpan<TIn> source, Span<TOut> destination);
