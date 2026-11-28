using System.Globalization;
using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Sanitizing;

/// <summary>
/// Runs <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/> (issue #648): the catalog's scripts, files and metadata first,
/// then one walk over every object reachable from the catalog for actions, XFA, associated and embedded files, metadata streams and
/// page-piece dictionaries, then the pages' annotations and thumbnails, then the content pass, and last the optional content
/// properties. Each removal is counted under its kind, in the order the kinds are first met.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.6 (actions), §7.7.4 (name dictionary), §12.7.3 (XFA), §7.11.4 and §14.13 (embedded and associated files), §14.3
/// and §14.5 (metadata, page-piece dictionaries), §12.3.4 (thumbnails), §8.11 (optional content), §9.3.6 (rendering mode 3). The
/// actions are pruned by <see cref="ActionPruner"/>, which <see cref="Writing.PdfAConverter"/> shares; removals go through the
/// owners the live views use (<see cref="PdfDocument.RemoveEmbeddedFile"/>, <see cref="PdfPage.RemoveAnnotation"/>).
/// </remarks>
internal sealed class DocumentSanitizer
{
    private static readonly CosName A = new("A");
    private static readonly CosName AA = new("AA");
    private static readonly CosName AF = new("AF");
    private static readonly CosName AP = new("AP");
    private static readonly CosName Collection = new("Collection");
    private static readonly CosName Configs = new("Configs");
    private static readonly CosName D = new("D");
    private static readonly CosName Dest = new("Dest");
    private static readonly CosName EF = new("EF");
    private static readonly CosName EmbeddedFiles = new("EmbeddedFiles");
    private static readonly CosName F = new("F");
    private static readonly CosName Info = new("Info");
    private static readonly CosName JavaScriptKey = new("JavaScript");
    private static readonly CosName JS = new("JS");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName Locked = new("Locked");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName Names = new("Names");
    private static readonly CosName NeedsRendering = new("NeedsRendering");
    private static readonly CosName OC = new("OC");
    private static readonly CosName OCGs = new("OCGs");
    private static readonly CosName OCProperties = new("OCProperties");
    private static readonly CosName Off = new("OFF");
    private static readonly CosName On = new("ON");
    private static readonly CosName OpenAction = new("OpenAction");
    private static readonly CosName Order = new("Order");
    private static readonly CosName PA = new("PA");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName PieceInfo = new("PieceInfo");
    private static readonly CosName Properties = new("Properties");
    private static readonly CosName RBGroups = new("RBGroups");
    private static readonly CosName Rect = new("Rect");
    private static readonly CosName RF = new("RF");
    private static readonly CosName S = new("S");
    private static readonly CosName AS = new("AS");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Thumb = new("Thumb");
    private static readonly CosName Title = new("Title");
    private static readonly CosName TypeKey = new("Type");
    private static readonly CosName XFA = new("XFA");
    private static readonly CosName XObject = new("XObject");

    private readonly PdfDocument _document;
    private readonly PdfSanitizeOptions _options;
    private readonly List<string> _order = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly HashSet<CosDictionary> _lostAction = new(ReferenceEqualityComparer.Instance);
    private readonly ActionPruner _pruner;

    private DocumentSanitizer(PdfDocument document, PdfSanitizeOptions options)
    {
        _document = document;
        _options = options;
        _pruner = new ActionPruner(document, Keep, action =>
        {
            if (action is null)
            {
                Count("damaged actions (not a dictionary)");
            }
        }, splice: true);
    }

    private PdfSanitizeActions Actions => _options.Actions & PdfSanitizeActions.All;

    /// <summary>Sanitizes <paramref name="document"/> in place.</summary>
    /// <returns>What was removed.</returns>
    public static PdfSanitization Run(PdfDocument document, PdfSanitizeOptions options)
    {
        var sanitizer = new DocumentSanitizer(document, options);
        bool signed = document.AcroForm is { } form && form.TerminalFields.OfType<Forms.PdfSignatureField>().Any(static field => field.IsSigned);
        bool pdfA3 = document.Metadata?.Packet?.PdfAPart == 3;

        sanitizer.SanitizeCatalog();
        sanitizer.SweepObjects();
        sanitizer.SanitizePages();
        sanitizer.SanitizeContent();
        sanitizer.SanitizeOptionalContentProperties();

        var removed = new List<string>(sanitizer._order.Count + 2);
        removed.AddRange(sanitizer._order.Select(kind => string.Create(CultureInfo.InvariantCulture, $"{kind}: {sanitizer._counts[kind]}")));
        if (removed.Count > 0 && signed)
        {
            removed.Add("signatures invalidated");
        }

        if (pdfA3 && sanitizer._filesRemoved)
        {
            removed.Add("the PDF/A-3 claim no longer holds: its embedded and associated files are removed");
        }

        return new PdfSanitization(removed, options.RemoveMetadata);
    }

