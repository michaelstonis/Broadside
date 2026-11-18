using Broadside.Content;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// A display-list build that runs in time slices: <see cref="Continue"/> interprets the page's content until its budget is used, and
/// the next call resumes at the operator where it stopped. Start one with
/// <see cref="PdfPage.BeginDisplayList(DisplayLayers, DisplayIntent, ContentOptions)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Issue #365, viewer capability 2: a single-threaded viewer (Blazor WebAssembly) builds a heavy page between frames instead of in one
/// long step. The list is the one <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, ContentOptions)"/> builds, and it
/// is offered to the document's display-list cache the same way; a list already in the cache completes the build at once.
/// </para>
/// <para>
/// The content layer is built in slices of the page's top-level operators; a top-level operator is never split, with the form
/// XObject, pattern or glyph content it runs. The annotation and form-field layers are built in the last slice. A build with an
/// optional-content state is not kept in the cache, as with <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, ContentOptions)"/>.
/// The budget is checked every few operators, so a slice ends a few operators after its budget at most.
/// </para>
/// <para>
/// Not thread-safe: one thread at a time continues a build. The document must not be edited while a build is in progress; an
/// edit between two slices leaves a list that the cache does not keep. Dispose an unfinished build to abandon it. ISO 32000-2
/// §7.8.2, §8.2 and §10.7.1.
/// </para>
/// </remarks>
public sealed class DisplayListBuild : IDisposable
{
    private readonly PdfPage _page;
    private readonly PdfDocument _document;
    private readonly DisplayLayers _layers;
    private readonly DisplayIntent _intent;
    private readonly ContentOptions _options;
    private readonly long _epoch;
    private DisplayListBuilder? _builder;
    private ContentInterpreter? _interpreter;
    private int _pageGroup;
    private bool _layered;
    private ContentOptions? _runOptions;
    private DisplayList? _result;
    private bool _ended;

    internal DisplayListBuild(PdfPage page, PdfDocument document, DisplayLayers layers, DisplayIntent intent, ContentOptions options)
    {
        _page = page;
        _document = document;
        _layers = layers;
        _intent = intent;
        _options = options;
        _epoch = document.Epoch;
        if (options.OptionalContentState is null && document.Caches.DisplayLists.TryGet(Key, out DisplayListEntry? cached))
        {
            _result = cached.List;
            _ended = true;
        }
    }

    /// <summary>Gets a value indicating whether the list is built.</summary>
    public bool IsCompleted => _result is not null;

    /// <summary>Gets the list once <see cref="IsCompleted"/> is <see langword="true"/>.</summary>
    /// <exception cref="InvalidOperationException">The build is not complete.</exception>
    public DisplayList Result => _result ?? throw new InvalidOperationException("The display list is not built yet; continue the build until it completes.");

    private DisplayListKey Key => new(_page.Dictionary, _layers, _intent, _options.MaxOperands, _options.MaxSaveDepth, _options.MaxNestingDepth);

    /// <summary>
    /// Builds from where the last slice stopped until the list is complete or, after at least one operator, <paramref name="budget"/>
    /// has passed.
    /// </summary>
    /// <param name="budget">
    /// How long the slice may run; <see cref="TimeSpan.Zero"/> runs a few operators, <see cref="Timeout.InfiniteTimeSpan"/> builds the
    /// rest.
    /// </param>
    /// <returns><see langword="true"/> when the list is complete (also when it already was); <see langword="false"/> when the slice stopped for the budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="OperationCanceledException">The options' token was cancelled: the build has ended.</exception>
    /// <exception cref="ObjectDisposedException">The build was disposed, or ended by an exception, before completing.</exception>
    /// <remarks>ISO 32000-2 §7.8.2 and §8.2.</remarks>
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
        long deadline = DisplayImage.Deadline(budget);
        try
        {
            if (_builder is null)
            {
                Start();
            }

            DisplayListBuilder builder = _builder!;
            if (_interpreter is { } interpreter)
            {
                if (!interpreter.ContinuePageRun(deadline))
                {
                    return false;
                }

                interpreter.EndPageRun();
                _interpreter = null;
                builder.ClosePageGroup(_pageGroup);
                if (_layered)
                {
                    builder.CloseLayer();
                }
            }

            // The annotation and form-field layers are built in one step, after the content (as CreateDisplayList builds them).
            if (Layered && (_layers & (DisplayLayers.Annotations | DisplayLayers.FormFields)) != 0)
            {
                IReadOnlyList<Annotations.PdfAnnotation> annotations = _page.Annotations;
                PdfOptionalContentState? state = null;
                if ((_layers & DisplayLayers.Annotations) != 0)
                {
                    builder.AddAnnotations(annotations, widgets: false, _intent, _runOptions!, ref state);
                }

                if ((_layers & DisplayLayers.FormFields) != 0)
                {
                    builder.AddAnnotations(annotations, widgets: true, _intent, _runOptions!, ref state);
                }
            }

            DisplayList list = builder.ToDisplayList(_document);
            _builder = null;
            DisplayListBuilder.Return(builder);
            Complete(list, cache: _options.OptionalContentState is null);
            return true;
        }
        catch
        {
            Abandon();
            throw;
        }
    }

    /// <summary>Whether the build takes the layered path (other layers than the content, or printing), as <c>BuildLayers</c> chooses.</summary>
    private bool Layered => _layers != DisplayLayers.Content || _intent != DisplayIntent.View;

    /// <summary>Takes a builder and starts the page's content run, its top-level operators left to the slices.</summary>
    private void Start()
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        _runOptions = Layered ? DisplayListBuilder.ForIntent(_document, _intent, _options) : _options;
        DisplayListBuilder builder = DisplayListBuilder.Rent();
        _builder = builder;
        if ((_layers & DisplayLayers.Content) == 0)
        {
            return;
        }

        // As the layered build does: the content's own top-level clips and transforms must not reach the annotations drawn after it.
        _layered = Layered && (_layers & ~DisplayLayers.Content) != 0;
        if (_layered)
        {
            builder.OpenLayer();
        }

        _pageGroup = builder.OpenPageGroup(_page, _document);
        _interpreter = ContentInterpreter.BeginPageRun(_page, _document, builder, _runOptions);
    }

    /// <summary>Abandons an unfinished build; a finished one keeps its list.</summary>
    public void Dispose() => Abandon();

    private void Complete(DisplayList list, bool cache)
    {
        _result = list;
        _ended = true;
        if (cache && _document.Caches.DisplayLists.Budget > 0)
        {
            _document.Caches.DisplayLists.Add(Key, new DisplayListEntry(_document.Owner, _epoch, list));
        }
    }

    private void Abandon()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _interpreter?.EndPageRun();
        _interpreter = null;
        if (_builder is { } builder)
        {
            // Nothing owns what was decoded: its pooled buffers go back now.
            builder.ReleaseImages();
            DisplayListBuilder.Return(builder);
            _builder = null;
        }
    }
}
