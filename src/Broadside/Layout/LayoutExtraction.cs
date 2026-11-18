using Broadside.Text;

namespace Broadside.Layout;

/// <summary>
/// A page's layout-tree build that runs in time slices: <see cref="Continue"/> extracts the page's text until its budget is used, and
/// the next call resumes where it stopped. Start one with <see cref="PdfPage.BeginLayoutExtraction(LayoutOptions)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Issue #546, viewer capability 4: a single-threaded viewer (Blazor WebAssembly) builds a page's reading order and accessible tree
/// between frames instead of in one long step. The result is the tree <see cref="PdfPage.ExtractLayout(LayoutOptions)"/> returns.
/// </para>
/// <para>
/// The page's text is extracted in slices of its top-level operators, as by <see cref="TextExtraction"/>, with the geometry the
/// layout needs collected in the same pass. Building the tree from it (the structure tree's nodes, or the columns, paragraphs and
/// tables detected on an untagged page) is the last slice.
/// </para>
/// <para>
/// Not thread-safe: one thread at a time continues a build. The document must not be edited while a build is in progress; start a
/// new one after an edit. Dispose an unfinished build to abandon it. ISO 32000-2 §14.8.4 and ISO/TS 32005.
/// </para>
/// </remarks>
public sealed class LayoutExtraction : IDisposable
{
    private readonly PdfPage _page;
    private readonly PdfDocument _document;
    private StructureLayout.PageBuild _build;
    private TextExtraction? _text;
    private LayoutTree? _result;
    private bool _ended;

    internal LayoutExtraction(PdfPage page, PdfDocument document, LayoutOptions options)
    {
        _page = page;
        _document = document;
        _build = new StructureLayout.PageBuild(document, 1, options);
    }

    /// <summary>Gets a value indicating whether the tree is built.</summary>
    public bool IsCompleted => _result is not null;

    /// <summary>Gets the page's layout tree once <see cref="IsCompleted"/> is <see langword="true"/>.</summary>
    /// <exception cref="InvalidOperationException">The build is not complete.</exception>
    public LayoutTree Result => _result ?? throw new InvalidOperationException("The layout is not built yet; continue the build until it completes.");

    /// <summary>
    /// Builds from where the last slice stopped until the tree is complete or, after at least one step, <paramref name="budget"/> has
    /// passed.
    /// </summary>
    /// <param name="budget">
    /// How long the slice may run; <see cref="TimeSpan.Zero"/> runs a few operators, <see cref="Timeout.InfiniteTimeSpan"/> builds the
    /// rest.
    /// </param>
    /// <returns><see langword="true"/> when the tree is complete (also when it already was); <see langword="false"/> when the slice stopped for the budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="OperationCanceledException">The content options' token was cancelled: the build has ended.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found: the build has ended.</exception>
    /// <exception cref="ObjectDisposedException">The build was disposed, or ended by an exception, before completing.</exception>
    /// <remarks>ISO 32000-2 §14.8.4; ISO/TS 32005.</remarks>
    public bool Continue(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The budget must not be negative, except for Timeout.InfiniteTimeSpan.");
        }

        if (_result is not null)
        {
            return true;
        }

        ObjectDisposedException.ThrowIf(_ended, this);
        long deadline = Rendering.DisplayImage.Deadline(budget);
        try
        {
            if (_text is null)
            {
                _text = new TextExtraction(_page, _document, _build.TextOptions, _build.Companion);
            }
            else if (_text.IsCompleted)
            {
                return Finish();
            }

            if (!_text.ContinueUntil(deadline))
            {
                return false;
            }

            // The tree is the next slice's when the text used up this one.
            return System.Diagnostics.Stopwatch.GetTimestamp() < deadline && Finish();
        }
        catch
        {
            Abandon();
            throw;
        }
    }

    /// <summary>Builds the tree from the page's extracted text.</summary>
    private bool Finish()
    {
        _build.AddPage(0, _page, _text!.Result);
        _result = _build.Finish();
        _ended = true;
        _build.Release();
        return true;
    }

    /// <summary>Abandons an unfinished build; a finished one keeps its tree.</summary>
    public void Dispose() => Abandon();

    private void Abandon()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _text?.Dispose();
        _text = null;
        _build.Release();
    }
}
