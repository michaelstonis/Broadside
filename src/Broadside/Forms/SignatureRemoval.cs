using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Signatures;

namespace Broadside.Forms;

/// <summary>
/// Clearing signed signature fields and removing signature fields (issue #660): the field's value, its widgets' appearances, the
/// catalog's <c>Perms</c> <c>DocMDP</c> entry that names the signature, and the form's <c>SigFlags</c>.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.5 (Tables 235 and 236), §12.7.3 (Table 225), §12.8.1 (Table 255) and §12.8.2.2 (Table 263). The core methods
/// are not atomic on their own: the public entry points run them inside <see cref="AnnotationEditing.Atomically"/>.
/// </remarks>
internal static class SignatureRemoval
{
    /// <summary>What clearing did: how many fields lost a signature value, and whether a certification went with it.</summary>
    internal readonly record struct Cleared(int Fields, bool Certification);

    /// <summary>
    /// Clears <paramref name="field"/> and every other signature field whose value is the same signature dictionary (a malformed
    /// file), then recomputes the form's signature flags. An unsigned field is left as it is.
    /// </summary>
    public static Cleared Clear(PdfSignatureField field) =>
        AnnotationEditing.Atomically(() =>
        {
            Cleared cleared = ClearCore(field);
            if (cleared.Fields > 0)
            {
                UpdateFlags(field.Form);
            }

            return cleared;
        });

    /// <summary>Clears <paramref name="field"/> if it is signed and removes it with its widgets, then recomputes the form's signature flags.</summary>
    public static Cleared Remove(PdfSignatureField field) =>
        AnnotationEditing.Atomically(() =>
        {
            Cleared cleared = ClearCore(field);
            RemoveCore(field);
            UpdateFlags(field.Form);
            return cleared;
        });

    /// <summary>
    /// Removes the value (<c>V</c>, wherever up the field's inheritance chain it is stored) of <paramref name="field"/> and of every
    /// signature field holding the same signature dictionary, gives their widgets an empty appearance, and removes the catalog's
    /// <c>Perms</c> <c>DocMDP</c> entry when it names that dictionary. Not atomic.
    /// </summary>
    public static Cleared ClearCore(PdfSignatureField field)
    {
        if (field.Holder(FormNames.V) is null)
        {
            return default;
        }

        PdfDocument document = field.Document;

        // A value that is not a dictionary (damaged) is cleared from this field only; a dictionary shared by several fields from each.
        CosObject? value = field.ValueObject;
        List<PdfSignatureField> fields = [field];
        if (value is CosDictionary)
        {
            fields.AddRange(field.Form.TerminalFields.OfType<PdfSignatureField>()
                .Where(other => !ReferenceEquals(other.Dictionary, field.Dictionary) && ReferenceEquals(other.ValueObject, value)));
        }

        foreach (PdfSignatureField cleared in fields)
        {
            while (cleared.Holder(FormNames.V) is { } holder)
            {
                holder.Remove(FormNames.V);
            }

            ResetAppearances(cleared);
        }

        bool certification = value is CosDictionary signature && RemoveCertification(document, signature);
        return new Cleared(fields.Count, certification);
    }

    /// <summary>
    /// Removes <paramref name="field"/> from the field tree (the form's <c>Fields</c> and <c>CO</c>, or the <c>Kids</c> it is listed
    /// in) and its widgets from their pages and the structure tree. Not atomic; the flags are not recomputed.
    /// </summary>
    public static void RemoveCore(PdfSignatureField field)
    {
        PdfDocument document = field.Document;
        AnnotationEditing.RemoveWidgets(document, field.Widgets);
        RemoveFrom(document, field.Form.Dictionary, FormNames.Fields, field.Dictionary);
        RemoveFrom(document, field.Form.Dictionary, FormNames.CO, field.Dictionary);
        foreach (CosDictionary container in field.Containers)
        {
            RemoveFrom(document, container, FormNames.Kids, field.Dictionary);
        }
    }

    /// <summary>
    /// Recomputes the form's <c>SigFlags</c> (Table 225): AppendOnly is cleared when no signed signature field remains, and
    /// SignaturesExist when no signature field does. Other bits are kept; the entry goes when nothing is left. A value that is not
    /// an integer is left as it is (it reads as 0 with a diagnostic).
    /// </summary>
    public static void UpdateFlags(PdfAcroForm form)
    {
        PdfDocument document = form.Document;
        if (!form.Dictionary.TryGetValue(FormNames.SigFlags, out CosObject? entry) || document.Resolve(entry) is not CosInteger current)
        {
            return;
        }

        PdfSignatureField[] fields = [.. form.TerminalFields.OfType<PdfSignatureField>()];
        long flags = current.Value;
        if (!fields.Any(static field => field.Holder(FormNames.V) is not null))
        {
            flags &= ~(long)PdfSignatureFlags.AppendOnly;
        }

        if (fields.Length == 0)
        {
            flags &= ~(long)PdfSignatureFlags.SignaturesExist;
        }

        if (flags == current.Value)
        {
            return;
        }

        if (flags == 0)
        {
            form.Dictionary.Remove(FormNames.SigFlags);
        }
        else
        {
            form.Dictionary[FormNames.SigFlags] = new CosInteger(flags);
        }
    }

    /// <summary>Gives every widget of <paramref name="field"/> the empty appearance a field <see cref="PdfAcroForm.AddSignatureField"/> creates has.</summary>
    private static void ResetAppearances(PdfSignatureField field)
    {
        PdfDocument document = field.Document;
        foreach (PdfWidgetAnnotation widget in field.Widgets)
        {
            CosStream stream = TextFieldAppearance.BuildStream(document, widget, defaultResources: null, fonts: [], static (_, _, _) => { });
            widget.Dictionary[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = document.AddObject(stream) };
            widget.Dictionary.Remove(AnnotationNames.AS);
        }
    }

    /// <summary>
    /// Removes the catalog's <c>Perms</c> <c>DocMDP</c> entry when it names <paramref name="signature"/> (Table 263), and
    /// <c>Perms</c> when nothing else is left in it. A <c>Perms</c> that is not a dictionary is left as it is.
    /// </summary>
    private static bool RemoveCertification(PdfDocument document, CosDictionary signature)
    {
        if (!document.Catalog.TryGetValue(TransformMethods.Perms, out CosObject? value) || document.Resolve(value) is not CosDictionary permissions
            || !permissions.TryGetValue(TransformMethods.DocMdp, out CosObject? docMdp) || !ReferenceEquals(document.Resolve(docMdp), signature))
        {
            return false;
        }

        permissions.Remove(TransformMethods.DocMdp);
        if (permissions.Count == 0)
        {
            document.Catalog.Remove(TransformMethods.Perms);
        }

        return true;
    }

    private static void RemoveFrom(PdfDocument document, CosDictionary owner, CosName key, CosDictionary field)
    {
        if (!owner.TryGetValue(key, out CosObject? value) || document.Resolve(value) is not CosArray array)
        {
            return;
        }

        for (int index = array.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(document.Resolve(array[index]), field))
            {
                array.RemoveAt(index);
            }
        }
    }
}
