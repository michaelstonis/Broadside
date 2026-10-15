using Broadside.Annotations;
using Broadside.Filters;
using Broadside.Forms;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Signatures.Validation;

/// <summary>What an object is to the document of one revision, as far as modification detection needs to know.</summary>
internal enum ObjectRole : byte
{
    /// <summary>Nothing the walk from the catalog reached.</summary>
    None,

    /// <summary>The catalog (§7.7.2).</summary>
    Catalog,

    /// <summary>The document information dictionary (§14.3.3) or the catalog's metadata stream (§14.3.2).</summary>
    DocumentInformation,

    /// <summary>The interactive form dictionary (§12.7.3).</summary>
    AcroForm,

    /// <summary>A field dictionary, possibly merged with its widget (§12.7.4).</summary>
    Field,

    /// <summary>A field's value held in an object of its own (a rich text stream, an array), §12.7.4.</summary>
    FieldValue,

    /// <summary>The form's <c>Fields</c> array or a field's <c>Kids</c> array (§12.7.3, §12.7.4).</summary>
    FieldArray,

    /// <summary>A widget annotation that is not merged with its field (§12.5.6.19).</summary>
    Widget,

    /// <summary>A signature field's lock or seed value dictionary (§12.7.5.5).</summary>
    FieldLockOrSeedValue,

    /// <summary>A signature field's signature dictionary (§12.8.1).</summary>
    SignatureDictionary,

    /// <summary>A page tree node (§7.7.3.2).</summary>
    PageTreeNode,

    /// <summary>A page object (§7.7.3.3).</summary>
    Page,

    /// <summary>A page's <c>Annots</c> array.</summary>
    AnnotationArray,

    /// <summary>An annotation other than a widget, or an object only it reaches (§12.5).</summary>
    Annotation,

    /// <summary>A page's content streams or resources, or an object they reach (§7.8).</summary>
    PageContent,

    /// <summary>A widget's appearance or appearance characteristics, or an object only they reach (§12.5.5).</summary>
    Appearance,

    /// <summary>The form's default resources or an object they reach (§12.7.3, <c>DR</c>).</summary>
    FormResources,

    /// <summary>The document security store or an object it reaches (§12.8.4.3).</summary>
    SecurityStore,
}

/// <summary>An object's role, and the field it belongs to.</summary>
/// <param name="Role">The role.</param>
/// <param name="FieldName">The fully qualified name of the field of a field, widget, appearance, value or lock; <see langword="null"/> otherwise.</param>
/// <param name="FieldObject">The object number of that field's dictionary; 0 when none or direct.</param>
/// <param name="IsSignatureField">Whether that field is a signature field.</param>
internal readonly record struct RoleInfo(ObjectRole Role, string? FieldName = null, int FieldObject = 0, bool IsSignatureField = false);

/// <summary>
/// The document as one revision left it: an object loader over the file's sections up to that revision (§7.5.6: revision k is the
/// first <see cref="PdfRevision.Length"/> bytes), sharing the document's decryption, with the role of every object the catalog
/// reaches.
/// </summary>
/// <remarks>ISO 32000-2 §7.5.6 and §12.8.2.2.2 ("the portion of the document specified by the ByteRange ... is known to correspond to the state of the document at the time of signing").</remarks>
internal sealed class RevisionState
{
    private static readonly CosName FieldsName = FormNames.Fields;
    private static readonly CosName Dss = new("DSS");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName Info = new("Info");
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName AP = new("AP");
    private static readonly CosName MK = new("MK");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Widget = new("Widget");
    private static readonly CosName Pages = new("Pages");
    private static readonly CosName Kids = new("Kids");
    private static readonly HashSet<CosName> AnnotationLinks = [new("P"), new("Popup"), new("IRT"), new("Parent"), new("Dest"), new("A")];

    private readonly ObjectLoader _loader;
    private readonly Lazy<Dictionary<int, RoleInfo>> _roles;

    public RevisionState(int index, PdfSource source, ObjectLoader document, StreamDecoder streams)
    {
        Index = index;
        IReadOnlyList<XrefRevision> revisions = document.CrossReference.Revisions;
        var sections = new List<XrefSection>();
        for (int revision = index; revision >= 0; revision--)
        {
            sections.AddRange(revisions[revision].Sections);
        }

        CrossReference = new CrossReference(sections);
        _loader = new ObjectLoader(source, document.Header, CrossReference, new DiagnosticSink(strict: false), new ObjectLoaderHooks { Decryptor = document.Hooks.Decryptor }, streams);
        _roles = new Lazy<Dictionary<int, RoleInfo>>(FindRoles);
    }

    /// <summary>Gets the revision's index.</summary>
    public int Index { get; }

    /// <summary>Gets the cross-reference information up to the revision.</summary>
    public CrossReference CrossReference { get; }

    /// <summary>Gets the merged trailer of the revision.</summary>
    public CosDictionary Trailer => CrossReference.Trailer;

