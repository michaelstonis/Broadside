using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>
/// A snapshot of an interactive form's field hierarchy: the field views, their names, and which field each widget dictionary belongs
/// to. Built once from the <c>Fields</c> array (or, for a widget no field lists, from the top of its <c>Parent</c> chain) and rebuilt
/// when a dictionary or array it read has changed.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.2 and §12.7.4. The walk is iterative and visits each dictionary once: a dictionary met again (a <c>Kids</c> cycle
/// or a field under two parents) is skipped with <c>FieldTreeCycle</c>, and nesting beyond <see cref="MaxDepth"/> is cut with
/// <c>FieldTreeTooDeep</c>. A dictionary with <c>T</c> is a field; one without is a widget, or, when it has <c>Kids</c>, an unnamed
/// level whose kids belong to the field above it (§12.7.4.2). Nothing is written to the COS objects.
/// </remarks>
internal sealed class FieldTree
{
    /// <summary>The deepest nesting of field dictionaries read; deeper ones are cut with a diagnostic.</summary>
    public const int MaxDepth = 256;

    private readonly ContainerStamps _tracked = new();
    private readonly CosObject? _fieldsEntry;
    private readonly Dictionary<CosDictionary, FieldTree> _orphans = new(ReferenceEqualityComparer.Instance);

    private FieldTree(CosObject? fieldsEntry) => _fieldsEntry = fieldsEntry;

    /// <summary>Gets the root fields, in order.</summary>
    public List<PdfField> Roots { get; } = [];

    /// <summary>Gets every field, depth first in <c>Kids</c> order.</summary>
    public List<PdfField> All { get; } = [];