    private bool _filesRemoved;

    private void Count(string kind, int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        if (_counts.TryGetValue(kind, out int existing))
        {
            _counts[kind] = existing + count;
        }
        else
        {
            _order.Add(kind);
            _counts[kind] = count;
        }
    }

    private CosObject Resolve(CosObject? value) => _document.Resolve(value);

    private CosDictionary? Dictionary(CosDictionary owner, CosName key) => Resolve(owner.GetValueOrDefault(key)) switch
    {
        CosDictionary dictionary => dictionary,
        CosStream stream => stream.Dictionary,
        _ => null,
    };

    private string? NameOf(CosDictionary owner, CosName key) => (Resolve(owner.GetValueOrDefault(key)) as CosName)?.Value;

    /// <summary>Whether an action stays (ISO 32000-2 §12.6.4, Table 201); a removed one is counted under its type.</summary>
    private bool Keep(CosDictionary action)
    {
        if (NameOf(action, S) is not { } type)
        {
            Count("damaged actions (no S)");
            return false;
        }

        PdfSanitizeActions? kind = type switch
        {
            "JavaScript" => PdfSanitizeActions.JavaScript,
            "Launch" => PdfSanitizeActions.Launch,
            "URI" => PdfSanitizeActions.Uri,
            "SubmitForm" => PdfSanitizeActions.SubmitForm,
            "ImportData" => PdfSanitizeActions.ImportData,
            "GoToR" or "GoToE" => PdfSanitizeActions.RemoteGoTo,
            "Movie" or "Sound" or "Rendition" or "RichMediaExecute" or "GoTo3DView" => PdfSanitizeActions.Multimedia,
            "GoTo" or "GoToDp" or "Thread" or "Hide" or "Named" or "ResetForm" or "SetOCGState" or "Trans" => PdfSanitizeActions.None,
            _ => null,
        };
        if (kind is null)
        {
            Count("unrecognized actions");
            return false;
        }

        if ((Actions & kind.Value) != 0)
        {
            Count(string.Create(CultureInfo.InvariantCulture, $"{type} actions"));
            return false;
        }

        if (type == "Rendition" && (Actions & PdfSanitizeActions.JavaScript) != 0 && action.Remove(JS))
        {
            // §12.6.4.14, Table 216: a rendition action's JS runs in place of its other entries.
            Count("Rendition action scripts (JS)");
        }

        return true;
    }

    /// <summary>The catalog's own entries: the JavaScript name tree, NeedsRendering, the embedded files tree, the collection and the Info dictionary.</summary>
    private void SanitizeCatalog()
    {
        CosDictionary catalog = _document.Catalog;
        CosDictionary? names = Dictionary(catalog, Names);
        if ((Actions & PdfSanitizeActions.JavaScript) != 0 && names is not null && names.ContainsKey(JavaScriptKey))
        {
            Count("document-level scripts (JavaScript name tree)", Math.Max(1, _document.Names?.JavaScript?.Count() ?? 0));
            names.Remove(JavaScriptKey);
        }

        if (_options.RemoveXfa && catalog.Remove(NeedsRendering))
        {
            Count("NeedsRendering flags");
        }

        if (_options.RemoveEmbeddedFiles)
        {
            if (_document.Names?.EmbeddedFiles is { } tree)
            {
                int removed = 0;
                foreach (string name in tree.Select(entry => entry.Key.DecodeText()).Distinct(StringComparer.Ordinal).ToArray())
                {
                    if (_document.RemoveEmbeddedFile(name))
                    {
                        removed++;
                    }
                }

                Count("embedded files (EmbeddedFiles name tree)", removed);
                _filesRemoved |= removed > 0;
            }

            if (names is not null && names.Remove(EmbeddedFiles))
            {
                _filesRemoved = true;
            }

            if (catalog.Remove(Collection))
            {
                Count("portable collections (Collection)");
            }
        }

        if (_options.RemoveMetadata)
        {
            if (_document.Trailer.Remove(Info))
            {
                Count("document information dictionaries (Info)");
            }

            if (catalog.Remove(Metadata))
            {
                Count("metadata streams (Metadata)");
            }
        }
    }