    /// <summary>Gets the catalog, or an empty dictionary when the trailer does not lead to one.</summary>
    public CosDictionary Catalog => Resolve(Trailer.GetValueOrDefault(KnownNames.Root)) as CosDictionary ?? [];

    public CosObject Resolve(CosObject? value) => _loader.Resolve(value);

    /// <summary>Returns the object numbered <paramref name="number"/> in this revision and its reference, or null when it is free.</summary>
    public (CosReference Reference, CosObject Value)? Load(int number)
    {
        if (!CrossReference.TryGetEntry(number, out XrefEntry entry) || entry.Kind == XrefEntryKind.Free)
        {
            return null;
        }

        var reference = new CosReference(number, entry.Kind == XrefEntryKind.InUse ? entry.Generation : 0);
        CosObject value = _loader.Load(reference);
        return value is CosNull ? null : (reference, value);
    }

    /// <summary>Returns the role of the object numbered <paramref name="number"/>.</summary>
    public RoleInfo RoleOf(int number) => _roles.Value.TryGetValue(number, out RoleInfo role) ? role : default;

    /// <summary>Gets the fully qualified names of the fields of the form.</summary>
    public IEnumerable<string> FieldNames => _roles.Value.Values.Where(static role => role.Role == ObjectRole.Field).Select(static role => role.FieldName!);

    /// <summary>Whether the signature field whose dictionary is <paramref name="fieldObject"/> has a value, and whether it is a document timestamp.</summary>
    public (bool Signed, bool Timestamp) SignatureOf(int fieldObject)
    {
        if (Load(fieldObject) is not { Value: CosDictionary field } || Resolve(field.GetValueOrDefault(FormNames.V)) is not CosDictionary signature)
        {
            return (false, false);
        }

        return (true, TransformMethods.IsDocumentTimestamp(signature, Resolve));
    }

    private Dictionary<int, RoleInfo> FindRoles()
    {
        var roles = new Dictionary<int, RoleInfo>();
        var widgets = new List<(CosObject Value, RoleInfo Owner)>();
        var annotations = new List<CosObject>();
        var pages = new List<CosDictionary>();
        CosDictionary trailer = Trailer;
        Assign(roles, trailer.GetValueOrDefault(KnownNames.Root), new(ObjectRole.Catalog));
        Assign(roles, trailer.GetValueOrDefault(Info), new(ObjectRole.DocumentInformation));
        CosDictionary catalog = Catalog;
        Assign(roles, catalog.GetValueOrDefault(Metadata), new(ObjectRole.DocumentInformation));

        // The form first, so that widgets listed in Annots are known as their fields'.
        CosObject? formValue = catalog.GetValueOrDefault(FormNames.AcroForm);
        Assign(roles, formValue, new(ObjectRole.AcroForm));
        CosDictionary? form = Resolve(formValue) as CosDictionary;
        if (form is not null)
        {
            CosObject? fields = form.GetValueOrDefault(FieldsName);
            Assign(roles, fields, new(ObjectRole.FieldArray));
            if (Resolve(fields) is CosArray array)
            {
                WalkFields(roles, array, parent: null, inheritedType: null, widgets, depth: 0);
            }
        }

        // The page tree: nodes, pages, Annots arrays and annotations.
        var nodes = new Stack<(CosObject Node, int Depth)>();
        nodes.Push((catalog.GetValueOrDefault(Pages)!, 0));
        var seenNodes = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        while (nodes.Count > 0)
        {
            (CosObject node, int depth) = nodes.Pop();
            if (Resolve(node) is not CosDictionary dictionary || !seenNodes.Add(dictionary) || depth > 256)
            {
                continue;
            }

            if (Resolve(dictionary.GetValueOrDefault(Kids)) is CosArray kids && !Widget.Equals(Resolve(dictionary.GetValueOrDefault(Subtype))))
            {
                Assign(roles, node, new(ObjectRole.PageTreeNode));
                Assign(roles, dictionary.GetValueOrDefault(Kids), new(ObjectRole.PageTreeNode));
                for (int index = kids.Count - 1; index >= 0; index--)
                {
                    nodes.Push((kids[index], depth + 1));
                }

                continue;
            }

            Assign(roles, node, new(ObjectRole.Page));
            pages.Add(dictionary);
            CosObject? annots = dictionary.GetValueOrDefault(AnnotationNames.Annots);
            Assign(roles, annots, new(ObjectRole.AnnotationArray));
            if (Resolve(annots) is CosArray list)
            {
                foreach (CosObject annotation in list)
                {
                    if (annotation is CosReference reference && roles.TryGetValue(reference.ObjectNumber, out RoleInfo known)
                        && known.Role is ObjectRole.Field or ObjectRole.Widget)
                    {
                        continue;
                    }

                    if (Resolve(annotation) is CosDictionary dictionaryValue && Widget.Equals(Resolve(dictionaryValue.GetValueOrDefault(Subtype))))
                    {
                        Assign(roles, annotation, new(ObjectRole.Widget));
                        widgets.Add((annotation, new(ObjectRole.Appearance)));
                    }
                    else
                    {
                        Assign(roles, annotation, new(ObjectRole.Annotation));
                        annotations.Add(annotation);
                    }
                }
            }
        }

        // What the pages draw comes before what appearances draw: an object both reach is the page's.
        foreach (CosDictionary page in pages)
        {
            Walk(roles, page.GetValueOrDefault(Contents), new(ObjectRole.PageContent));
            Walk(roles, page.GetValueOrDefault(Resources), new(ObjectRole.PageContent));
        }

        foreach ((CosObject widget, RoleInfo owner) in widgets)
        {
            if (Resolve(widget) is CosDictionary dictionary)
            {
                Walk(roles, dictionary.GetValueOrDefault(AP), owner);
                Walk(roles, dictionary.GetValueOrDefault(MK), owner);
            }
        }

        foreach (CosObject annotation in annotations)
        {
            if (Resolve(annotation) is CosDictionary dictionary)
            {
                foreach ((CosName key, CosObject value) in dictionary)
                {
                    if (!AnnotationLinks.Contains(key))
                    {
                        Walk(roles, value, new(ObjectRole.Annotation));
                    }
                }
            }
        }

        if (form is not null)
        {
            Walk(roles, form.GetValueOrDefault(FormNames.DR), new(ObjectRole.FormResources));
        }

        Walk(roles, catalog.GetValueOrDefault(Dss), new(ObjectRole.SecurityStore));
        return roles;
    }

