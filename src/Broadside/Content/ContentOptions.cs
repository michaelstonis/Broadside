namespace Broadside.Content;

/// <summary>Limits and cancellation for one content run, set fluently with <c>With*</c> methods.</summary>
/// <remarks>
/// ISO 32000-2 §7.8.2 and §8.4.2 set no limits; these protect against malformed and hostile content. Reaching a limit is a
/// diagnostic, never an exception, in lenient mode.
/// </remarks>
public sealed class ContentOptions
{
    /// <summary>The default of <see cref="MaxOperands"/>: 65,536.</summary>
    public const int DefaultMaxOperands = 1 << 16;

    /// <summary>The default of <see cref="MaxSaveDepth"/>: 4,096.</summary>
    public const int DefaultMaxSaveDepth = 4096;

    /// <summary>The default of <see cref="MaxNestingDepth"/>: 50.</summary>
    public const int DefaultMaxNestingDepth = 50;

    private int _maxOperands = DefaultMaxOperands;
    private int _maxSaveDepth = DefaultMaxSaveDepth;
    private int _maxNestingDepth = DefaultMaxNestingDepth;

    /// <summary>
    /// Gets or sets the most operands held for one operator, the elements of array and dictionary operands included. When more are
    /// written, the oldest are dropped with a diagnostic. Default 65,536.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 16.</exception>
    public int MaxOperands
    {
        get => _maxOperands;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 16);
            _maxOperands = value;
        }
    }

    /// <summary>
    /// Gets or sets the deepest the graphics state stack may grow; a <c>q</c> beyond it is ignored with a diagnostic, as is its
    /// matching <c>Q</c>. Default 4,096.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.4.2.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxSaveDepth
    {
        get => _maxSaveDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxSaveDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the deepest content streams may nest: form XObjects, tiling patterns, Type 3 glyphs, soft masks and
    /// appearances share one budget. Default 50.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.10.1, §8.7.3, §9.6.4, §11.6.5.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxNestingDepth
    {
        get => _maxNestingDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxNestingDepth = value;
        }
    }

    /// <summary>Gets or sets the token that cancels the run; it is checked every 1,024 operators and at every nested run.</summary>
    /// <remarks>
    /// A display-list build (<see cref="PdfPage.CreateDisplayList(ContentOptions)"/>) also hands the token to the image codecs that
    /// decode the page's images, so a long decode stops too. A cancelled run throws <see cref="OperationCanceledException"/> and
    /// leaves nothing behind that changes a later run.
    /// </remarks>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Gets or sets what receives the run's progress: the share of the run's content stream executed so far, from 0 to 1;
    /// <see langword="null"/>, the default, for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported on the running thread, synchronously, where the run checks <see cref="CancellationToken"/> (every 1,024 operators),
    /// then once with exactly 1 when the run completes; a run that throws reports no completion. Values never decrease. The share is
    /// measured in bytes of the top-level stream (for a page, its <c>Contents</c> joined, ISO 32000-2 §7.7.3.3 Table 31), so the
    /// work of nested forms counts at the position of their <c>Do</c> operator.
    /// </para>
    /// <para>
    /// The receiver may cancel <see cref="CancellationToken"/>'s source; the run stops before its next operator. A host without
    /// threads (Blazor WebAssembly) can bound a build that way, by cancelling once its time slice is used.
    /// </para>
    /// </remarks>
    public IProgress<double>? Progress { get; set; }

    /// <summary>
    /// Gets or sets the states of the optional content groups that decide what is hidden (<see cref="ContentContext.IsHidden"/>);
    /// <see langword="null"/>, the default, for the document's default configuration.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.2 and §8.11.4.3.</remarks>
    public PdfOptionalContentState? OptionalContentState { get; set; }

    /// <summary>Sets <see cref="OptionalContentState"/>.</summary>
    /// <param name="state">The group states, or <see langword="null"/> for the document's defaults.</param>
    /// <returns>These options.</returns>
    public ContentOptions WithOptionalContentState(PdfOptionalContentState? state)
    {
        OptionalContentState = state;
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the run is for printing: an image XObject without <c>OC</c> is then painted as its
    /// alternate marked <c>DefaultForPrinting</c> (ISO 32000-2 §8.9.5.4 rule d). Set by display-list builds for
    /// <see cref="Rendering.DisplayIntent.Print"/> on their own copy of the caller's options.
    /// </summary>
    internal bool Printing { get; set; }

    /// <summary>Returns a copy of these options, every setting included.</summary>
    internal ContentOptions Clone() => (ContentOptions)MemberwiseClone();

    /// <summary>Sets <see cref="MaxOperands"/>.</summary>
    /// <param name="maxOperands">The limit, at least 16.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOperands"/> is less than 16.</exception>
    public ContentOptions WithMaxOperands(int maxOperands)
    {
        MaxOperands = maxOperands;
        return this;
    }

    /// <summary>Sets <see cref="MaxSaveDepth"/>.</summary>
    /// <param name="maxSaveDepth">The limit, positive.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSaveDepth"/> is not positive.</exception>
    public ContentOptions WithMaxSaveDepth(int maxSaveDepth)
    {
        MaxSaveDepth = maxSaveDepth;
        return this;
    }

    /// <summary>Sets <see cref="MaxNestingDepth"/>.</summary>
    /// <param name="maxNestingDepth">The limit, positive.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxNestingDepth"/> is not positive.</exception>
    public ContentOptions WithMaxNestingDepth(int maxNestingDepth)
    {
        MaxNestingDepth = maxNestingDepth;
        return this;
    }

    /// <summary>Sets <see cref="Progress"/>.</summary>
    /// <param name="progress">What receives the share of the content run so far, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    public ContentOptions WithProgress(IProgress<double>? progress)
    {
        Progress = progress;
        return this;
    }

    /// <summary>Sets <see cref="CancellationToken"/>.</summary>
    /// <param name="cancellationToken">The token.</param>
    /// <returns>These options.</returns>
    public ContentOptions WithCancellation(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        return this;
    }
}
