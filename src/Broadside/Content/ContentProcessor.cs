namespace Broadside.Content;

/// <summary>
/// A consumer of interpreted content: the one content interpreter runs a content stream through its graphics-state machine and
/// calls these methods, so every consumer (renderer, text extractor, layout detector, editor) sees the same events computed once.
/// Override what you need; every method does nothing by default.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.8.2 (content streams), §8.2 (graphics objects), §8.4 (graphics state), §8.5 (paths and clipping), §9.4 (text
/// objects), §8.8 to §8.10 (XObjects and images), §8.7 (shadings), §14.6 (marked content). Geometry is device independent: paths in
/// user space with <see cref="ContentContext.State"/> holding the CTM to the run's default user space.
/// </para>
/// <para>
/// Payloads are passed by reference and are views valid only during the call; the context and its state likewise. A processor
/// copies what it keeps. Calls come from one thread; a processor may run nested content through the context during a call.
/// Several processors share one run through <see cref="CompositeContentProcessor"/>.
/// </para>
/// <para>
/// Which events are reported today: runs, operators, the state stack, paths, clips and text object boundaries (issue #55). Glyphs,
/// images, forms and marked content come with issue #56, shadings with #79; their methods and payloads are declared now so that
/// processors written today keep working.
/// </para>
/// </remarks>
public abstract class ContentProcessor
{
    /// <summary>Gets the events this processor wants; read once at the start of each run. Default: <see cref="ContentEvents.All"/>.</summary>
    public virtual ContentEvents Events => ContentEvents.All;

    /// <summary>Called when a run starts, before the first operator.</summary>
    /// <param name="context">The run.</param>
    public virtual void BeginRun(ContentContext context)
    {
    }

    /// <summary>Called when a run ends, after the implicit restores that close unbalanced <c>q</c> operators.</summary>
    /// <param name="context">The run.</param>
    public virtual void EndRun(ContentContext context)
    {
    }

    /// <summary>Called for every operator, before it executes (<see cref="ContentEvents.Operators"/>).</summary>
    /// <param name="op">The operator, its operands and its source range.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public virtual void VisitOperator(in ContentOperator op, ContentContext context)
    {
    }

    /// <summary>Called after the state is pushed: by <c>q</c>, and implicitly when a nested stream starts (<see cref="ContentEvents.StateStack"/>).</summary>
    /// <param name="context">The run; its state is the new top, equal to the one saved.</param>
    /// <remarks>ISO 32000-2 §8.4.2.</remarks>
    public virtual void SaveState(ContentContext context)
    {
    }

    /// <summary>Called after the state is popped: by <c>Q</c>, and implicitly at the end of a stream with unbalanced <c>q</c> (<see cref="ContentEvents.StateStack"/>).</summary>
    /// <param name="context">The run; its state is the restored one.</param>
    /// <remarks>ISO 32000-2 §8.4.2.</remarks>
    public virtual void RestoreState(ContentContext context)
    {
    }

    /// <summary>Called when a path object is painted, or ended with <c>n</c> (<see cref="ContentEvents.Paths"/>).</summary>
    /// <param name="path">The path and how it is painted.</param>
    /// <param name="context">The run; its state is the one the path is painted with.</param>
    /// <remarks>ISO 32000-2 §8.5.3, Table 59.</remarks>
    public virtual void PaintPath(in PathEvent path, ContentContext context)
    {
    }

    /// <summary>Called when the clipping path narrows: after the paint that ends a path object with <c>W</c> or <c>W*</c>, at <c>ET</c> for text clips (<see cref="ContentEvents.Clips"/>).</summary>
    /// <param name="clip">The new clip node.</param>
    /// <param name="context">The run; its state already holds the new clip.</param>
    /// <remarks>ISO 32000-2 §8.5.4, §9.3.6.</remarks>
    public virtual void IntersectClip(in ClipEvent clip, ContentContext context)
    {
    }

    /// <summary>Called at <c>BT</c> (<see cref="ContentEvents.Text"/>).</summary>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §9.4.1, Table 105.</remarks>
    public virtual void BeginText(ContentContext context)
    {
    }

    /// <summary>Called for every glyph shown (<see cref="ContentEvents.Glyphs"/>).</summary>
    /// <param name="glyph">The glyph and its placement.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107.</remarks>
    public virtual void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
    }

    /// <summary>Called at <c>ET</c>, before a text clip it sets (<see cref="ContentEvents.Text"/>).</summary>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §9.4.1, Table 105.</remarks>
    public virtual void EndText(ContentContext context)
    {
    }

    /// <summary>Called for an image XObject painted by <c>Do</c> and for an inline image (<see cref="ContentEvents.Images"/>).</summary>
    /// <param name="image">The image.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §8.9.</remarks>
    public virtual void PaintImage(in ImageEvent image, ContentContext context)
    {
    }

    /// <summary>Called for <c>sh</c> (<see cref="ContentEvents.Shadings"/>).</summary>
    /// <param name="shading">The shading.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §8.7.4.2, Table 76.</remarks>
    public virtual void PaintShading(in ShadingEvent shading, ContentContext context)
    {
    }

    /// <summary>Called when a form XObject's content is about to run (<see cref="ContentEvents.Forms"/>).</summary>
    /// <param name="form">The form.</param>
    /// <param name="context">The run, with the state in effect at <c>Do</c>.</param>
    /// <returns><see cref="ContentVisit.Enter"/> to receive the form's events; <see cref="ContentVisit.Skip"/> not to.</returns>
    /// <remarks>ISO 32000-2 §8.10.1.</remarks>
    public virtual ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Enter;

    /// <summary>Called when a form XObject's content has run, for a form whose <see cref="BeginForm"/> returned <see cref="ContentVisit.Enter"/>.</summary>
    /// <param name="form">The form.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §8.10.1.</remarks>
    public virtual void EndForm(in FormEvent form, ContentContext context)
    {
    }

    /// <summary>Called before a Type 3 glyph's procedure runs (<see cref="ContentEvents.Type3GlyphContent"/>). Default: <see cref="ContentVisit.Skip"/>.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="context">The run.</param>
    /// <returns><see cref="ContentVisit.Enter"/> to receive the procedure's events.</returns>
    /// <remarks>ISO 32000-2 §9.6.4.</remarks>
    public virtual ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => ContentVisit.Skip;

    /// <summary>Called after a Type 3 glyph's procedure ran, for a glyph whose <see cref="BeginType3Glyph"/> returned <see cref="ContentVisit.Enter"/>.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §9.6.4.</remarks>
    public virtual void EndType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
    }

    /// <summary>Called at <c>BMC</c> and <c>BDC</c> (<see cref="ContentEvents.MarkedContent"/>).</summary>
    /// <param name="mc">The sequence.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §14.6, Table 352.</remarks>
    public virtual void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
    }

    /// <summary>Called at <c>EMC</c>, and implicitly for sequences still open at the end of a stream (<see cref="ContentEvents.MarkedContent"/>).</summary>
    /// <param name="mc">The sequence that ends.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §14.6, Table 352.</remarks>
    public virtual void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
    }

    /// <summary>Called at <c>MP</c> and <c>DP</c> (<see cref="ContentEvents.MarkedContent"/>).</summary>
    /// <param name="mc">The point.</param>
    /// <param name="context">The run.</param>
    /// <remarks>ISO 32000-2 §14.6, Table 352.</remarks>
    public virtual void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context)
    {
    }
}
