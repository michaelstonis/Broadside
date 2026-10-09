using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// The run a processor is called from: the current graphics state, what is being interpreted, and how to resolve clip handles.
/// One instance serves a whole run; it is valid only during the run's callbacks.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2 (content streams), §7.8.3 (resources), §8.4 (graphics state), §8.3.2 (coordinate spaces).</remarks>
public sealed class ContentContext
{
    private readonly ContentInterpreter _interpreter;

    internal ContentContext(ContentInterpreter interpreter) => _interpreter = interpreter;

    /// <summary>Gets the current graphics state, by read-only reference into the interpreter's stack: copy it to keep it.</summary>
    /// <remarks>ISO 32000-2 §8.4.1.</remarks>
    public ref readonly GraphicsState State => ref _interpreter.State;

    /// <summary>Gets the number of states saved by <c>q</c> (and implicit saves) above the run's initial state.</summary>
    /// <remarks>ISO 32000-2 §8.4.2.</remarks>
    public int StateDepth => _interpreter.StateDepth;

    /// <summary>Gets what kind of content stream is running.</summary>
    public ContentRunKind RunKind { get; internal set; }

    /// <summary>Gets the nesting depth of the running stream: 0 for the top-level run, 1 inside a form it paints, and so on.</summary>
    public int Depth { get; internal set; }

    /// <summary>Gets the document the content belongs to.</summary>
    public PdfDocument Document { get; internal set; } = null!;

    /// <summary>Gets the page being interpreted, or <see langword="null"/> when the run is not over a page.</summary>
    public PdfPage? Page { get; internal set; }

    /// <summary>Gets the resource dictionary names in the running stream resolve against, or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §7.8.3: a page's (inherited) <c>Resources</c>; a form's own, else the page's.</remarks>
    public CosDictionary? Resources { get; internal set; }

    /// <summary>
    /// Gets the CTM at the start of the running stream, the space a pattern named in it is relative to: the identity for a page,
    /// the CTM at <c>Do</c> times the form matrix for a form.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.2: the pattern matrix maps pattern space to the default space of the stream that uses the pattern.</remarks>
    public Matrix StreamBaseMatrix { get; internal set; } = Matrix.Identity;

    /// <summary>Gets a value indicating whether optional content currently hides what is painted.</summary>
    /// <remarks>ISO 32000-2 §8.11.3.1. Always false until optional content is evaluated (issue #76).</remarks>
    public bool IsHidden { get; internal set; }

    /// <summary>
    /// Gets the content stream the current operator is in, or <see langword="null"/> when that stream is a direct object; for a
    /// page whose <c>Contents</c> is an array, the part being read.
    /// </summary>
    public CosReference? CurrentStream => _interpreter.CurrentStream;

    /// <summary>Gets the index in the page's <c>Contents</c> array of the part being read; 0 for a single stream.</summary>
    public int CurrentPartIndex => _interpreter.CurrentPartIndex;

    /// <summary>Gets the token that cancels the run.</summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>Returns the clip node a clip handle refers to, such as <see cref="GraphicsState.ClipHandle"/>.</summary>
    /// <param name="handle">The handle; 0 for the run's initial clipping path.</param>
    /// <returns>The node; a node of kind <see cref="ClipKind.Initial"/> for 0 and for a handle this run did not issue.</returns>
    /// <remarks>ISO 32000-2 §8.5.4.</remarks>
    public ClipView GetClip(int handle) => _interpreter.Clips.Get(handle);
}
