namespace Broadside.Annotations;

/// <summary>Which of an annotation's appearances to use: normal, rollover or down.</summary>
/// <remarks>ISO 32000-2 §12.5.5, Table 170: <c>N</c>, <c>R</c> and <c>D</c>; the rollover and down appearances default to the normal one.</remarks>
public enum PdfAppearanceMode
{
    /// <summary><c>N</c>: the appearance when the user is not interacting with the annotation.</summary>
    Normal,

    /// <summary><c>R</c>: the appearance when the cursor is over the annotation without the mouse button pressed.</summary>
    Rollover,

    /// <summary><c>D</c>: the appearance when the mouse button is pressed or held down within the annotation.</summary>
    Down,
}
