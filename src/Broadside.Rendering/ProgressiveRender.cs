namespace Broadside.Rendering;

/// <summary>
/// A render of the <see cref="SoftwareRasterizer"/> that draws in time slices: <see cref="Continue"/> draws until its budget is used,
/// leaving a valid partial image in <see cref="Target"/>, and the next call resumes where it stopped. Start one with
/// <see cref="SoftwareRasterizer.BeginRender(PdfPage, Bitmap, RenderOptions)"/> or
/// <see cref="SoftwareRasterizer.BeginRender(DisplayList, Bitmap, in Graphics.Matrix, CancellationToken, IProgress{double})"/>.
/// </summary>
/// <remarks>
/// <para>
/// Viewer capability 2 (issue #97), like PDFium's progressive rendering (<c>FPDF_RenderPageBitmap_Start</c>,
/// <c>FPDF_RenderPage_Continue</c>, <c>FPDF_RenderPage_Close</c>). The finished bitmap is the same, pixel for pixel, however the
/// render is sliced: between slices the rasterizer keeps its graphics state stack, transform and clip, and every operation of
/// the display list is drawn whole, in order (<see cref="DisplayListReplay"/>). After any slice the bitmap holds the operations
/// drawn so far, composited as they will be in the end.
/// </para>
/// <para>
/// Nothing blocks or waits: a single-threaded host (Blazor WebAssembly) draws a slice, presents or yields to its event loop, and
/// continues, for example <c>while (!render.Continue(TimeSpan.FromMilliseconds(8))) await Task.Yield();</c>. The rasterizer renders
/// nothing else until the render completes, is cancelled, fails or is disposed. Dispose it to abandon it: the bitmap keeps the
/// partial image and the rasterizer is free again, with nothing left behind that changes its next render.
/// </para>
/// </remarks>
public sealed class ProgressiveRender : IDisposable
{
    private readonly SoftwareRasterizer _rasterizer;
    private readonly DisplayListReplay _replay;
    private bool _ended;

    internal ProgressiveRender(SoftwareRasterizer rasterizer, DisplayListReplay replay, Bitmap target)
    {
        _rasterizer = rasterizer;
        _replay = replay;
        Target = target;
    }

    /// <summary>Gets the bitmap being drawn into.</summary>
    public Bitmap Target { get; }

    /// <summary>Gets the display list being drawn.</summary>
    public DisplayList List => _replay.List;

    /// <summary>Gets a value indicating whether every operation of the list has been drawn.</summary>
    public bool IsCompleted => _replay.IsCompleted;

    /// <summary>Gets the share of the list's operations drawn so far, from 0 to 1.</summary>
    public double Progress => _replay.Progress;

    /// <summary>Draws from where the last slice stopped until the list is drawn or, after at least one operation, <paramref name="budget"/> has passed.</summary>
    /// <param name="budget">
    /// How long the slice may run; <see cref="TimeSpan.Zero"/> draws exactly one operation, <see cref="Timeout.InfiniteTimeSpan"/>
    /// draws the rest. One operation is never split, so a slice can overrun its budget by one operation's time.
    /// </param>
    /// <returns><see langword="true"/> when the render is complete (also when it already was), <see langword="false"/> when it stopped for the budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="OperationCanceledException">The render's token was cancelled: the render has ended, the bitmap keeps what was drawn.</exception>
    /// <exception cref="ObjectDisposedException">The render was disposed, or ended by an exception, before completing.</exception>
    public bool Continue(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The budget must not be negative, except for Timeout.InfiniteTimeSpan.");
        }

        if (_replay.IsCompleted && _ended)
        {
            return true;
        }

        ObjectDisposedException.ThrowIf(_ended || !_rasterizer.IsDrawing(_replay), this);
        bool completed;
        try
        {
            completed = _replay.Continue(budget);
        }
        catch
        {
            End();
            throw;
        }

        if (completed)
        {
            End();
        }

        return completed;
    }

    /// <summary>Ends the render if it has not completed: the bitmap keeps what was drawn, and the rasterizer may render again.</summary>
    public void Dispose() => End();

    private void End()
    {
        if (!_ended)
        {
            _ended = true;
            _rasterizer.EndProgressive(_replay);
        }
    }
}
