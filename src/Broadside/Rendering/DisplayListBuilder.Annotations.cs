using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Rendering;

/// <summary>
/// Display-list layers (ISO 32000-2 §12.5, §12.7): the page content, the annotations' appearances and the widgets' appearances, each
/// annotation between <see cref="DisplayOperationKind.BeginAnnotation"/> and <see cref="DisplayOperationKind.EndAnnotation"/>.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private static readonly CosName Isolated = new("I");
    private static readonly CosName Knockout = new("K");

    private DisplayAnnotation[] _annotations = new DisplayAnnotation[4];
    private int _annotationCount;

    /// <summary>Builds the display list of the <paramref name="layers"/> of <paramref name="page"/>, in content, annotations, form fields order.</summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The page's document.</param>
    /// <param name="layers">The layers the list holds.</param>
    /// <param name="intent">Whether the list is for the screen or for printing.</param>
    /// <param name="options">Limits, optional-content state and cancellation for the content and appearance runs.</param>
    /// <returns>The list.</returns>
    public static DisplayList Build(PdfPage page, PdfDocument document, DisplayLayers layers, DisplayIntent intent, ContentOptions options)
    {
        DisplayListBuilder builder = _cached ?? new DisplayListBuilder();
        _cached = null;
        try
        {
            if ((layers & DisplayLayers.Content) != 0)
            {
                // The content's own top-level clips and transforms must not reach the annotations drawn after it.
                bool more = (layers & ~DisplayLayers.Content) != 0;
                if (more)
                {
                    builder.OpenLayer();
                }

                ContentInterpreter.RunPage(page, document, builder, options);
                if (more)
                {
                    builder.CloseLayer();
                }
            }

            if ((layers & (DisplayLayers.Annotations | DisplayLayers.FormFields)) != 0)
            {
                IReadOnlyList<PdfAnnotation> annotations = page.Annotations;
                PdfOptionalContentState? state = null;
                if ((layers & DisplayLayers.Annotations) != 0)
                {
                    builder.AddAnnotations(annotations, widgets: false, intent, options, ref state);
                }

                if ((layers & DisplayLayers.FormFields) != 0)
                {
                    builder.AddAnnotations(annotations, widgets: true, intent, options, ref state);
                }
            }

            return builder.ToDisplayList(document);
        }
        finally
        {
            builder.Reset();
            _cached = builder;
        }
    }

    private void ResetAnnotations()
    {
        Array.Clear(_annotations, 0, _annotationCount);
        _annotationCount = 0;
    }

    /// <summary>Saves the state around a layer, as <c>q</c> would, the alpha constants and blend mode with it.</summary>
    private void OpenLayer()
    {
        Append(ref _saved, ref _savedCount, _current);
        Append(ref _savedAt, ref _savedAtCount, _operationCount);
        SaveCompositing();
        AddOperation(new DisplayOperation(DisplayOperationKind.SaveState, default, 0, 0));
    }

    /// <summary>Restores the state saved by <see cref="OpenLayer"/>, dropping the pair when nothing was painted.</summary>
    private void CloseLayer() => Close(DisplayOperationKind.RestoreState);

    /// <summary>Pops the innermost saved state and records <paramref name="kind"/>, or drops everything since the save when nothing was painted.</summary>
    private bool Close(DisplayOperationKind kind)
    {
        _current = _saved[--_savedCount];
        int saveAt = _savedAt[--_savedAtCount];

        // The backend restores its alpha constants and blend mode with the state (§11.6.4.4), so the content's top-level gs
        // operators do not reach the annotations, nor one annotation's the next.
        RestoreCompositing();
        if (!PaintsSince(saveAt + 1))
        {
            _operationCount = saveAt;
            return false;
        }

        AddOperation(new DisplayOperation(kind, default, 0, 0));
        return true;
    }

    /// <summary>
    /// Adds the appearance of every annotation of one layer that the flags, the intent and optional content leave visible: widgets
    /// for the form-field layer, the others for the annotation layer.
    /// </summary>
    private void AddAnnotations(IReadOnlyList<PdfAnnotation> annotations, bool widgets, DisplayIntent intent, ContentOptions options, ref PdfOptionalContentState? state)
    {
        foreach (PdfAnnotation annotation in annotations)
        {
            if ((annotation.Kind == PdfAnnotationKind.Widget) != widgets || !IsDrawn(annotation, intent))
            {
                continue;
            }

            PdfDocument document = annotation.Document;
            if (document.OptionalContent is { } properties)
            {
                state ??= options.OptionalContentState ?? properties.GetDefaultStates();
                if (!annotation.IsVisible(state))
                {
                    continue;
                }
            }

            if (annotation.GetAppearance() is not { } appearance)
            {
                ReportNoAppearance(annotation);
                continue;
            }

            if (appearance.Dictionary.TryGetValue(AnnotationNames.OC, out CosObject? group)
                && document.OptionalContent is { } formProperties
                && !formProperties.IsVisible(group, state ??= options.OptionalContentState ?? formProperties.GetDefaultStates()))
            {
                continue;
            }

            if (annotation.GetAppearanceMatrix(appearance) is not { } matrix)
            {
                continue;
            }

            Add(annotation, appearance, matrix, intent, options);
        }
    }

    /// <summary>Records one annotation: its placement, then its appearance's paints, then its end (all dropped when it paints nothing).</summary>
    private void Add(PdfAnnotation annotation, PdfFormXObject appearance, in Matrix matrix, DisplayIntent intent, ContentOptions options)
    {
        PdfAnnotationFlags flags = annotation.Flags;
        bool screen = intent == DisplayIntent.View;

        // §12.5.6.4: a text annotation behaves as if NoZoom and NoRotate were set, whatever F says.
        bool text = annotation.Kind == PdfAnnotationKind.Text;
        bool noZoom = screen && (text || (flags & PdfAnnotationFlags.NoZoom) != 0);
        bool noRotate = screen && (text || (flags & PdfAnnotationFlags.NoRotate) != 0);
        double opacity = annotation.NonStrokingOpacity;
        opacity = double.IsNaN(opacity) ? 1 : Math.Clamp(opacity, 0, 1);
        bool isolated = false;
        bool knockout = false;
        if (appearance.Group is { } group)
        {
            isolated = group.TryGetValue(Isolated, out CosObject? i) && annotation.Document.Resolve(i) is CosBoolean { Value: true };
            knockout = group.TryGetValue(Knockout, out CosObject? k) && annotation.Document.Resolve(k) is CosBoolean { Value: true };
        }

        var record = new DisplayAnnotation(
            annotation.Kind,
            annotation.Reference,
            annotation.Rect,
            noZoom,
            noRotate,
            opacity,
            annotation.BlendMode,
            isolated,
            knockout);
        int index = _annotationCount;
        Append(ref _annotations, ref _annotationCount, record);
        Append(ref _saved, ref _savedCount, _current);
        Append(ref _savedAt, ref _savedAtCount, _operationCount);
        SaveCompositing();
        AddOperation(new DisplayOperation(DisplayOperationKind.BeginAnnotation, default, index, 0));

        ContentInterpreter.RunAppearance(appearance, matrix, annotation.Page, annotation.Reference ?? annotation.Page?.Reference, annotation.Document, this, options);

        if (!Close(DisplayOperationKind.EndAnnotation))
        {
            _annotations[--_annotationCount] = default;
        }
    }

    /// <summary>Applies Table 167's Hidden, Invisible, NoView and Print flags for the intent; popups are the viewer's, not the page's (§12.5.6.14).</summary>
    private static bool IsDrawn(PdfAnnotation annotation, DisplayIntent intent)
    {
        if (annotation.Kind == PdfAnnotationKind.Popup)
        {
            return false;
        }

        PdfAnnotationFlags flags = annotation.Flags;
        if ((flags & PdfAnnotationFlags.Hidden) != 0 || ((flags & PdfAnnotationFlags.Invisible) != 0 && annotation.Kind == PdfAnnotationKind.Unknown))
        {
            return false;
        }

        return intent == DisplayIntent.Print ? (flags & PdfAnnotationFlags.Print) != 0 : (flags & PdfAnnotationFlags.NoView) == 0;
    }

    /// <summary>
    /// Records that a drawn annotation shows nothing because it has no normal appearance for its state: the renderer draws
    /// appearance streams only and does not generate one (§12.5.5). Links and projections need none (Table 166), nor does an
    /// annotation whose rectangle has zero width and height.
    /// </summary>
    private static void ReportNoAppearance(PdfAnnotation annotation)
    {
        if (annotation.Kind is PdfAnnotationKind.Link or PdfAnnotationKind.Projection)
        {
            return;
        }

        PdfRectangle rect = annotation.Rect;
        if (rect.Width == 0 && rect.Height == 0)
        {
            return;
        }

        annotation.Document.DiagnosticSink.ReportOnce(
            DiagnosticCodes.AppearanceNotRendered,
            DiagnosticSeverity.Information,
            "The annotation has no normal appearance stream for its appearance state; it is not drawn (appearances are not generated when rendering, §12.5.5).",
            objectReference: annotation.DiagnosticReference);
    }
}