    private void WalkFields(Dictionary<int, RoleInfo> roles, CosArray kids, RoleInfo? parent, CosName? inheritedType, List<(CosObject, RoleInfo)> widgets, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        foreach (CosObject kid in kids)
        {
            if (Resolve(kid) is not CosDictionary dictionary)
            {
                continue;
            }

            int number = kid is CosReference kidReference ? kidReference.ObjectNumber : 0;
            if (number != 0 && roles.ContainsKey(number))
            {
                continue;
            }

            CosName? type = Resolve(dictionary.GetValueOrDefault(FormNames.FT)) as CosName ?? inheritedType;
            bool signature = FormNames.Sig.Equals(type);
            if (Resolve(dictionary.GetValueOrDefault(FormNames.T)) is not CosString title)
            {
                // A widget of the field above (§12.7.4.1: a kid without T is a widget, not a field).
                var widget = new RoleInfo(ObjectRole.Widget, parent?.FieldName, parent?.FieldObject ?? 0, signature);
                Assign(roles, kid, widget);
                widgets.Add((kid, widget with { Role = ObjectRole.Appearance }));
                continue;
            }

            string name = parent?.FieldName is { } parentName ? $"{parentName}.{title.DecodeText()}" : title.DecodeText();
            var field = new RoleInfo(ObjectRole.Field, name, number, signature);
            Assign(roles, kid, field);
            widgets.Add((kid, field with { Role = ObjectRole.Appearance }));
            CosObject? value = dictionary.GetValueOrDefault(FormNames.V);
            Assign(roles, value, field with { Role = signature ? ObjectRole.SignatureDictionary : ObjectRole.FieldValue });
            Assign(roles, dictionary.GetValueOrDefault(FormNames.Lock), field with { Role = ObjectRole.FieldLockOrSeedValue });
            Assign(roles, dictionary.GetValueOrDefault(FormNames.SV), field with { Role = ObjectRole.FieldLockOrSeedValue });
            CosObject? children = dictionary.GetValueOrDefault(Kids);
            Assign(roles, children, field with { Role = ObjectRole.FieldArray });
            if (Resolve(children) is CosArray array)
            {
                WalkFields(roles, array, field, type, widgets, depth + 1);
            }
        }
    }

    /// <summary>Gives <paramref name="role"/> to <paramref name="value"/> when it is a reference without one.</summary>
    private static void Assign(Dictionary<int, RoleInfo> roles, CosObject? value, RoleInfo role)
    {
        if (value is CosReference reference)
        {
            roles.TryAdd(reference.ObjectNumber, role);
        }
    }

    /// <summary>Gives <paramref name="role"/> to every object <paramref name="start"/> reaches that has none yet, stopping at objects that have one.</summary>
    private void Walk(Dictionary<int, RoleInfo> roles, CosObject? start, RoleInfo role)
    {
        if (start is null)
        {
            return;
        }

        var pending = new Stack<CosObject>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            CosObject value = pending.Pop();
            if (value is CosReference reference)
            {
                if (!roles.TryAdd(reference.ObjectNumber, role))
                {
                    continue;
                }

                value = _loader.Resolve(reference);
            }

            switch (value)
            {
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push(element);
                    }

                    break;
                case CosStream stream:
                    foreach (CosObject element in stream.Dictionary.Values)
                    {
                        pending.Push(element);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (CosObject element in dictionary.Values)
                    {
                        pending.Push(element);
                    }

                    break;
            }
        }
    }
}
