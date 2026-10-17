namespace Broadside.Viewer;

/// <summary>One annotation of a page an <see cref="AnnotationLayer"/> has loaded, by its place in the layer's page model.</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Index">
/// The annotation's place in the page model: annotations in drawing order (the page's <c>Annots</c> order, widgets after the others),
/// leaving out popups and annotations that are never shown. Valid until the page's annotations change.
/// </param>
/// <remarks>Spec #33 viewer capability 6; ISO 32000-2 §12.5.2.</remarks>
public readonly record struct AnnotationTarget(int PageIndex, int Index);

/// <summary>What an annotation does when the user points at it, for the shell's cursor and the keyboard.</summary>
/// <remarks>Spec #33 viewer capability 6.</remarks>
public enum AnnotationRole
{
    /// <summary>A link annotation (ISO 32000-2 §12.5.6.5): activating it raises <see cref="AnnotationLayer.ActionRequested"/>.</summary>
    Link,

    /// <summary>A widget of an interactive form field (§12.5.6.19): activating it focuses or toggles the field.</summary>
    Field,

    /// <summary>Any other annotation: it can be selected, its note read and, unless locked, moved or deleted.</summary>
    Annotation,
}

/// <summary>An area of a page whose drawing changed after an edit through an <see cref="AnnotationLayer"/>.</summary>
/// <remarks>Spec #33 viewer capability 6.</remarks>
public sealed class PageInvalidatedEventArgs : EventArgs
{
    internal PageInvalidatedEventArgs(int pageIndex, PdfRectangle? area, Rendering.DisplayLayers layers)
    {
        PageIndex = pageIndex;
        Area = area;
        Layers = layers;
    }

    /// <summary>Gets the zero-based page.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the changed area in the page's default user space, or <see langword="null"/> for the whole page.</summary>
    public PdfRectangle? Area { get; }

    /// <summary>Gets the layers that changed.</summary>
    public Rendering.DisplayLayers Layers { get; }
}