    /// <summary>
    /// One walk over every dictionary reachable from the catalog: action entries and their <c>Next</c> sequences, additional
    /// actions, <c>XFA</c>, <c>AF</c>, <c>EF</c>, <c>Metadata</c> and <c>PieceInfo</c>. A removed entry is not walked into.
    /// </summary>
    private void SweepObjects()
    {
        bool actions = Actions != PdfSanitizeActions.None;
        bool files = _options.RemoveEmbeddedFiles;
        bool metadata = _options.RemoveMetadata;
        bool xfa = _options.RemoveXfa;
        if (!actions && !files && !metadata && !xfa)
        {
            return;
        }

        Walk(dictionary =>
        {
            if (actions)
            {
                PruneActions(dictionary);
            }

            if (xfa && dictionary.Remove(XFA))
            {
                // §12.7.3, Table 224: XFA is an entry of the interactive form dictionary, wherever the walk reaches one.
                Count("XFA forms");
            }

            if (actions && (Actions & PdfSanitizeActions.JavaScript) != 0 && Dictionary(dictionary, JavaScriptKey) is { } scripts
                && (scripts.ContainsKey(Names) || scripts.ContainsKey(Kids)) && dictionary.Remove(JavaScriptKey))
            {
                // A name dictionary the catalog does not name directly (a repaired or shared one, §7.7.4): its script tree goes too.
                Count("document-level scripts (JavaScript name tree)");
            }

            if (files)
            {
                if (dictionary.Remove(EmbeddedFiles))
                {
                    _filesRemoved = true;
                }

                if (dictionary.Remove(AF))
                {
                    Count("associated files (AF)");
                    _filesRemoved = true;
                }

                if (dictionary.Remove(EF))
                {
                    dictionary.Remove(RF);
                    Count("embedded file streams (EF)");
                    _filesRemoved = true;
                }
            }

            if (metadata)
            {
                if (dictionary.Remove(Metadata))
                {
                    Count("metadata streams (Metadata)");
                }

                if (dictionary.Remove(PieceInfo))
                {
                    Count("page-piece dictionaries (PieceInfo)");
                }
            }
        }, skip: null);
    }

    private void PruneActions(CosDictionary dictionary)
    {
        if (dictionary.TryGetValue(A, out CosObject? action) && IsActionEntry(dictionary, action))
        {
            if (_pruner.Prune(dictionary, A) && !dictionary.ContainsKey(A))
            {
                _lostAction.Add(dictionary);
            }
        }

        if (dictionary.TryGetValue(PA, out CosObject? previous) && IsActionEntry(dictionary, previous))
        {
            _ = _pruner.Prune(dictionary, PA);
        }

        if (Resolve(dictionary.GetValueOrDefault(OpenAction)) is CosDictionary)
        {
            _ = _pruner.Prune(dictionary, OpenAction);
        }

        if (dictionary.ContainsKey(AA))
        {
            _ = _pruner.PruneAdditionalActions(dictionary, AA);
        }
    }

    /// <summary>
    /// Whether an <c>A</c> or <c>PA</c> entry is an action (§12.6.2): it names an action type or is typed <c>Action</c>, or its owner is
    /// an annotation (other than a movie annotation, whose <c>A</c> is an activation dictionary, §13.4) or an outline item. A
    /// structure element's attributes and a measure's area format, also named <c>A</c>, are neither.
    /// </summary>
    private bool IsActionEntry(CosDictionary owner, CosObject value)
    {
        if (Resolve(value) is CosDictionary action && (action.ContainsKey(S) || NameOf(action, TypeKey) == "Action"))
        {
            return true;
        }

        bool annotation = (NameOf(owner, TypeKey) == "Annot" || (owner.ContainsKey(Subtype) && owner.ContainsKey(Rect))) && NameOf(owner, Subtype) != "Movie";
        bool outlineItem = owner.ContainsKey(Title) && owner.ContainsKey(Parent);
        return annotation || outlineItem;
    }

