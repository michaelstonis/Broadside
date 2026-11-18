using Broadside.Content;
using Broadside.Rendering;

namespace Broadside.Text;

/// <summary>
/// A text extraction that runs in time slices: <see cref="Continue"/> interprets the page's content until its budget is used, and
/// the next call resumes at the operator where it stopped. Start one with
/// <see cref="PdfPage.BeginTextExtraction(TextExtractionOptions)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Issue #546, viewer capability 4: a single-threaded viewer (Blazor WebAssembly) extracts a page's selectable text between frames
/// instead of in one long step. The result is the page <see cref="PdfPage.ExtractText(TextExtractionOptions)"/> returns.
/// </para>
/// <para>
/// The content is run in slices of the page's top-level operators; a top-level operator is never split, with the form XObject,
/// pattern or glyph content it runs. Putting the glyphs in structure order, on a tagged page, is the last slice. The budget is
/// checked every few operators, so a slice ends a few operators after its budget at most.
/// </para>
/// <para>
/// Not thread-safe: one thread at a time continues an extraction. The document must not be edited while an extraction is in
/// progress; start a new one after an edit. Dispose an unfinished extraction to abandon it. ISO 32000-2 §7.8.2, §9.4.4, §9.10,
/// §14.8.2 and §14.9.
/// </para>
/// </remarks>
public sealed class TextExtraction : IDisposable
{
    private readonly PdfPage _page;
    private readonly PdfDocument _document;
    private readonly TextExtractionOptions _options;
    private readonly ContentProcessor? _companion;
    private TextExtractor? _extractor;
    private ContentInterpreter? _interpreter;
    private TextPage? _result;
    private bool _ended;

    /// <summary>An extraction of <paramref name="page"/>'s text, with <paramref name="companion"/>, when given, run in the same pass.</summary>
    internal TextExtraction(PdfPage page, PdfDocument document, TextExtractionOptions options, ContentProcessor? companion = null)
    {
        _page = page;
        _document = document;
        _options = options;
        _companion = companion;
    }

    /// <summary>Gets a value indicating whether the text is extracted.</summary>
    public bool IsCompleted => _result is not null;

    /// <summary>Gets the page's text once <see cref="IsCompleted"/> is <see langword="true"/>.</summary>
    /// <exception cref="InvalidOperationException">The extraction is not complete.</exception>
    public TextPage Result => _result ?? throw new InvalidOperationException("The text is not extracted yet; continue the extraction until it completes.");

    /// <summary>
    /// Extracts from where the last slice stopped until the text is complete or, after at least one step, <paramref name="budget"/>
    /// has passed.
    /// </summary>
    /// <param name="budget">
    /// How long the slice may run; <see cref="TimeSpan.Zero"/> runs a few operators, <see cref="Timeout.InfiniteTimeSpan"/> extracts the
    /// rest.
    /// </param>
    /// <returns><see langword="true"/> when the text is complete (also when it already was); <see langword="false"/> when the slice stopped for the budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="OperationCanceledException">The content options' token was cancelled: the extraction has ended.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found: the extraction has ended.</exception>
    /// <exception cref="ObjectDisposedException">The extraction was disposed, or ended by an exception, before completing.</exception>
    /// <remarks>ISO 32000-2 §7.8.2, §9.10 and §14.8.2.5.</remarks>
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
        return ContinueUntil(DisplayImage.Deadline(budget));
    }

    /// <summary>As <see cref="Continue"/>, until a <see cref="System.Diagnostics.Stopwatch"/> timestamp.</summary>
    internal bool ContinueUntil(long deadline)
    {
        if (_result is not null)
        {
            return true;
        }

        ObjectDisposedException.ThrowIf(_ended, this);
        try
        {
            ContentOptions options = _options.ContentOptions ?? ContentInterpreter.DefaultOptions;
            options.CancellationToken.ThrowIfCancellationRequested(); // at every slice; the run checks every 1,024 operators
            if (_extractor is null)
            {
                _extractor = TextExtractor.Rent(_options);
                ContentProcessor processor = _companion is null ? _extractor : new CompositeContentProcessor(_extractor, _companion);
                _interpreter = ContentInterpreter.BeginPageRun(_page, _document, processor, options);
            }

            if (_interpreter is { } interpreter)
            {
                if (!interpreter.ContinuePageRun(deadline))
                {
                    return false;
                }

                interpreter.EndPageRun();
                _interpreter = null;
                if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                {
                    return false; // the reading order is the next slice's
                }
            }

            // Structure order reads the extractor's glyphs and content items, so it runs before the extractor is returned (issue #185).
            TextPage page = _page.ToTextPage(_extractor, _options);
            TextExtractor.Return(_extractor);
            _extractor = null;
            _result = page;
            _ended = true;
            return true;
        }
        catch
        {
            Abandon();
            throw;
        }
    }

    /// <summary>Abandons an unfinished extraction; a finished one keeps its text.</summary>
    public void Dispose() => Abandon();

    private void Abandon()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _interpreter?.EndPageRun();
        _interpreter = null;
        if (_extractor is { } extractor)
        {
            TextExtractor.Return(extractor);
            _extractor = null;
        }
    }
}
