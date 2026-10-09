namespace Broadside.Content;

/// <summary>What a clip node intersects the clipping path with.</summary>
/// <remarks>ISO 32000-2 §8.5.4 (paths), §8.10.1 (a form's bounding box), §9.3.6 (text rendering modes 4 to 7).</remarks>
public enum ClipKind
{
    /// <summary>The run's initial clipping path: for a page, its media box (§8.5.1). Only handle 0 has this kind.</summary>
    Initial,

    /// <summary>A path, after <c>W</c> or <c>W*</c> and the painting operator that follows.</summary>
    Path,

    /// <summary>A rectangle given as a path, such as a form XObject's bounding box.</summary>
    Rectangle,

    /// <summary>The glyph outlines of a text object shown with a clipping rendering mode, applied at its <c>ET</c>.</summary>
    Text,
}
