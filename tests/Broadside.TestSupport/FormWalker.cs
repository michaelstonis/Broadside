using System.Reflection;
using System.Runtime.ExceptionServices;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// Reads everything the interactive form model exposes (issue #74): every public property of the form, of every field and of its
/// signature lock, every button widget's on state, export value and appearance state, the field each widget of every page resolves
/// to, and every XFA packet. Public API only (reflection over public properties, so a property added later is read too). Exceptions
/// the library throws propagate unwrapped.
/// </summary>
/// <remarks>ISO 32000-2 §12.7 and Annex K.</remarks>
public static class FormWalker
{
    /// <summary>Walks the interactive form of <paramref name="document"/>.</summary>
    /// <param name="document">An open document.</param>
    /// <returns>The number of fields read.</returns>
    public static int Walk(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.AcroForm is not { } form)
        {
            return 0;
        }

        ReadProperties(form);
        int count = 0;
        foreach (PdfField field in form.AllFields)
        {
            count++;
            ReadProperties(field);
            _ = form.FindFields(field.FullyQualifiedName).Count;
            switch (field)
            {
                case PdfToggleButtonField toggle:
                    foreach (PdfWidgetAnnotation widget in toggle.Widgets)
                    {
                        _ = (toggle.GetOnState(widget), toggle.GetWidgetExportValue(widget), toggle.GetAppearanceState(widget));
                    }

                    break;
                case PdfSignatureField { Lock: { } fieldLock }:
                    ReadProperties(fieldLock);
                    break;
            }
        }

        foreach (PdfXfaPacket packet in form.Xfa?.Packets ?? [])
        {
            ReadProperties(packet);
        }

        foreach (PdfPage page in document.Pages)
        {
            foreach (PdfWidgetAnnotation widget in page.Annotations.OfType<PdfWidgetAnnotation>())
            {
                _ = widget.Field?.FullyQualifiedName;
            }
        }

        return count;
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
