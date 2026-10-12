using System.Reflection;
using System.Runtime.ExceptionServices;
using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// Reads everything the annotation model exposes (issue #71): every public property of every annotation of every page, of its
/// appearance dictionary, entries, appearance characteristics and fixed print dictionary, every appearance in every mode with its
/// placement matrix, and every appearance state. Public API only (reflection over public properties, so a property added later is
/// read too). Exceptions the library throws propagate unwrapped.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.</remarks>
public static class AnnotationWalker
{
    private static readonly PdfAppearanceMode[] Modes = [PdfAppearanceMode.Normal, PdfAppearanceMode.Rollover, PdfAppearanceMode.Down];

    /// <summary>Walks the annotations of every page of <paramref name="document"/>.</summary>
    /// <param name="document">An open document.</param>
    /// <returns>The number of annotations read (an annotation listed twice counts twice).</returns>
    public static int Walk(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int count = 0;
        foreach (PdfPage page in document.Pages)
        {
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                count++;
                ReadProperties(annotation);
                if (annotation.AppearanceDictionary is { } appearance)
                {
                    ReadProperties(appearance);
                    foreach (PdfAppearanceMode mode in Modes)
                    {
                        if (appearance.GetEntry(mode) is { } entry)
                        {
                            ReadProperties(entry);
                            foreach (CosName state in entry.StateNames)
                            {
                                ReadForm(annotation, entry.GetAppearance(state));
                            }
                        }

                        ReadForm(annotation, annotation.GetAppearance(mode));
                    }
                }

                switch (annotation)
                {
                    case PdfWidgetAnnotation { AppearanceCharacteristics: { } characteristics }:
                        ReadProperties(characteristics);
                        break;
                    case PdfScreenAnnotation { AppearanceCharacteristics: { } characteristics }:
                        ReadProperties(characteristics);
                        break;
                    case PdfWatermarkAnnotation { FixedPrint: { } fixedPrint }:
                        ReadProperties(fixedPrint);
                        break;
                }
            }
        }

        return count;
    }

    private static void ReadForm(PdfAnnotation annotation, PdfFormXObject? form)
    {
        if (form is not null)
        {
            ReadProperties(form);
            _ = annotation.GetAppearanceMatrix(form);
            _ = form.Decode().Length;
        }
    }

    private static void ReadProperties(object view)
    {
        foreach (PropertyInfo property in view.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                object? value = property.GetValue(view);
                if (value is System.Collections.IEnumerable sequence and not string and not CosObject)
                {
                    foreach (object? item in sequence)
                    {
                        _ = item;
                    }
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
