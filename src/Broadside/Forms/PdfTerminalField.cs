using Broadside.Annotations;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>A field without child fields: it holds a value and is shown on pages by its widget annotations.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.2: a terminal field's children are widget annotations, or, when it has a single widget, field and widget may be
/// one merged dictionary. <see cref="Widgets"/> and <see cref="PdfWidgetAnnotation.Field"/> link the two both ways with the same
/// instances <see cref="PdfPage.Annotations"/> lists.
/// </remarks>
public abstract class PdfTerminalField : PdfField
{
    private readonly CosObject[] _widgets;

    private protected PdfTerminalField(FieldInfo info, PdfFieldKind kind, CosObject[] widgets)
        : base(info, kind) => _widgets = widgets;

    /// <summary>
    /// Gets the field's widget annotations, in <c>Kids</c> order: the field dictionary itself when it is merged with its only widget.
    /// Each is the instance the page holding it lists; a widget no page lists has no <see cref="PdfAnnotation.Page"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.2, §12.7.4.1 (Table 226, <c>Kids</c>) and §12.5.6.19. A kid whose subtype is not <c>Widget</c> is not listed.
    /// </remarks>
    public IReadOnlyList<PdfWidgetAnnotation> Widgets
    {
        get
        {
            var widgets = new List<PdfWidgetAnnotation>(_widgets.Length);
            foreach (CosObject element in _widgets)
            {
                // A kid that is not a widget annotation (a Kids-only stub without Subtype, as in the radio example of §12.7.5.2.4)
                // gets no annotation view here, so reading the field records nothing about it.
                if (Document.Resolve(element) is CosDictionary dictionary && PdfWidgetAnnotation.IsWidget(Document, dictionary) &&
                    Document.AnnotationIndex.Find(element, from: null) is PdfWidgetAnnotation widget)
                {
                    widgets.Add(widget);
                }
            }

            return widgets;
        }
    }

    /// <summary>Gets the widget dictionaries (or references to them) in <c>Kids</c> order, as the tree read them.</summary>
    internal IReadOnlyList<CosObject> WidgetElements => _widgets;

    /// <summary>Returns the position of <paramref name="widget"/> among the field's widgets (its index in <c>Kids</c>), or -1.</summary>
    internal int IndexOfWidget(CosDictionary widget)
    {
        for (int index = 0; index < _widgets.Length; index++)
        {
            if (ReferenceEquals(Document.Resolve(_widgets[index]), widget))
            {
                return index;
            }
        }

        return -1;
    }
}
