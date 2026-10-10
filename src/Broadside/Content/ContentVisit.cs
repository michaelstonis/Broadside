namespace Broadside.Content;

/// <summary>What a processor wants done with a nested content stream (a form XObject or a Type 3 glyph procedure) it is offered.</summary>
/// <remarks>
/// ISO 32000-2 §8.10 and §9.6.4. A processor that caches a form as a unit answers <see cref="Skip"/> and runs the form itself
/// through the run context when it needs it.
/// </remarks>
public enum ContentVisit
{
    /// <summary>Interpret the nested stream and report its events.</summary>
    Enter,

    /// <summary>Do not report the nested stream's events to this processor.</summary>
    Skip,
}
