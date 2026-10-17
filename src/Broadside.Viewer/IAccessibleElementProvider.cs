namespace Broadside.Viewer;

/// <summary>
/// Supplies a page's accessible elements that are not page content (form fields, links, comments) in keyboard focus order:
/// the seam the viewer's annotation, link and form layers implement.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5 (annotations), §12.7 (forms), Spec #33 viewer capability 8. The bridge appends the elements to the page's
/// node after its content, in the order given, and <see cref="AccessibleNavigation.Control"/> walks them. When an element's
/// value or state changes, call <see cref="AccessibilityBridge.InvalidatePage"/>; when the layer moves its keyboard focus,
/// call <see cref="AccessibilityBridge.SetFocus"/> with <see cref="AccessibilityBridge.FindNode"/>.
/// </remarks>
public interface IAccessibleElementProvider
{
    /// <summary>Adds a page's elements, in focus order.</summary>
    /// <param name="page">The page.</param>
    /// <param name="pageIndex">The page's zero-based index.</param>
    /// <param name="destination">Receives the elements.</param>
    void GetElements(PdfPage page, int pageIndex, ICollection<AccessibleElement> destination);
}

/// <summary>
/// An accessible element that is not page content, such as a form field, a link or a comment, supplied by an
/// <see cref="IAccessibleElementProvider"/>.
/// </summary>
/// <param name="Role">The element's role.</param>
/// <param name="Bounds">The element's rectangle on its page, in default user space.</param>
/// <remarks>ISO 32000-2 §12.5, §12.7, Spec #33 viewer capability 8.</remarks>
public readonly record struct AccessibleElement(AccessibleRole Role, PdfRectangle Bounds)
{
    /// <summary>Gets the element's accessible name, such as a field's alternate name (TU).</summary>
    public string? Name { get; init; }

    /// <summary>Gets the element's value, such as a text field's text.</summary>
    public string? Value { get; init; }

    /// <summary>Gets the element's states; <see cref="AccessibleStates.Focusable"/> is always added.</summary>
    public AccessibleStates States { get; init; }

    /// <summary>Gets the object the element stands for (an annotation or a field), for <see cref="AccessibilityBridge.FindNode"/>.</summary>
    public object? Source { get; init; }
}
