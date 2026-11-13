using System.Runtime.CompilerServices;

namespace Broadside.Rendering;

/// <summary>
/// The columns of the footprints of the spans of a reduced image (<see cref="ImagePainter"/>) and their weights, kept for the spans
/// that follow: where u does not change down the image, every row whose span starts at the same pixel and the same u takes the same
/// columns pixel for pixel, so they are worked out once per image rather than once per pixel.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.3 and §10.7.4 (see <see cref="ImagePainter"/>). A row of an upright image usually arrives as a few spans
/// that are the same on every row (an edge pixel of partial coverage, then the rest), so a few spans are kept: up to
/// <see cref="MaxSpans"/>, after which they are dropped and kept again from the next.
/// </para>
/// <para>
/// A span is found only by its first pixel and its u, the same double, so what is read back is exactly what the painter would work
/// out again. Owned by one rasterizer and used by one painter at a time; the arrays are pooled, grow to what the image's spans need
/// and go back to the pool on <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class ImageFootprints : IDisposable
{
    /// <summary>The most spans kept at once.</summary>
    public const int MaxSpans = 4;

    private KeptSpans _kept;
    private int _spans;

    // _starts[p] is where pixel p's columns start in _columns and _weights, and _starts[p + 1] where they end; each span's pixels are
    // consecutive, from _spanFirst. _pixels pixels are written.
    private int[] _starts = [];
    private int[] _columns = [];
    private double[] _weights = [];
    private int _pixels;

    /// <summary>Forgets the kept spans: the next painter has another grid.</summary>
    public void Clear()
    {
        _spans = 0;
        _pixels = 0;
    }

    /// <summary>
    /// Finds the kept span that starts at pixel <paramref name="x"/> and at <paramref name="u"/>, with at least
    /// <paramref name="count"/> pixels: the index of its first pixel for <see cref="Columns"/> and <see cref="Weights"/>, or -1.
    /// </summary>
    public int Find(int x, double u, int count)
    {
        foreach (KeptSpan span in ((ReadOnlySpan<KeptSpan>)_kept)[.._spans])
        {
            if (span.X == x && span.U == u && span.Count >= count)
            {
                return span.First;
            }
        }

        return -1;
    }

    /// <summary>
    /// Starts keeping a span of <paramref name="count"/> pixels from pixel <paramref name="x"/> at <paramref name="u"/>, dropping the
    /// kept ones when there is no room; <see cref="Add"/> follows for each pixel. Returns the index of its first pixel.
    /// </summary>
    public int Start(int x, double u, int count)
    {
        if (_spans == MaxSpans || _pixels + count + 1 > _starts.Length || (long)(_pixels + count) * BoxFilter.MaxTaps > _columns.Length)
        {
            // Room for twice what is kept with this span, so that the spans of a row soon all fit.
            int pixels = (_spans == MaxSpans ? 0 : _pixels) + count;
            Pooled.Ensure(ref _starts, (2 * pixels) + 1);
            Pooled.Ensure(ref _columns, 2 * pixels * BoxFilter.MaxTaps);
            Pooled.Ensure(ref _weights, 2 * pixels * BoxFilter.MaxTaps);
            Clear();
        }

        int first = _pixels;
        if (first == 0)
        {
            _starts[0] = 0;
        }

        _kept[_spans++] = new KeptSpan(x, u, count, first);
        return first;
    }

    /// <summary>Gets where the next pixel's columns go, <see cref="BoxFilter.MaxTaps"/> long.</summary>
    public Span<int> NextColumns => _columns.AsSpan(_starts[_pixels], BoxFilter.MaxTaps);

    /// <summary>Gets where the next pixel's column weights go, <see cref="BoxFilter.MaxTaps"/> long.</summary>
    public Span<double> NextWeights => _weights.AsSpan(_starts[_pixels], BoxFilter.MaxTaps);

    /// <summary>Ends the next pixel's columns, <paramref name="taps"/> of them written to <see cref="NextColumns"/> and <see cref="NextWeights"/>.</summary>
    public void Add(int taps)
    {
        _starts[_pixels + 1] = _starts[_pixels] + taps;
        _pixels++;
    }

    /// <summary>Gets where each kept pixel's columns start in <see cref="AllColumns"/> and <see cref="AllWeights"/>, and the next one's end.</summary>
    public ReadOnlySpan<int> Starts => _starts.AsSpan(0, _pixels + 1);

    /// <summary>Gets the kept pixels' columns, each pixel's from its start in <see cref="Starts"/>.</summary>
    public ReadOnlySpan<int> AllColumns => _columns;

    /// <summary>Gets the kept pixels' column weights, each pixel's from its start in <see cref="Starts"/>.</summary>
    public ReadOnlySpan<double> AllWeights => _weights;

    /// <summary>The columns of pixel <paramref name="p"/> of the kept spans.</summary>
    public ReadOnlySpan<int> Columns(int p) => _columns.AsSpan(_starts[p], _starts[p + 1] - _starts[p]);

    /// <summary>The column weights of pixel <paramref name="p"/> of the kept spans.</summary>
    public ReadOnlySpan<double> Weights(int p) => _weights.AsSpan(_starts[p], _starts[p + 1] - _starts[p]);

    /// <summary>A kept span: its first pixel, the u of that pixel's centre, its pixels and the index of its first pixel.</summary>
    private readonly record struct KeptSpan(int X, double U, int Count, int First);

    /// <summary>The kept spans, inline.</summary>
    [InlineArray(MaxSpans)]
    private struct KeptSpans
    {
        private KeptSpan _first;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Pooled.Return(ref _starts);
        Pooled.Return(ref _columns);
        Pooled.Return(ref _weights);
        Clear();
    }
}
