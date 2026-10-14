namespace Broadside.Rendering;

/// <summary>
/// The layers a page's display list can hold, so that a viewer can redraw the annotation and form-field overlays without
/// rebuilding or re-rasterizing the page content. A list holding several layers paints them in this order.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5 (annotations are drawn over the page content, §12.5.5) and §12.7 (an interactive form's fields appear through
/// their widget annotations, §12.5.6.19). Splitting widgets from the other annotations changes their relative order on the page:
/// every field is drawn over every other annotation, which is what a viewer that shows fields as a layer of their own does.
/// </remarks>
[Flags]
public enum DisplayLayers
{
    /// <summary>Nothing: the list is empty.</summary>
    None = 0,

    /// <summary>The page's content streams (ISO 32000-2 §7.7.3.3, Table 31, <c>Contents</c>).</summary>
    Content = 1,

    /// <summary>The appearances of every annotation but widgets (ISO 32000-2 §12.5.5), in <c>Annots</c> order.</summary>
    Annotations = 2,

    /// <summary>The appearances of widget annotations (ISO 32000-2 §12.5.6.19, §12.7), in <c>Annots</c> order.</summary>
    FormFields = 4,

    /// <summary>Content, then annotations, then form fields: the whole page as a viewer shows it.</summary>
    All = Content | Annotations | FormFields,
}