    /// <summary>
    /// Visits every dictionary (a stream's dictionary for a stream) reachable from the trailer, the catalog and the pages once, following references, then walks
    /// into the entries <paramref name="visit"/> left; <paramref name="skip"/> is not entered.
    /// </summary>
    private void Walk(Action<CosDictionary> visit, CosObject? skip)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject>();
        // The pages as the document reads them too: a damaged page tree is repaired in the model (ADR 0005), and the save writes that.
        foreach (PdfPage page in _document.Pages)
        {
            pending.Push(page.Dictionary);
        }

        pending.Push(_document.Trailer);
        pending.Push(_document.Catalog);
        while (pending.Count > 0)
        {
            CosObject current = Resolve(pending.Pop());
            if (ReferenceEquals(current, skip) || !seen.Add(current))
            {
                continue;
            }

            switch (current)
            {
                case CosStream stream:
                    if (seen.Add(stream.Dictionary))
                    {
                        visit(stream.Dictionary);
                        PushAll(stream.Dictionary.Values, pending);
                    }

                    break;
                case CosDictionary dictionary:
                    visit(dictionary);
                    PushAll(dictionary.Values, pending);
                    break;
                case CosArray array:
                    PushAll(array, pending);
                    break;
            }
        }

        static void PushAll(IEnumerable<CosObject> values, Stack<CosObject> pending)
        {
            foreach (CosObject value in values.ToArray())
            {
                if (value is CosDictionary or CosArray or CosStream or CosReference)
                {
                    pending.Push(value);
                }
            }
        }
    }

    /// <summary>Per page: links left without an action, file attachments, comments, hidden annotations and thumbnails.</summary>
    private void SanitizePages()
    {
        PdfOptionalContentProperties? properties = _options.OptionalContent == PdfSanitizeOptionalContent.Keep ? null : _document.OptionalContent;
        PdfOptionalContentState? state = properties?.GetDefaultStates();
        bool flatten = _options.OptionalContent == PdfSanitizeOptionalContent.FlattenVisible;
        foreach (PdfPage page in _document.Pages)
        {
            if (_options.RemoveThumbnails && page.Dictionary.Remove(Thumb))
            {
                Count("thumbnails (Thumb)");
            }

            foreach (PdfAnnotation annotation in page.Annotations.ToArray())
            {
                CosDictionary dictionary = annotation.Dictionary;
                if (!page.Annotations.Any(candidate => ReferenceEquals(candidate.Dictionary, dictionary)))
                {
                    continue; // Removed with an annotation it belongs to.
                }

                string? reason = annotation.Kind switch
                {
                    PdfAnnotationKind.Link when _lostAction.Contains(dictionary) && !dictionary.ContainsKey(Dest) => "link annotations left without an action",
                    PdfAnnotationKind.FileAttachment when _options.RemoveEmbeddedFiles => "file attachment annotations",
                    _ when _options.RemoveComments && annotation is PdfMarkupAnnotation or PdfPopupAnnotation => "comments (markup annotations and pop-ups)",
                    _ => null,
                };

                if (reason is null && properties is not null && dictionary.TryGetValue(OC, out CosObject? group))
                {
                    bool visible = properties.IsVisible(group, state!);
                    if (!visible && annotation.Kind != PdfAnnotationKind.Widget)
                    {
                        reason = "hidden annotations (optional content)";
                    }
                    else if (flatten)
                    {
                        // §12.5.3: a widget cannot go without its field; hidden by its layer, it is hidden by its flag instead.
                        dictionary.Remove(OC);
                        if (!visible)
                        {
                            long flags = Resolve(dictionary.GetValueOrDefault(F)) is CosInteger { Value: long value } ? value : 0;
                            dictionary[F] = new CosInteger(flags | (long)PdfAnnotationFlags.Hidden);
                        }
                    }
                }

                if (reason is not null)
                {
                    int before = page.Annotations.Count;
                    page.RemoveAnnotation(annotation);
                    Count(reason, before - page.Annotations.Count);
                    _filesRemoved |= annotation.Kind == PdfAnnotationKind.FileAttachment;
                }
            }
        }
    }

    /// <summary>
    /// The content pass: each page, then each form XObject they use and each annotation appearance, rewritten once by
    /// <see cref="SanitizeContentEditor"/>; then the resources the runs used lose the XObjects and property lists only hidden content
    /// named, and with <see cref="PdfSanitizeOptionalContent.FlattenVisible"/> every <c>OC</c> entry and property list of a group.
    /// </summary>
    private void SanitizeContent()
    {
        PdfOptionalContentProperties? properties = _options.OptionalContent == PdfSanitizeOptionalContent.Keep ? null : _document.OptionalContent;
        if (properties is null && !_options.RemoveInvisibleText)
        {
            return;
        }

        var editor = new SanitizeContentEditor(_document, properties, _options.OptionalContent, _options.RemoveInvisibleText);
        var resources = new List<CosDictionary>();
        var seenResources = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var forms = new Queue<CosObject>();
        var seenForms = new HashSet<CosStream>(ReferenceEqualityComparer.Instance);

        void Collect(CosDictionary? dictionary)
        {
            if (dictionary is null || !seenResources.Add(dictionary))
            {
                return;
            }

            resources.Add(dictionary);
            if (Dictionary(dictionary, XObject) is not { } xobjects)
            {
                return;
            }

            foreach (CosObject value in xobjects.Values)
            {
                if (Resolve(value) is CosStream stream && NameOf(stream.Dictionary, Subtype) == "Form" && !editor.IsHidden(stream) && seenForms.Add(stream))
                {
                    forms.Enqueue(value);
                }
            }
        }

        void Appearance(CosObject? value)
        {
            switch (Resolve(value))
            {
                case CosStream stream when seenForms.Add(stream):
                    forms.Enqueue(value!);
                    break;
                case CosDictionary states:
                    foreach (CosObject state in states.Values)
                    {
                        if (Resolve(state) is CosStream stateStream && seenForms.Add(stateStream))
                        {
                            forms.Enqueue(state);
                        }
                    }

                    break;
            }
        }

        foreach (PdfPage page in _document.Pages)
        {
            _ = page.RewriteContent(editor);
            Collect(page.Resources);
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                if (Dictionary(annotation.Dictionary, AP) is { } appearances)
                {
                    foreach (CosObject appearance in appearances.Values)
                    {
                        Appearance(appearance);
                    }
                }
            }
        }

        while (forms.Count > 0)
        {
            if (PdfFormXObject.Create(_document, forms.Dequeue()) is { } form)
            {
                _ = form.RewriteContent(editor);
                Collect(form.Resources);
            }
        }

        Count("hidden content operators (optional content)", editor.HiddenOperators);
        Count("invisible text operators (rendering mode 3)", editor.InvisibleOperators);
        Count("optional content markers (BDC /OC … EMC)", editor.Markers);
        if (properties is not null)
        {
            CleanResources(resources, editor, properties);
        }
    }

    /// <summary>
    /// Removes from the resources the runs used the XObjects whose own <c>OC</c> is off (every <c>Do</c> of them is gone) and the
    /// property lists that only hidden content named (a group that is off, a membership that is hidden); with flattening, every
    /// remaining XObject's <c>OC</c> and every property list that is a group or membership.
    /// </summary>
    private void CleanResources(List<CosDictionary> resources, SanitizeContentEditor editor, PdfOptionalContentProperties properties)
    {
        bool flatten = _options.OptionalContent == PdfSanitizeOptionalContent.FlattenVisible;
        PdfOptionalContentState state = properties.GetDefaultStates();
        foreach (CosDictionary dictionary in resources)
        {
            if (Dictionary(dictionary, XObject) is { } xobjects)
            {
                foreach ((CosName name, CosObject value) in xobjects.ToArray())
                {
                    if (Resolve(value) is not CosStream stream)
                    {
                        continue;
                    }

                    if (editor.IsHidden(stream))
                    {
                        xobjects.Remove(name);
                        Count("hidden XObjects (optional content)");
                    }
                    else if (flatten)
                    {
                        stream.Dictionary.Remove(OC);
                    }
                }
            }

            if (Dictionary(dictionary, Properties) is { } lists)
            {
                foreach ((CosName name, CosObject value) in lists.ToArray())
                {
                    if (Resolve(value) is not CosDictionary list || NameOf(list, TypeKey) is not ("OCG" or "OCMD"))
                    {
                        continue;
                    }

                    if (flatten || !properties.IsVisible(value, state))
                    {
                        lists.Remove(name);
                    }
                }
            }
        }
    }

    /// <summary>
    /// With <see cref="PdfSanitizeOptionalContent.RemoveHidden"/>, removes from <c>OCProperties</c> the groups that are off and that
    /// nothing reachable refers to any more (a membership, a set-state action, an <c>OC</c> entry); with
    /// <see cref="PdfSanitizeOptionalContent.FlattenVisible"/>, removes <c>OCProperties</c>.
    /// </summary>
    private void SanitizeOptionalContentProperties()
    {
        if (_options.OptionalContent == PdfSanitizeOptionalContent.Keep || _document.OptionalContent is not { } properties)
        {
            return;
        }

        CosDictionary catalog = _document.Catalog;
        if (_options.OptionalContent == PdfSanitizeOptionalContent.FlattenVisible)
        {
            Count("optional content groups", properties.Groups.Count);
            catalog.Remove(OCProperties);
            return;
        }

        PdfOptionalContentState state = properties.GetDefaultStates();
        var off = new HashSet<CosDictionary>(properties.Groups.Where(group => !state.IsOn(group)).Select(group => group.Dictionary), ReferenceEqualityComparer.Instance);
        if (off.Count == 0)
        {
            return;
        }

        CosObject root = Resolve(catalog.GetValueOrDefault(OCProperties));
        var referenced = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var arrays = new HashSet<CosArray>(ReferenceEqualityComparer.Instance);
        Walk(dictionary =>
        {
            foreach (CosObject value in dictionary.Values)
            {
                Note(value);
            }
        }, skip: root);

        void Note(CosObject value)
        {
            switch (Resolve(value))
            {
                case CosDictionary group when off.Contains(group):
                    referenced.Add(group);
                    break;
                case CosArray array when arrays.Add(array):
                    // A membership's OCGs and VE (nested arrays, §8.11.2.2), a set-state action's State.
                    foreach (CosObject item in array)
                    {
                        Note(item);
                    }

                    break;
            }
        }

        off.ExceptWith(referenced);
        if (off.Count == 0)
        {
            return;
        }

        CosDictionary ocProperties = (CosDictionary)root;
        RemoveGroups(ocProperties.GetValueOrDefault(OCGs), off);
        var configurations = new List<CosDictionary>();
        if (Dictionary(ocProperties, D) is { } defaults)
        {
            configurations.Add(defaults);
        }

        if (Resolve(ocProperties.GetValueOrDefault(Configs)) is CosArray configs)
        {
            configurations.AddRange(configs.Select(item => Resolve(item)).OfType<CosDictionary>());
        }

        foreach (CosDictionary configuration in configurations)
        {
            foreach (CosName key in (ReadOnlySpan<CosName>)[On, Off, Locked, Order, RBGroups])
            {
                RemoveGroups(configuration.GetValueOrDefault(key), off);
            }

            if (Resolve(configuration.GetValueOrDefault(AS)) is CosArray usages)
            {
                foreach (CosObject usage in usages)
                {
                    if (Resolve(usage) is CosDictionary application)
                    {
                        RemoveGroups(application.GetValueOrDefault(OCGs), off);
                    }
                }
            }
        }

        Count("optional content groups", off.Count);
    }

    /// <summary>Removes <paramref name="groups"/> from an array of groups, and from the arrays nested in it (<c>Order</c>, <c>RBGroups</c>).</summary>
    private void RemoveGroups(CosObject? value, HashSet<CosDictionary> groups)
    {
        var seen = new HashSet<CosArray>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosArray>();
        if (Resolve(value) is CosArray top)
        {
            pending.Push(top);
        }

        while (pending.Count > 0)
        {
            CosArray array = pending.Pop();
            if (!seen.Add(array))
            {
                continue;
            }

            for (int index = array.Count - 1; index >= 0; index--)
            {
                switch (Resolve(array[index]))
                {
                    case CosDictionary group when groups.Contains(group):
                        array.RemoveAt(index);
                        break;
                    case CosArray nested:
                        pending.Push(nested);
                        break;
                }
            }
        }
    }
}
