using Broadside.Rendering;
using Broadside.Security;

namespace Broadside.Viewer;

/// <summary>How a <see cref="DocumentSession"/> opens its document, where it works and which backend draws its tiles.</summary>
/// <remarks>
/// Spec #33 story 54. Mutable and fluent like the other options objects: <c>Use*</c> selects an implementation, <c>With*</c> sets a
/// value. A session copies what it needs when it opens, so changing the options afterwards does not affect it.
/// </remarks>
public sealed class ViewerOptions
{
    /// <summary>The default <see cref="SliceBudget"/>: 8 ms, half a 60 Hz frame.</summary>
    public static readonly TimeSpan DefaultSliceBudget = TimeSpan.FromMilliseconds(8);

    private TimeSpan _sliceBudget = DefaultSliceBudget;
    private int _maxConcurrency = Math.Max(1, Environment.ProcessorCount - 1); // one core stays with the UI thread (#357)

    /// <summary>Gets or sets the engine that opens the document; <see langword="null"/> (the default) builds one from <see cref="DocumentOptions"/>.</summary>
    public PdfEngine? Engine { get; set; }

    /// <summary>Gets or sets the document options an engine is built from when <see cref="Engine"/> is not set; <see langword="null"/> means defaults.</summary>
    public PdfOptions? DocumentOptions { get; set; }

    /// <summary>Gets or sets the credentials for an encrypted document (ISO 32000-2 §7.6), or <see langword="null"/>. Not used by remote documents.</summary>
    public PdfCredentials? Credentials { get; set; }

    /// <summary>Gets or sets where the session works (<see cref="ViewerThreading.Automatic"/> by default).</summary>
    public ViewerThreading Threading { get; set; }

    /// <summary>Gets or sets the intent annotations are drawn for (ISO 32000-2 §12.5.3; <see cref="DisplayIntent.View"/> by default).</summary>
    public DisplayIntent Intent { get; set; }

    /// <summary>Gets or sets the length of one drawing slice in <see cref="ViewerThreading.Cooperative"/> mode (<see cref="DefaultSliceBudget"/> by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    public TimeSpan SliceBudget
    {
        get => _sliceBudget;
        set
        {
            if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A slice must be positive or infinite.");
            }

            _sliceBudget = value;
        }
    }

    /// <summary>
    /// Gets or sets how many tiles render at once (by default one less than the processor count, at least one, so the UI thread keeps
    /// a core); further renders wait their turn.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxConcurrency = value;
        }
    }

    /// <summary>Gets or sets the factory of tile backends, the backend choice (<see cref="SoftwareTileBackend"/> by default).</summary>
    public Func<ITileBackend>? BackendFactory { get; set; }

    /// <summary>
    /// Gets or sets how a <see cref="ViewerThreading.Cooperative"/> session yields between slices; <see langword="null"/> (the default)
    /// awaits <see cref="Task.Yield"/>. A browser shell can yield to its event loop instead, for example with a zero delay.
    /// </summary>
    public Func<CancellationToken, ValueTask>? Yield { get; set; }

    /// <summary>Sets <see cref="Engine"/>.</summary>
    /// <param name="engine">The engine.</param>
    /// <returns>These options.</returns>
    public ViewerOptions UseEngine(PdfEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        return this;
    }

    /// <summary>Sets <see cref="DocumentOptions"/>.</summary>
    /// <param name="options">The document options.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithDocumentOptions(PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DocumentOptions = options;
        return this;
    }

    /// <summary>Sets <see cref="Credentials"/>.</summary>
    /// <param name="credentials">The credentials.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithCredentials(PdfCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        Credentials = credentials;
        return this;
    }

    /// <summary>Sets <see cref="Threading"/>.</summary>
    /// <param name="threading">Where the session works.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithThreading(ViewerThreading threading)
    {
        Threading = threading;
        return this;
    }

    /// <summary>Sets <see cref="Intent"/>.</summary>
    /// <param name="intent">The intent.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithIntent(DisplayIntent intent)
    {
        Intent = intent;
        return this;
    }

    /// <summary>Sets <see cref="SliceBudget"/>.</summary>
    /// <param name="budget">The slice.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithSliceBudget(TimeSpan budget)
    {
        SliceBudget = budget;
        return this;
    }

    /// <summary>Sets <see cref="MaxConcurrency"/>.</summary>
    /// <param name="maxConcurrency">How many tiles render at once.</param>
    /// <returns>These options.</returns>
    public ViewerOptions WithMaxConcurrency(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        return this;
    }

    /// <summary>
    /// Gets or sets the renderer that draws the tiles it accepts outside the viewer's thread (a browser Web Worker), or
    /// <see langword="null"/> (the default) to draw every tile with <see cref="BackendFactory"/>'s backends. The session that opens
    /// with these options owns it and disposes it when it closes, so set it on options used for one session.
    /// </summary>
    /// <remarks>Spec #33 viewer capability 1 (#357), ADR 0013.</remarks>
    public IRemoteTileRenderer? RemoteRenderer { get; set; }

    /// <summary>Selects the renderer that draws tiles outside the viewer's thread (see <see cref="RemoteRenderer"/>).</summary>
    /// <param name="renderer">The renderer; the session opened with these options disposes it.</param>
    /// <returns>These options.</returns>
    /// <remarks>Spec #33 viewer capability 1 (#357), ADR 0013.</remarks>
    public ViewerOptions UseRemoteRenderer(IRemoteTileRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        RemoteRenderer = renderer;
        return this;
    }

    /// <summary>Sets <see cref="BackendFactory"/>.</summary>
    /// <param name="factory">Creates one backend per concurrent render.</param>
    /// <returns>These options.</returns>
    public ViewerOptions UseBackend(Func<ITileBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        BackendFactory = factory;
        return this;
    }

    /// <summary>Sets <see cref="Yield"/>.</summary>
    /// <param name="yield">Yields between cooperative slices.</param>
    /// <returns>These options.</returns>
    public ViewerOptions UseYield(Func<CancellationToken, ValueTask> yield)
    {
        ArgumentNullException.ThrowIfNull(yield);
        Yield = yield;
        return this;
    }

    internal bool IsCooperative => Threading switch
    {
        ViewerThreading.Cooperative => true,
        ViewerThreading.Background => false,
        _ => OperatingSystem.IsBrowser() || OperatingSystem.IsWasi(),
    };
}
