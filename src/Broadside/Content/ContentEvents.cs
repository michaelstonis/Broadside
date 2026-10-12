namespace Broadside.Content;

/// <summary>
/// The events a <see cref="ContentProcessor"/> wants. The interpreter reads <see cref="ContentProcessor.Events"/> once per run and
/// skips the work no processor needs: no path is built when nobody wants paths or clips, no glyph is decoded when nobody wants glyphs.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.2: the graphics objects a content stream describes. Graphics state changes are always applied, whatever is
/// requested; only the reporting is skipped.
/// </remarks>
[Flags]
public enum ContentEvents
{
    /// <summary>Only <see cref="ContentProcessor.BeginRun"/> and <see cref="ContentProcessor.EndRun"/>.</summary>
    None = 0,

    /// <summary><see cref="ContentProcessor.PaintPath"/>: path objects as they are painted (§8.5).</summary>
    Paths = 1 << 0,

    /// <summary><see cref="ContentProcessor.IntersectClip"/>: changes of the clipping path (§8.5.4, §9.3.6). Without it, clip handles stay 0.</summary>
    Clips = 1 << 1,

    /// <summary><see cref="ContentProcessor.BeginText"/> and <see cref="ContentProcessor.EndText"/>: text objects (§9.4).</summary>
    Text = 1 << 2,

    /// <summary><see cref="ContentProcessor.ShowGlyph"/>: every glyph shown (§9.4.3).</summary>
    Glyphs = 1 << 3,

    /// <summary><see cref="ContentProcessor.PaintImage"/>: image XObjects and inline images (§8.9).</summary>
    Images = 1 << 4,

    /// <summary><see cref="ContentProcessor.PaintShading"/>: the <c>sh</c> operator (§8.7.4.2).</summary>
    Shadings = 1 << 5,

    /// <summary><see cref="ContentProcessor.BeginForm"/> and <see cref="ContentProcessor.EndForm"/>: form XObjects (§8.10).</summary>
    Forms = 1 << 6,

    /// <summary>The marked-content events (§14.6).</summary>
    MarkedContent = 1 << 7,

    /// <summary><see cref="ContentProcessor.SaveState"/> and <see cref="ContentProcessor.RestoreState"/>: the graphics state stack (§8.4.2).</summary>
    StateStack = 1 << 8,

    /// <summary><see cref="ContentProcessor.VisitOperator"/>: every operator with its operands and source range, before it executes (§7.8.2).</summary>
    Operators = 1 << 9,

    /// <summary>Paint, glyph, image and shading events for content hidden by optional content, flagged as hidden (§8.11.3.1).</summary>
    HiddenContent = 1 << 10,

    /// <summary>The events inside Type 3 glyph procedures run through <see cref="ContentProcessor.BeginType3Glyph"/> (§9.6.4).</summary>
    Type3GlyphContent = 1 << 11,

    /// <summary>Every event.</summary>
    All = Paths | Clips | Text | Glyphs | Images | Shadings | Forms | MarkedContent | StateStack | Operators | HiddenContent | Type3GlyphContent,
}
