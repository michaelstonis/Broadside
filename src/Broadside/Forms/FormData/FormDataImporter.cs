using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms.FormData;

/// <summary>Applies what an FDF or XFDF file holds to a document: field values and flags through the typed setters, annotations onto their pages.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.8.3.2: "importing a field causes the values of the entries in the FDF field dictionary to replace those of the
/// corresponding entries in the field with the same fully qualified name in the target document". Each name is matched through
/// <see cref="PdfAcroForm.FindFields(string)"/>, and every field of a name several fields share is set, as viewers do. Values go
/// through <see cref="PdfTextField.SetValue"/>, <see cref="PdfChoiceField.SetValue"/>, <see cref="PdfChoiceField.SetSelectedIndices"/>
/// and <see cref="PdfToggleButtonField.SetValue(CosName)"/>, which write the appearances; an FDF <c>AP</c> is not imported. <c>Ff</c>
/// replaces the field flags, else <c>SetFf</c> then <c>ClrFf</c> change them; <c>F</c>, <c>SetF</c> and <c>ClrF</c> do the same for
/// every widget's annotation flags. A value of the wrong type, or an option or state the field does not have, is skipped with a
/// <see cref="FormDiagnostics.FormDataValueInvalid"/> warning.
/// </para>
/// <para>
/// §12.7.8.3.4: each annotation goes on the page its <c>Page</c> names, with its <c>P</c> set. One whose <c>NM</c> matches an
/// annotation on that page replaces it in place (the same dictionary and object number, so references to it stay valid); others
/// are added. References between the file's annotations (<c>IRT</c>, <c>Popup</c>, <c>Parent</c>) are remapped to the document's
/// copies, and XFDF's <c>inreplyto</c> is resolved by <c>NM</c> once every annotation is in. An annotation without an appearance
/// gets one generated, and the features its entries use are recorded for the save's version (ISO 32000-2 §7.5.2).
/// </para>
/// <para>An import is a mutation: no read of the document may run concurrently with it.</para>
/// </remarks>
internal static class FormDataImporter
{
    private static readonly CosName Page = new("Page");

    public static PdfFormDataImport Import(PdfDocument document, FormDataSet set, bool includeFields, bool includeAnnotations, IReadOnlyList<string>? names, DiagnosticSink diagnostics)
    {
        var fieldsSet = new List<PdfField>();
        var notFound = new List<string>();
        var added = new List<PdfAnnotation>();
        var replaced = new List<PdfAnnotation>();
        if (includeFields && set.Fields.Count > 0)
        {
            ImportFields(document, set, names, diagnostics, fieldsSet, notFound);
        }

        if (includeAnnotations && set.Annotations.Count > 0)
        {
            ImportAnnotations(document, set, diagnostics, added, replaced);
        }

        return new PdfFormDataImport(fieldsSet, notFound, added, replaced, diagnostics.Snapshot());
    }

