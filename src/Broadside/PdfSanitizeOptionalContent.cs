namespace Broadside;

/// <summary>What <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/> does with optional content (layers).</summary>
/// <remarks>ISO 32000-2 §8.11.2 to §8.11.4. Visibility is that of the default configuration (<c>OCProperties</c> <c>D</c>) on screen.</remarks>
public enum PdfSanitizeOptionalContent
{
    /// <summary>Optional content is left as it is.</summary>
    Keep,

    /// <summary>
    /// Content in groups that are off is removed: <c>BDC /OC … EMC</c> sequences, XObjects and annotations whose <c>OC</c> is off. The
    /// groups that nothing refers to any more are then removed from <c>OCProperties</c>.
    /// </summary>
    RemoveHidden,

    /// <summary>
    /// As <see cref="RemoveHidden"/>, then every optional-content marker, every <c>OC</c> entry and <c>OCProperties</c> are removed, so
    /// the visible content is shown unconditionally.
    /// </summary>
    FlattenVisible,
}