    /// <summary>Gets the fields by fully qualified name (several when names repeat).</summary>
    public Dictionary<string, List<PdfField>> ByName { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the field each field dictionary is.</summary>
    public Dictionary<CosDictionary, PdfField> ByDictionary { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the terminal field each widget dictionary belongs to (a merged dictionary maps to itself).</summary>
    public Dictionary<CosDictionary, PdfTerminalField> ByWidget { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    public static CosObject? Get(PdfDocument document, CosDictionary dictionary, CosName key) => EntryReader.Get(document, dictionary, key);

    /// <summary>Builds the tree of <paramref name="form"/> from its <c>Fields</c> array.</summary>
    public static FieldTree Build(PdfAcroForm form)
    {
        PdfDocument document = form.Document;
        form.Dictionary.TryGetValue(FormNames.Fields, out CosObject? entry);
        var tree = new FieldTree(entry);
        var builder = new Builder(form, tree);
        switch (document.Resolve(entry))
        {
            case CosArray fields:
                tree.Track(fields);
                builder.AddRoots(fields);
                break;
            case CosNull when form.Dictionary.Count == 0:
                // An empty interactive form dictionary is how many writers say "no form"; it is read as a form without fields.
                break;
            case CosNull:
                form.Report(DiagnosticCodes.AcroFormFieldsMissing, "The interactive form dictionary has no Fields array, which Table 224 requires; the form has no fields.");
                break;
            default:
                form.Report(DiagnosticCodes.AcroFormFieldsMissing, "The interactive form dictionary's Fields entry shall be an array; the form has no fields.");
                break;
        }

        builder.Run();
        builder.CheckDuplicates();
        return tree;
    }

    /// <summary>Whether nothing the tree read has changed since it was built.</summary>
    public bool IsCurrent(CosDictionary acroForm)
    {
        acroForm.TryGetValue(FormNames.Fields, out CosObject? entry);
        return ReferenceEquals(entry, _fieldsEntry) && _tracked.IsCurrent;
    }

    /// <summary>
    /// Finds the field a widget belongs to when the tree does not list it: its own dictionary when it has <c>T</c>, else the first
    /// dictionary with <c>T</c> up its <c>Parent</c> chain; a field outside the tree is read from the top of that chain.
    /// </summary>
    public PdfTerminalField? FindOutside(PdfAcroForm form, CosDictionary widget, CosReference? widgetReference)
    {
        PdfDocument document = form.Document;
        CosDictionary? candidate = widget;
        CosReference? reference = widgetReference;
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        while (candidate is not null && !candidate.ContainsKey(FormNames.T))
        {
            if (!seen.Add(candidate) || seen.Count > MaxDepth)
            {
                return null;
            }

            reference = candidate.TryGetValue(FormNames.Parent, out CosObject? up) ? up as CosReference : null;
            candidate = Get(document, candidate, FormNames.Parent) as CosDictionary;
        }

        if (candidate is null)
        {
            return null;
        }

        if (ByDictionary.TryGetValue(candidate, out PdfField? listed))
        {
            return listed as PdfTerminalField;
        }

        CosDictionary top = candidate;
        CosReference? candidateReference = reference;
        seen.Clear();
        while (Get(document, top, FormNames.Parent) is CosDictionary parent && seen.Add(top) && seen.Count <= MaxDepth)
        {
            reference = top[FormNames.Parent] as CosReference;
            top = parent;
        }

        if (!top.ContainsKey(FormNames.T))
        {
            // An unnamed dictionary at the top: read from the named field below it.
            (top, reference) = (candidate, candidateReference);
        }


        lock (_orphans)
        {
            if (!_orphans.TryGetValue(top, out FieldTree? orphan) || !orphan._tracked.IsCurrent)
            {
                orphan = new FieldTree(fieldsEntry: null);
                var builder = new Builder(form, orphan);
                builder.AddOrphanRoot(top, reference);
                builder.Run();
                _orphans[top] = orphan;
            }

            return orphan.ByWidget.GetValueOrDefault(widget) ?? orphan.ByDictionary.GetValueOrDefault(candidate) as PdfTerminalField;
        }
    }

    private void Track(CosObject container) => _tracked.Add(container);

    /// <summary>A dictionary waiting to be read as a field: how it was reached, under which field, through which unnamed levels.</summary>
    private readonly record struct Pending(CosObject Element, CosDictionary Dictionary, PdfNonTerminalField? Parent, CosDictionary[] Intermediates, int Depth);

    /// <summary>A kid of a field after unnamed levels are expanded.</summary>
    private readonly record struct Kid(CosObject Element, CosDictionary Dictionary, CosDictionary[] Intermediates);

    private sealed class Builder(PdfAcroForm form, FieldTree tree)
    {
        private readonly PdfDocument _document = form.Document;
        private readonly HashSet<CosDictionary> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<Pending> _pending = new();
        private readonly List<Pending> _roots = [];

        public void AddRoots(CosArray fields)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                CosObject element = fields[index];
                if (_document.Resolve(element) is not CosDictionary dictionary)
                {
                    form.Report(DiagnosticCodes.FieldReferenceInvalid, $"Element {index} of the interactive form's Fields array is not a field dictionary; it is skipped.");
                    continue;
                }

                if (element is not CosReference)
                {
                    form.Report(DiagnosticCodes.FieldNotIndirect, $"Element {index} of the interactive form's Fields array is a direct dictionary; field dictionaries shall be indirect objects (§12.7.4.1). It is read anyway.");
                }

                if (!_visited.Add(dictionary))
                {
                    Report(DiagnosticCodes.FieldTreeCycle, "The field dictionary appears more than once in the field tree; only its first occurrence is read.", element, null);
                    continue;
                }

                if (Get(_document, dictionary, FormNames.Parent) is not null)
                {
                    Report(DiagnosticCodes.FieldNotRoot, "A field in the interactive form's Fields array has a Parent entry, but Fields shall hold only root fields (Table 224); it is read as a root.", element, null);
                }

                if (dictionary.ContainsKey(FormNames.T))
                {
                    _roots.Add(new Pending(element, dictionary, null, [], 1));
                }
                else if (Get(_document, dictionary, FormNames.Kids) is not null)
                {
                    Report(DiagnosticCodes.FieldNameMissing, "A dictionary in the field tree has kids but no partial name (T); its kids are read as kids of the field above it.", element, null);
                    tree.Track(dictionary);
                    var fieldKids = new List<Kid>();
                    var widgetKids = new List<Kid>();
                    Expand(dictionary, element as CosReference ?? form.Reference, [dictionary], 1, fieldKids, widgetKids);
                    foreach (Kid kid in fieldKids)
                    {
                        _roots.Add(new Pending(kid.Element, kid.Dictionary, null, kid.Intermediates, 2));
                    }

                    if (widgetKids.Count > 0)
                    {
                        Report(DiagnosticCodes.FieldNameMissing, "Widget annotations under an unnamed root dictionary belong to no field; they are ignored.", element, null);
                    }
                }
                else
                {
                    Report(DiagnosticCodes.FieldNameMissing, "A dictionary in the interactive form's Fields array has no partial name (T), so it is a widget annotation, not a field (§12.7.4.2); it is skipped.", element, null);
                }
            }
        }

        public void AddOrphanRoot(CosDictionary top, CosReference? reference)
        {
            _visited.Add(top);
            if (top.ContainsKey(FormNames.T))
            {
                _roots.Add(new Pending((CosObject?)reference ?? top, top, null, [], 1));
            }
        }

        public void Run()
        {
            for (int index = _roots.Count - 1; index >= 0; index--)
            {
                _pending.Push(_roots[index]);
            }

            while (_pending.TryPop(out Pending item))
            {
                Read(item);
            }
        }

        public void CheckDuplicates()
        {
            foreach ((string name, List<PdfField> fields) in tree.ByName)
            {
                PdfField first = fields[0];
                foreach (PdfField other in fields.Skip(1))
                {
                    if (!Same(first.FieldTypeName, other.FieldTypeName) || !Same(first.ValueObject, other.ValueObject) || !Same(first.DefaultValueObject, other.DefaultValueObject))
                    {
                        other.Report(DiagnosticCodes.FieldNameDuplicateInconsistent, $"Fields named {name} shall have the same FT, V and DV (§12.7.4.2); they differ.");
                        break;
                    }
                }
            }
        }

        private static bool Same(CosObject? left, CosObject? right) => (left, right) switch
        {
            (null, null) => true,
            (CosArray a, CosArray b) => ReferenceEquals(a, b) || (a.Count == b.Count && a.Zip(b).All(pair => Equals(pair.First, pair.Second))),
            (CosNumber a, CosNumber b) => a.ToDouble() == b.ToDouble(),
            _ => Equals(left, right),
        };

        private void Read(Pending item)
        {
            CosDictionary dictionary = item.Dictionary;
            tree.Track(dictionary);
            if (item.Depth > MaxDepth)
            {
                Report(DiagnosticCodes.FieldTreeTooDeep, $"The field tree is nested more than {MaxDepth} levels deep; deeper fields are not read.", item.Element, item.Parent);
                return;
            }

            string partialName = ReadPartialName(item);
            string fullName = item.Parent is null ? partialName : item.Parent.FullyQualifiedName + "." + partialName;
            var info = new FieldInfo(form, dictionary, item.Element as CosReference, item.Parent, partialName, fullName, item.Intermediates);

            var fieldKids = new List<Kid>();
            var widgetKids = new List<Kid>();
            bool hasKids = Expand(dictionary, item.Element as CosReference ?? item.Parent?.DiagnosticReference ?? form.Reference, [], item.Depth, fieldKids, widgetKids);
            PdfField field;
            if (fieldKids.Count > 0)
            {
                var container = new PdfNonTerminalField(info);
                if (widgetKids.Count > 0)
                {
                    container.Report(DiagnosticCodes.FieldKidsMixed, "The field's Kids array holds both fields and widget annotations (§12.7.2); the widgets are ignored.");
                }

                for (int index = fieldKids.Count - 1; index >= 0; index--)
                {
                    Kid kid = fieldKids[index];
                    _pending.Push(new Pending(kid.Element, kid.Dictionary, container, kid.Intermediates, item.Depth + 1 + kid.Intermediates.Length));
                }

                field = container;
            }
            else
            {
                CosObject[] widgets = hasKids
                    ? [.. widgetKids.Select(kid => kid.Element)]
                    : Annotations.PdfWidgetAnnotation.IsWidget(_document, dictionary) ? [item.Element] : [];
                PdfTerminalField terminal = Create(info, widgets);
                if (hasKids)
                {
                    foreach (Kid kid in widgetKids)
                    {
                        tree.ByWidget.TryAdd(kid.Dictionary, terminal);
                    }
                }
                else if (widgets.Length > 0)
                {
                    tree.ByWidget.TryAdd(dictionary, terminal);
                }

                if (terminal is PdfSignatureField && widgets.Length > 1)
                {
                    terminal.Report(DiagnosticCodes.SignatureFieldMultipleWidgets, "A signature field shall never refer to more than one annotation (§12.7.5.5); all its widgets are kept.");
                }

                field = terminal;
            }

            if (item.Parent is { } parent)
            {
                parent.AddChild(field);
            }
            else
            {
                tree.Roots.Add(field);
            }

            tree.All.Add(field);
            tree.ByDictionary.TryAdd(dictionary, field);
            if (!tree.ByName.TryGetValue(fullName, out List<PdfField>? named))
            {
                tree.ByName[fullName] = named = [];
            }

            named.Add(field);
        }

        /// <summary>Reads the kids of <paramref name="container"/>, expanding unnamed levels; returns whether it has a Kids entry.</summary>
        private bool Expand(CosDictionary container, CosReference? containerReference, CosDictionary[] intermediates, int depth, List<Kid> fieldKids, List<Kid> widgetKids)
        {
            CosObject? value = Get(_document, container, FormNames.Kids);
            if (value is null)
            {
                return false;
            }

            if (value is not CosArray kids)
            {
                ReportOn(containerReference, DiagnosticCodes.FieldEntryInvalid, "The field's Kids entry shall be an array; it is ignored.");
                return false;
            }

            tree.Track(kids);
            for (int index = 0; index < kids.Count; index++)
            {
                CosObject element = kids[index];
                if (_document.Resolve(element) is not CosDictionary kid)
                {
                    ReportOn(containerReference, DiagnosticCodes.FieldReferenceInvalid, $"Element {index} of the field's Kids array is not a dictionary; it is skipped.");
                    continue;
                }

                if (element is not CosReference)
                {
                    ReportOn(containerReference, DiagnosticCodes.FieldNotIndirect, $"Element {index} of the field's Kids array is a direct dictionary; Table 226 requires indirect references. It is read anyway.");
                }

                if (!_visited.Add(kid))
                {
                    ReportOn(containerReference, DiagnosticCodes.FieldTreeCycle, $"Element {index} of the field's Kids array appears elsewhere in the field tree too (a cycle, or a kid of two parents); only its first occurrence is read.");
                    continue;
                }

                switch (Get(_document, kid, FormNames.Parent))
                {
                    case null:
                        ReportAt(element, containerReference, DiagnosticCodes.FieldParentMissing, "A kid in the field tree has no Parent entry, which Table 226 requires; the dictionary whose Kids holds it is used.");
                        break;
                    case var parent when !ReferenceEquals(parent, container):
                        ReportAt(element, containerReference, DiagnosticCodes.FieldParentMismatch, "A kid's Parent entry does not name the dictionary whose Kids array holds it; the tree is followed.");
                        break;
                }

                if (kid.ContainsKey(FormNames.T))
                {
                    fieldKids.Add(new Kid(element, kid, intermediates));
                }
                else if (Get(_document, kid, FormNames.Kids) is not null)
                {
                    ReportAt(element, containerReference, DiagnosticCodes.FieldNameMissing, "A dictionary in the field tree has kids but no partial name (T); its kids are read as kids of the field above it.");
                    tree.Track(kid);
                    if (depth + intermediates.Length + 1 > MaxDepth)
                    {
                        ReportAt(element, containerReference, DiagnosticCodes.FieldTreeTooDeep, $"The field tree is nested more than {MaxDepth} levels deep; deeper fields are not read.");
                        continue;
                    }

                    Expand(kid, element as CosReference ?? containerReference, [.. intermediates, kid], depth, fieldKids, widgetKids);
                }
                else
                {
                    widgetKids.Add(new Kid(element, kid, intermediates));
                }
            }

            return true;
        }

        private string ReadPartialName(Pending item)
        {
            switch (Get(_document, item.Dictionary, FormNames.T))
            {
                case CosString text:
                    string name = text.DecodeText();
                    if (name.Contains('.', StringComparison.Ordinal))
                    {
                        Report(DiagnosticCodes.FieldNameHasPeriod, "A partial field name shall not contain a period (§12.7.4.2); it is kept, so looking the field up by name may be ambiguous.", item.Element, item.Parent);
                    }

                    return name;
                case CosName nameObject:
                    Report(DiagnosticCodes.FieldEntryInvalid, "The field's T entry shall be a text string; it is a name, read as its text.", item.Element, item.Parent);
                    return nameObject.Value;

                default:
                    Report(DiagnosticCodes.FieldEntryInvalid, "The field's T entry shall be a text string; the field's partial name reads as empty.", item.Element, item.Parent);
                    return string.Empty;
            }
        }

        private PdfTerminalField Create(FieldInfo info, CosObject[] widgets)
        {
            CosName? type = Inherited(info, FormNames.FT) as CosName;
            CosObject? flagsValue = Inherited(info, FormNames.Ff);
            uint flags = flagsValue is CosNumber number && double.IsFinite(number.ToDouble()) ? (uint)(long)number.ToDouble() : 0;
            switch (type?.Value)
            {
                case "Btn":
                    bool push = (flags & (1u << 16)) != 0;
                    bool radio = (flags & (1u << 15)) != 0;
                    PdfTerminalField button = push
                        ? new PdfPushButtonField(info, widgets)
                        : radio ? new PdfRadioButtonField(info, widgets) : new PdfCheckBoxField(info, widgets);
                    if (push && radio)
                    {
                        button.Report(DiagnosticCodes.ButtonFlagsConflict, "The button field sets both Pushbutton and Radio; Radio may be set only if Pushbutton is clear (Table 229). It is read as a push button.");
                    }

                    return button;
                case "Tx":
                    return new PdfTextField(info, widgets);
                case "Ch":
                    return (flags & (1u << 17)) != 0 ? new PdfComboBoxField(info, widgets) : new PdfListBoxField(info, widgets);
                case "Sig":
                    return new PdfSignatureField(info, widgets);
                default:
                    var unknown = new PdfUnknownField(info, widgets);
                    unknown.Report(
                        DiagnosticCodes.FieldTypeMissing,
                        type is null
                            ? "The terminal field has no FT entry, of its own or inherited, which Table 226 requires; its type is unknown."
                            : $"The terminal field's type {type.Value} is not one Table 226 defines; its type is unknown.");
                    return unknown;
            }
        }

        private CosObject? Inherited(FieldInfo info, CosName key)
        {
            if (Get(_document, info.Dictionary, key) is { } own)
            {
                return own;
            }

            foreach (CosDictionary intermediate in info.Intermediates)
            {
                if (Get(_document, intermediate, key) is { } carried)
                {
                    return carried;
                }
            }

            return info.Parent?.Inherited(key);
        }

        private void Report(string code, string message, CosObject element, PdfField? parent) =>
            _document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: element as CosReference ?? parent?.DiagnosticReference ?? form.Reference);

        private void ReportAt(CosObject element, CosReference? containerReference, string code, string message) =>
            _document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: element as CosReference ?? containerReference);

        private void ReportOn(CosReference? containerReference, string code, string message) =>
            _document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: containerReference);
    }
}