    private static void ImportFields(PdfDocument document, FormDataSet set, IReadOnlyList<string>? names, DiagnosticSink diagnostics, List<PdfField> fieldsSet, List<string> notFound)
    {
        HashSet<string>? filter = names is null ? null : new HashSet<string>(names, StringComparer.Ordinal);
        PdfAcroForm? form = document.AcroForm;
        if (form?.Xfa is not null)
        {
            diagnostics.Report(
                FormDiagnostics.XfaFormPresent,
                DiagnosticSeverity.Information,
                "The interactive form has an XFA resource; only the AcroForm fields are imported and the XFA datasets keep their values.");
        }

        var seenFields = new HashSet<PdfField>(ReferenceEqualityComparer.Instance);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormDataField entry in set.Fields)
        {
            if (filter is not null && !filter.Contains(entry.Name))
            {
                continue;
            }

            IReadOnlyList<PdfField> matches = form?.FindFields(entry.Name) ?? [];
            if (matches.Count == 0)
            {
                if (seenNames.Add(entry.Name))
                {
                    notFound.Add(entry.Name);
                }

                continue;
            }

            foreach (PdfField field in matches)
            {
                if (Apply(field, entry, diagnostics) && seenFields.Add(field))
                {
                    fieldsSet.Add(field);
                }
            }
        }
    }

    private static bool Apply(PdfField field, FormDataField entry, DiagnosticSink diagnostics)
    {
        bool changed = false;
        if (entry.Value is not null || entry.RichValue is not null)
        {
            changed |= SetValue(field, entry, diagnostics);
        }

        if (entry.FieldFlags is not null || entry.SetFieldFlags is not null || entry.ClearFieldFlags is not null)
        {
            uint current = (uint)field.Flags;
            field.Dictionary[FormNames.Ff] = new CosInteger(entry.FieldFlags ?? ((current | (entry.SetFieldFlags ?? 0)) & ~(entry.ClearFieldFlags ?? 0)));
            changed = true;
        }

        if ((entry.WidgetFlags is not null || entry.SetWidgetFlags is not null || entry.ClearWidgetFlags is not null) && field is PdfTerminalField terminal)
        {
            foreach (PdfWidgetAnnotation widget in terminal.Widgets)
            {
                uint current = (uint)widget.Flags;
                widget.Dictionary[AnnotationNames.F] = new CosInteger(entry.WidgetFlags ?? ((current | (entry.SetWidgetFlags ?? 0)) & ~(entry.ClearWidgetFlags ?? 0)));
            }

            changed = true;
        }

        return changed;
    }

    private static bool SetValue(PdfField field, FormDataField entry, DiagnosticSink diagnostics)
    {
        CosObject? value = entry.Value;
        try
        {
            switch (field)
            {
                case PdfTextField text:
                    if (value is not null)
                    {
                        if (Text(value) is not { } plain)
                        {
                            return Reject(field, diagnostics, "is not a text string");
                        }

                        text.SetValue(plain.Length == 0 ? null : plain);
                    }

                    if (entry.RichValue is { } rich)
                    {
                        // SetValue stores plain text and removes RV; the rich text value is kept beside it (§12.7.4.3.4, Table 228).
                        text.Dictionary[FormNames.RV] = new CosString(TextStringEncoder.Encode(rich));
                    }

                    return true;
                case PdfChoiceField choice:
                    return value is null || SetChoice(choice, value, diagnostics);
                case PdfToggleButtonField toggle:
                    return value is null || SetToggle(toggle, value, diagnostics);
                default:
                    return Reject(field, diagnostics, "is for a field that has no value an import can set (a push button, a signature field or a non-terminal field)");
            }
        }
        catch (ArgumentException exception)
        {
            return Reject(field, diagnostics, $"cannot be set: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return Reject(field, diagnostics, $"cannot be set: {exception.Message}");
        }
    }

    private static bool SetChoice(PdfChoiceField choice, CosObject value, DiagnosticSink diagnostics)
    {
        if (Text(value) is { } single)
        {
            if (single.Length == 0)
            {
                choice.SetSelectedIndices([]);
            }
            else
            {
                choice.SetValue(single);
            }

            return true;
        }

        if (value is not CosArray array)
        {
            return Reject(choice, diagnostics, "is not a text string or an array of them");
        }

        IReadOnlyList<PdfChoiceOption> options = choice.Options;
        var indices = new List<int>();
        foreach (CosObject element in array)
        {
            string? text = Text(element);
            int index = text is null ? -1 : IndexOf(options, text);
            if (index < 0)
            {
                return Reject(choice, diagnostics, $"names {text ?? "a non-text element"}, which is not an option of the field (Opt)");
            }

            indices.Add(index);
        }

        choice.SetSelectedIndices(indices);
        return true;
    }

    private static int IndexOf(IReadOnlyList<PdfChoiceOption> options, string text)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (options[index].ExportValue == text)
            {
                return index;
            }
        }

        for (int index = 0; index < options.Count; index++)
        {
            if (options[index].DisplayText == text)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool SetToggle(PdfToggleButtonField toggle, CosObject value, DiagnosticSink diagnostics)
    {
        CosName? state = value switch
        {
            CosName name => name,
            CosString text => new CosName(text.DecodeText()),
            _ => null,
        };
        if (state is null)
        {
            return Reject(toggle, diagnostics, "is not a name");
        }

        if (FormNames.Off.Equals(state) || toggle.OnStateNames.Contains(state))
        {
            toggle.SetValue(state);
        }
        else
        {
            // A name that is no on state may be an export value of Opt (§12.7.5.2.3, Table 230), as XFDF writes it.
            toggle.SetExportValue(state.Value);
        }

        return true;
    }

    private static string? Text(CosObject value) => value switch
    {
        CosString text => text.DecodeText(),
        CosName name => name.Value,
        _ => null,
    };

    private static bool Reject(PdfField field, DiagnosticSink diagnostics, string problem)
    {
        diagnostics.Report(
            FormDiagnostics.FormDataValueInvalid,
            DiagnosticSeverity.Warning,
            $"The value the file gives the field {field.FullyQualifiedName} {problem}; it is skipped and the field keeps its value.",
            objectReference: field.Reference);
        return false;
    }

    private static void ImportAnnotations(PdfDocument document, FormDataSet set, DiagnosticSink diagnostics, List<PdfAnnotation> added, List<PdfAnnotation> replaced)
    {
        int pageCount = document.Pages.Count;
        var plans = new List<Plan>();
        foreach (FormDataAnnotation annotation in set.Annotations)
        {
            string? subtype = (set.Resolve(annotation.Dictionary.TryGetValue(AnnotationNames.Subtype, out CosObject? entry) ? entry : null) as CosName)?.Value;
            if (subtype is null or "Link" or "Movie" or "Widget" or "PrinterMark" or "Screen" or "TrapNet")
            {
                Skip(diagnostics, $"An annotation of subtype {subtype ?? "(none)"} is not one a forms data file may carry (ISO 32000-2 §12.7.8.3.1, Table 246, Annots); it is skipped.");
                continue;
            }

            if (annotation.Page >= pageCount)
            {
                Skip(diagnostics, $"An annotation is on page {annotation.Page}, but the document has {pageCount} pages; it is skipped.");
                continue;
            }

            PdfPage page = document.Pages[annotation.Page];
            if (page.Reference is null)
            {
                Skip(diagnostics, "An annotation's page is not an indirect object, so the annotation cannot name it in P (ISO 32000-2 §12.5.2, Table 166); it is skipped.");
                continue;
            }

            string? name = set.Resolve(annotation.Dictionary.TryGetValue(AnnotationNames.NM, out CosObject? nm) ? nm : null) is CosString text ? text.DecodeText() : null;
            PdfAnnotation? existing = name is null ? null : page.Annotations.FirstOrDefault(candidate => candidate.Kind != PdfAnnotationKind.Widget && candidate.Name == name);
            if (existing is not null && plans.Any(plan => ReferenceEquals(plan.Existing?.Dictionary, existing.Dictionary)))
            {
                existing = null;
            }

            plans.Add(new Plan(annotation, page, name, existing));
        }

        // Every annotation's target is known before any is filled, so references between them map to the document's copies.
        var copier = new CosGraphCopier(
            set.Resolve,
            document.AddObject,
            reference => diagnostics.Report(
                FormDiagnostics.FormDataInvalid,
                DiagnosticSeverity.Warning,
                "An annotation entry names an object whose body is itself a reference, which no indirect object can be (ISO 32000-2 §7.3.10); the entry is skipped.",
                objectReference: reference));
        foreach (Plan plan in plans)
        {
            if (plan.Existing is { } existing)
            {
                plan.Target = existing.Dictionary;
                plan.TargetReference = existing.Reference;
            }
            else
            {
                plan.Target = new CosDictionary();
                plan.TargetReference = document.AddObject(plan.Target);
            }

            if (plan.Source.Reference is { } source && plan.TargetReference is { } target)
            {
                copier.Assign(source, target);
            }
        }

        foreach (Plan plan in plans)
        {
            CosDictionary target = plan.Target!;
            PdfAnnotation? oldPopup = (plan.Existing as PdfMarkupAnnotation)?.Popup;
            if (plan.Existing is not null)
            {
                foreach (CosName key in target.Keys.ToList())
                {
                    if (!key.Equals(AnnotationNames.P) && !key.Equals(AnnotationNames.StructParent))
                    {
                        target.Remove(key);
                    }
                }
            }

            copier.CopyEntries(plan.Source.Dictionary, target, key => key.Equals(Page) || key.Equals(AnnotationNames.P) || key.Equals(AnnotationNames.StructParent));
            target[AnnotationNames.P] = plan.Page.Reference!;
            if (plan.Existing is null)
            {
                AnnotationEditing.AddToPage(plan.Page, plan.TargetReference!);
            }
            else if (oldPopup is not null && !ReferenceEquals(document.Resolve(target.TryGetValue(AnnotationNames.Popup, out CosObject? popup) ? popup : null), oldPopup.Dictionary)
                && plan.Page.Annotations.Any(candidate => ReferenceEquals(candidate.Dictionary, oldPopup.Dictionary)))
            {
                // The replaced annotation's own pop-up is not the new one's: it goes with what it belonged to.
                plan.Page.RemoveAnnotation(plan.Page.Annotations.First(candidate => ReferenceEquals(candidate.Dictionary, oldPopup.Dictionary)));
            }
        }

        ResolveRepliesByName(document, plans, diagnostics);
        foreach (Plan plan in plans)
        {
            CosDictionary target = plan.Target!;
            PdfAnnotation view = document.AnnotationIndex.GetOrCreate(target, plan.TargetReference, plan.Page);
            if (!target.ContainsKey(AnnotationNames.AP) && AnnotationAppearances.CanGenerate(view.Kind))
            {
                try
                {
                    AnnotationAppearances.Generate(view);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    diagnostics.Report(
                        FormDiagnostics.FormDataInvalid,
                        DiagnosticSeverity.Warning,
                        $"An imported {view.Kind} annotation has no appearance and none can be generated ({exception.Message}); it is imported without one.",
                        objectReference: plan.TargetReference);
                }
            }

            AnnotationEditing.RequireFeatures(document, target, view.Kind);
            (plan.Existing is null ? added : replaced).Add(view);
        }
    }

    /// <summary>Sets the <c>IRT</c> of each annotation the file names its parent of by <c>NM</c>: an imported one first, then one already on the page or any page.</summary>
    private static void ResolveRepliesByName(PdfDocument document, List<Plan> plans, DiagnosticSink diagnostics)
    {
        foreach (Plan plan in plans)
        {
            if (plan.Source.InReplyToName is not { } parentName)
            {
                continue;
            }

            CosReference? parent = plans.FirstOrDefault(candidate => candidate.Name == parentName && !ReferenceEquals(candidate, plan))?.TargetReference
                ?? plan.Page.Annotations.FirstOrDefault(candidate => candidate.Name == parentName)?.Reference
                ?? document.Pages.SelectMany(page => page.Annotations).FirstOrDefault(candidate => candidate.Name == parentName)?.Reference;
            if (parent is null)
            {
                diagnostics.Report(
                    FormDiagnostics.FormDataInvalid,
                    DiagnosticSeverity.Warning,
                    $"An annotation replies to {parentName}, but no annotation of the document has that name (NM); it is imported without IRT.",
                    objectReference: plan.TargetReference);
                continue;
            }

            plan.Target![AnnotationNames.IRT] = parent;
        }
    }

    private static void Skip(DiagnosticSink diagnostics, string message) =>
        diagnostics.Report(FormDiagnostics.FormDataAnnotationSkipped, DiagnosticSeverity.Warning, message);

    private sealed class Plan(FormDataAnnotation source, PdfPage page, string? name, PdfAnnotation? existing)
    {
        public FormDataAnnotation Source { get; } = source;

        public PdfPage Page { get; } = page;

        public string? Name { get; } = name;

        public PdfAnnotation? Existing { get; } = existing;

        public CosDictionary? Target { get; set; }

        public CosReference? TargetReference { get; set; }
    }
}
