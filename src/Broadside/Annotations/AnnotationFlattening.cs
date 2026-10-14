using System.Text;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Structure;

namespace Broadside.Annotations;

/// <summary>
/// Flattens annotations into page content (<see cref="PdfDocument.Flatten(PdfFlattenOptions)"/>): plans every page first, without
/// changing anything, then rewrites each page's content, resources and <c>Annots</c>, undoing every change if one fails.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.5 (Algorithm "Appearance streams"), §12.5.3 (flags), §8.10 (form XObjects), §8.11.3.2 (optional content in
/// content), §14.7.5.2-§14.7.5.4 (content items and the parent tree of removed annotations), §12.8.6 (permissions).
/// </remarks>
internal static class AnnotationFlattening
{
    private static readonly CosName ExtGState = new("ExtGState");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName Properties = new("Properties");
    private static readonly CosName CA = new("CA");
    private static readonly CosName LowerCA = new("ca");
    private static readonly CosName BM = new("BM");
    private static readonly CosName S = new("S");
    private static readonly CosName Transparency = new("Transparency");
    private static readonly CosName ExtGStateType = new("ExtGState");
    private static readonly CosName XObjectType = new("XObject");
    private static readonly CosName FormSubtype = new("Form");
    private static readonly CosName OC = new("OC");
    private static readonly CosName Appearance = new("Ap");
    private static readonly CosName Perms = new("Perms");
    private static readonly CosName Span = new("Span");

    public static bool Flatten(PdfDocument document, PdfFlattenOptions options)
    {
        PdfAcroForm? form = document.AcroForm;
        if (form?.Xfa is not null)
        {
            throw new InvalidOperationException(
                "The form has an XFA resource (Annex K, reported at open as XfaFormPresent): XFA lays its fields out, so the AcroForm appearances may not be what the form shows; it is not flattened.");
        }

        List<PagePlan> plans = [];
        foreach (PdfPage page in document.Pages)
        {
            if (Plan(page, options) is { } plan)
            {
                plans.Add(plan);
            }
        }

        if (plans.Count == 0 && form is null)
        {
            return false;
        }

        StructurePlan? structure = PlanStructure(document, plans);

        if (form is not null && form.TerminalFields.OfType<PdfSignatureField>().Any(field => field.IsSigned))
        {
            form.Report(
                DiagnosticCodes.FlattenInvalidatesSignatures,
                "The form is signed: flattening removes its signature fields and changes the document they cover, so the signatures no longer verify (§12.8).");
        }

        var undo = new UndoLog();
        try
        {
            foreach (PagePlan plan in plans)
            {
                Apply(document, plan, undo);
            }

            if (form is not null)
            {
                RemovePermissions(document, form, undo);
                undo.Remove(document.Catalog, FormNames.AcroForm);
            }

            structure?.Apply();
        }
        catch
        {
            undo.Revert();
            throw;
        }

        return true;
    }

    /// <summary>Decides what leaves a page and what of it is drawn; null when nothing on the page is flattened.</summary>
    private static PagePlan? Plan(PdfPage page, PdfFlattenOptions options)
    {
        IReadOnlyList<PdfAnnotation> annotations = page.Annotations;
        var removed = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        List<Placement> drawn = [];
        List<CosObject> kept = [];
        foreach (PdfAnnotation annotation in annotations)
        {
            if (annotation.Kind != PdfAnnotationKind.Widget && !options.IncludeAnnotations)
            {
                kept.Add(annotation.Reference ?? (CosObject)annotation.Dictionary);
                continue;
            }

            if (!removed.Add(annotation.Dictionary) || annotation.Kind == PdfAnnotationKind.Popup || !IsShown(annotation, options.Intent))
            {
                continue;
            }

            if (annotation.GetAppearance() is { } appearance && Place(annotation, appearance) is { } placement)
            {
                drawn.Add(placement);
            }
        }

        return removed.Count == 0 ? null : new PagePlan(page, drawn, kept, removed);
    }

    /// <summary>
    /// Finds the structure-tree object references (OBJR, §14.7.5.3) of the annotations that leave, without changing anything, and
    /// gives each drawn one's placement the element that held it and a free MCID of its page; null when nothing refers to them.
    /// </summary>
    private static StructurePlan? PlanStructure(PdfDocument document, List<PagePlan> plans)
    {
        if (plans.Count == 0 || !document.Catalog.ContainsKey(StructureNames.StructTreeRoot) || document.StructureTree is not { } tree)
        {
            return null;
        }

        StructureContext context = tree.Context;
        var removed = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PagePlan plan in plans)
        {
            removed.UnionWith(plan.Removed);
        }

        Dictionary<CosDictionary, List<PdfObjectReference>> references = StructureWriter.FindObjectReferences(context, removed);
        var tagged = new Dictionary<CosDictionary, (PdfPage Page, int Mcid)>(ReferenceEqualityComparer.Instance);
        foreach (PagePlan plan in plans)
        {
            int? next = null;
            for (int index = 0; index < plan.Drawn.Count; index++)
            {
                Placement placement = plan.Drawn[index];
                CosDictionary annotation = placement.Annotation.Dictionary;
                if (!references.TryGetValue(annotation, out List<PdfObjectReference>? held) || tagged.ContainsKey(annotation) || plan.Page.Reference is null)
                {
                    continue;
                }

                // MCIDs are unique per content stream (§14.7.5.2): past those the tree knows of and those the page's content uses.
                // An MCID indexes the page's parent-tree array, so one past the length the reader takes leaves the content untagged.
                next ??= Math.Max(StructureWriter.PeekNextMcid(context, plan.Page), McidScanner.NextMcid(plan.Page));
                int mcid = next.Value;
                if (mcid >= StructureContext.MaxParentTreeArray)
                {
                    continue;
                }

                next = mcid + 1;
                tagged.Add(annotation, (plan.Page, mcid));
                plan.Drawn[index] = placement with { Owner = held[0].Parent, Mcid = mcid };
            }
        }

        return new StructurePlan(context, removed, references, tagged);
    }

    /// <summary>
    /// Removes the entries of the catalog's permissions dictionary (<c>Perms</c>, §12.8.6, Table 263) that name a signature of the form
    /// being removed: a certification signature's dictionary is a signature field's <c>V</c> that <c>DocMDP</c> refers to (§12.8.1,
    /// §12.8.2.2). A usage rights signature (<c>UR3</c>) is not a field's value, so it stays. <c>Perms</c> goes when nothing is left.
    /// </summary>
    private static void RemovePermissions(PdfDocument document, PdfAcroForm form, UndoLog undo)
    {
        if (!document.Catalog.TryGetValue(Perms, out CosObject? value) || document.Resolve(value) is not CosDictionary permissions)
        {
            return;
        }

        var signatures = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfSignatureField field in form.TerminalFields.OfType<PdfSignatureField>())
        {
            if (field.SignatureDictionary is { } signature)
            {
                signatures.Add(signature);
            }
        }

        List<CosName> removed = [.. permissions.Where(entry => document.Resolve(entry.Value) is CosDictionary target && signatures.Contains(target)).Select(entry => entry.Key)];
        if (removed.Count == 0)
        {
            return;
        }

        if (removed.Count == permissions.Count)
        {
            undo.Remove(document.Catalog, Perms);
            return;
        }

        foreach (CosName key in removed)
        {
            undo.Remove(permissions, key);
        }
    }

    /// <summary>Applies Table 167's Hidden, Invisible, NoView and Print flags for the intent.</summary>
    private static bool IsShown(PdfAnnotation annotation, PdfFlattenIntent intent)
    {
        PdfAnnotationFlags flags = annotation.Flags;
        if ((flags & PdfAnnotationFlags.Hidden) != 0 || ((flags & PdfAnnotationFlags.Invisible) != 0 && annotation is PdfUnknownAnnotation))
        {
            return false;
        }

        return intent == PdfFlattenIntent.Print ? (flags & PdfAnnotationFlags.Print) != 0 : (flags & PdfAnnotationFlags.NoView) == 0;
    }

    /// <summary>
    /// Computes where an appearance goes: the matrix <c>A</c> of Algorithm "Appearance streams" (§12.5.5), which maps the form's
    /// transformed bounding box onto the rectangle; <c>Do</c> applies the form's <c>Matrix</c> itself, so <c>A</c> after it is <c>AA</c>.
    /// </summary>
    private static Placement? Place(PdfAnnotation annotation, PdfFormXObject appearance)
    {
        PdfRectangle rect = annotation.Rect;
        PdfRectangle box = appearance.BoundingBox ?? new PdfRectangle(0, 0, rect.Width, rect.Height);
        Matrix matrix = appearance.Matrix;
        PathPoint a = matrix.Transform(box.Left, box.Bottom);
        PathPoint b = matrix.Transform(box.Right, box.Bottom);
        PathPoint c = matrix.Transform(box.Right, box.Top);
        PathPoint d = matrix.Transform(box.Left, box.Top);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        if (!(right - left > 0) || !(top - bottom > 0) || !(rect.Width > 0) || !(rect.Height > 0))
        {
            return null;
        }

        double scaleX = rect.Width / (right - left);
        double scaleY = rect.Height / (top - bottom);
        var toRect = new Matrix(scaleX, 0, 0, scaleY, rect.Left - (left * scaleX), rect.Bottom - (bottom * scaleY));
        annotation.Dictionary.TryGetValue(OC, out CosObject? optionalContent);
        return new Placement(
            annotation,
            appearance,
            box,
            toRect,
            new PdfRectangle(left, bottom, right, top),
            annotation.OptionalContent is null ? null : optionalContent,
            Math.Clamp(annotation.StrokingOpacity, 0, 1),
            Math.Clamp(annotation.NonStrokingOpacity, 0, 1),
            annotation.BlendMode);
    }

    private static void Apply(PdfDocument document, PagePlan plan, UndoLog undo)
    {
        CosDictionary page = plan.Page.Dictionary;
        if (plan.Drawn.Count > 0)
        {
            var resources = new CosDictionary();
            if (plan.Page.Resources is { } existing)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in existing)
                {
                    resources[entry.Key] = entry.Value;
                }
            }

            var categories = new Dictionary<CosName, CosDictionary>();
            var forms = new Dictionary<CosReference, CosName>();
            var writer = new ContentWriter();
            foreach (Placement placement in plan.Drawn)
            {
                Write(document, placement, writer, resources, categories, forms);
            }

            byte[] appended = writer.ToArray();
            undo.Set(page, KnownNames.Contents, page.TryGetValue(KnownNames.Contents, out CosObject? contents) ? contents : null);
            if (!plan.Page.RewriteContent(new AppendingEditor(appended)))
            {
                // No operator to wrap: the page draws nothing of its own, so the appearances are its whole content.
                page[KnownNames.Contents] = document.AddObject(new CosStream(new CosDictionary(), appended));
            }

            undo.Set(page, KnownNames.Resources, resources);
        }

        // A page that keeps nothing loses its own Annots, unless a page tree node above it lists annotations (read as viewers read
        // it, when the page has none): it then keeps an empty one, so the node's annotations do not show on it instead.
        if (plan.Kept.Count == 0)
        {
            undo.Remove(page, AnnotationNames.Annots);
            if (plan.Page.FindInheritedAnnots() is null)
            {
                return;
            }
        }

        undo.Set(page, AnnotationNames.Annots, new CosArray(plan.Kept));
    }

    /// <summary>Writes one appearance's placement: <c>[/OC /name BDC] q [/gs gs] A cm /name Do Q [EMC]</c>, naming what it uses in the page's resources.</summary>
    private static void Write(
        PdfDocument document,
        Placement placement,
        ContentWriter writer,
        CosDictionary resources,
        Dictionary<CosName, CosDictionary> categories,
        Dictionary<CosReference, CosName> forms)
    {
        if (placement.Owner is { } owner)
        {
            // §14.7.5.2: the sequence's MCID, with the element's type as its tag, ties the content to the element (§14.8.2.5.2).
            writer.Name(owner.StructureType ?? Span).Write("<</MCID "u8);
            writer.Number(placement.Mcid).Write(">> "u8);
            writer.Operator("BDC");
        }

        if (placement.OptionalContent is { } optionalContent)
        {
            CosName properties = Add(document, resources, categories, Properties, "BrsOC", optionalContent);
            writer.Name(OC).Name(properties).Operator("BDC");
        }

        writer.Operator("q");
        bool transparent = placement.StrokingOpacity < 1 || placement.NonStrokingOpacity < 1 || placement.BlendMode != BlendMode.Normal;
        if (transparent)
        {
            var state = new CosDictionary
            {
                [KnownNames.Type] = ExtGStateType,
                [CA] = new CosReal(placement.StrokingOpacity),
                [LowerCA] = new CosReal(placement.NonStrokingOpacity),
                [BM] = new CosName(Enum.GetName(placement.BlendMode)!),
            };
            writer.Name(Add(document, resources, categories, ExtGState, "BrsGS", state)).Operator("gs");
        }

        Matrix m = placement.ToRectangle;
        writer.Number(m.A).Number(m.B).Number(m.C).Number(m.D).Number(m.E).Number(m.F).Operator("cm");
        CosReference form = FormReference(document, placement);
        if (transparent && placement.Appearance.Group is null)
        {
            // §12.5.5: an appearance without a Group is a non-isolated, non-knockout transparency group, composited with BM, ca and
            // CA; drawn through a group form so the opacity applies to the whole, not to each of its objects.
            form = document.AddObject(Group(form, placement.TransformedBox));
        }

        if (!forms.TryGetValue(form, out CosName? name))
        {
            name = Add(document, resources, categories, XObject, "BrsFlat", form);
            forms[form] = name;
        }

        writer.Name(name).Operator("Do");
        writer.Operator("Q");
        if (placement.OptionalContent is not null)
        {
            writer.Operator("EMC");
        }

        if (placement.Owner is not null)
        {
            writer.Operator("EMC");
        }
    }

    /// <summary>
    /// Returns the reference that draws the appearance with <c>Do</c>: its own when it is an indirect form XObject with a bounding box
    /// (Table 93), else a new stream with the same data whose dictionary says so.
    /// </summary>
    private static CosReference FormReference(PdfDocument document, Placement placement)
    {
        PdfFormXObject appearance = placement.Appearance;
        if (appearance.Reference is { } reference && appearance.IsForm && appearance.BoundingBox is not null)
        {
            return reference;
        }

        var dictionary = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in appearance.Dictionary)
        {
            dictionary[entry.Key] = entry.Value;
        }

        dictionary[KnownNames.Type] = XObjectType;
        dictionary[AnnotationNames.Subtype] = FormSubtype;
        dictionary[AnnotationNames.BBox] = Rectangle(placement.BoundingBox);
        return document.AddObject(new CosStream(dictionary, appearance.Stream.EncodedData));
    }

    /// <summary>A form whose only content draws <paramref name="form"/>, as a transparency group (§11.6.6) over its transformed box.</summary>
    private static CosStream Group(CosObject form, PdfRectangle box) =>
        new(
            new CosDictionary
            {
                [KnownNames.Type] = XObjectType,
                [AnnotationNames.Subtype] = FormSubtype,
                [AnnotationNames.BBox] = Rectangle(box),
                [AnnotationNames.Group] = new CosDictionary { [KnownNames.Type] = new CosName("Group"), [S] = Transparency },
                [AnnotationNames.Resources] = new CosDictionary { [XObject] = new CosDictionary { [Appearance] = form } },
            },
            "/Ap Do\n"u8.ToArray());

    private static CosArray Rectangle(PdfRectangle box) =>
        new([new CosReal(box.Left), new CosReal(box.Bottom), new CosReal(box.Right), new CosReal(box.Top)]);

    /// <summary>Adds <paramref name="value"/> to a resource category under a name it does not use yet, copying the category first.</summary>
    private static CosName Add(PdfDocument document, CosDictionary resources, Dictionary<CosName, CosDictionary> categories, CosName category, string stem, CosObject value)
    {
        if (!categories.TryGetValue(category, out CosDictionary? entries))
        {
            entries = new CosDictionary();
            if (resources.TryGetValue(category, out CosObject? existing) && document.Resolve(existing) is CosDictionary old)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in old)
                {
                    entries[entry.Key] = entry.Value;
                }
            }

            resources[category] = entries;
            categories[category] = entries;
        }

        for (int index = 0; ; index++)
        {
            var name = new CosName(stem + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!entries.ContainsKey(name))
            {
                entries[name] = value;
                return name;
            }
        }
    }

    /// <summary>One annotation drawn into its page.</summary>
    private sealed record Placement(
        PdfAnnotation Annotation,
        PdfFormXObject Appearance,
        PdfRectangle BoundingBox,
        Matrix ToRectangle,
        PdfRectangle TransformedBox,
        CosObject? OptionalContent,
        double StrokingOpacity,
        double NonStrokingOpacity,
        BlendMode BlendMode)
    {
        /// <summary>Gets the structure element whose object reference held the annotation, which the drawn appearance is now marked content of.</summary>
        public PdfStructureElement? Owner { get; init; }

        /// <summary>Gets the MCID of the marked-content sequence the appearance is drawn in, when it has an <see cref="Owner"/>.</summary>
        public int Mcid { get; init; }
    }

    /// <summary>What a page draws and keeps: the annotations it keeps, in <c>Annots</c> order, are what its new <c>Annots</c> lists.</summary>
    private sealed record PagePlan(PdfPage Page, List<Placement> Drawn, List<CosObject> Kept, HashSet<CosDictionary> Removed);

    /// <summary>
    /// Wraps a page's operators in <c>q</c> … <c>Q</c> and appends the appearances after them: as many <c>q</c> as the content
    /// restores beyond what it saved, plus one, and enough <c>Q</c> to come back to the initial graphics state (§8.4.2), with an
    /// unclosed text object or marked-content sequence closed first (§9.4, §14.6).
    /// </summary>
    private sealed class AppendingEditor(byte[] appended) : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.None;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Skip;

        public override void EndRun(ContentContext context)
        {
            int count = OperatorCount;
            if (count == 0)
            {
                return;
            }

            int depth = 0;
            int lowest = 0;
            int marked = 0;
            bool inText = false;
            for (int index = 0; index < count; index++)
            {
                switch (GetOperator(index).Code)
                {
                    case ContentOperatorCode.SaveState:
                        depth++;
                        break;
                    case ContentOperatorCode.RestoreState:
                        lowest = Math.Min(lowest, --depth);
                        break;
                    case ContentOperatorCode.BeginText:
                        inText = true;
                        break;
                    case ContentOperatorCode.EndText:
                        inText = false;
                        break;
                    case ContentOperatorCode.BeginMarkedContent or ContentOperatorCode.BeginMarkedContentWithProperties:
                        marked++;
                        break;
                    case ContentOperatorCode.EndMarkedContent:
                        marked = Math.Max(0, marked - 1);
                        break;
                }
            }

            var before = new StringBuilder();
            before.Insert(0, "q\n", 1 - lowest);
            var after = new StringBuilder();
            if (inText)
            {
                after.Append("ET\n");
            }

            after.Insert(after.Length, "EMC\n", marked).Insert(after.Length, "Q\n", 1 - lowest + depth);
            byte[] closing = [.. Encoding.ASCII.GetBytes(after.ToString()), .. appended];
            Wrap(0, count - 1, Encoding.ASCII.GetBytes(before.ToString().TrimEnd('\n')), closing);
        }
    }

    /// <summary>
    /// The structure edits of a flatten, made after the pages: each removed annotation's object references go from their elements'
    /// <c>K</c> (§14.7.5.3), the first of a drawn one replaced by the marked-content item its appearance was drawn in (§14.7.5.2),
    /// and its <c>StructParent</c> entry goes from the parent tree (§14.7.5.4).
    /// </summary>
    private sealed class StructurePlan(
        StructureContext context,
        HashSet<CosDictionary> removed,
        Dictionary<CosDictionary, List<PdfObjectReference>> references,
        Dictionary<CosDictionary, (PdfPage Page, int Mcid)> tagged)
    {
        public void Apply()
        {
            foreach ((CosDictionary annotation, List<PdfObjectReference> held) in references)
            {
                int first = 0;
                if (tagged.TryGetValue(annotation, out (PdfPage Page, int Mcid) drawn))
                {
                    PdfObjectReference owner = held[0];
                    StructureWriter.AddMarkedContent(context, owner.Parent, drawn.Page, null, drawn.Mcid, replacing: owner.Dictionary);
                    first = 1;
                }

                for (int index = first; index < held.Count; index++)
                {
                    StructureWriter.RemoveObjectReference(context, held[index]);
                }
            }

            foreach (CosDictionary annotation in removed)
            {
                StructureWriter.UnlinkRemovedObject(context, annotation);
            }
        }
    }

    /// <summary>Finds the lowest MCID above every one a page's own content uses (§14.7.5.2); forms have MCIDs of their own and are not entered.</summary>
    private sealed class McidScanner : ContentProcessor
    {
        private int _next;

        public override ContentEvents Events => ContentEvents.MarkedContent;

        public static int NextMcid(PdfPage page)
        {
            var scanner = new McidScanner();
            page.ProcessContent(scanner);
            return scanner._next;
        }

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Skip;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context) => Note(mc);

        public override void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context) => Note(mc);

        private void Note(in MarkedContentEvent mc)
        {
            if (mc.ContentStream is null && mc.Mcid is { } mcid and >= 0)
            {
                _next = Math.Max(_next, (int)Math.Min((long)mcid + 1, int.MaxValue));
            }
        }
    }

    /// <summary>The entries a flatten changed, set back in reverse order when it fails part way.</summary>
    private sealed class UndoLog
    {
        private readonly List<(CosDictionary Dictionary, CosName Key, CosObject? Old)> _changes = [];

        public void Set(CosDictionary dictionary, CosName key, CosObject? value)
        {
            _changes.Add((dictionary, key, dictionary.TryGetValue(key, out CosObject? old) ? old : null));
            if (value is null)
            {
                dictionary.Remove(key);
            }
            else
            {
                dictionary[key] = value;
            }
        }

        public void Remove(CosDictionary dictionary, CosName key)
        {
            if (dictionary.ContainsKey(key))
            {
                Set(dictionary, key, null);
            }
        }

        public void Revert()
        {
            for (int index = _changes.Count - 1; index >= 0; index--)
            {
                (CosDictionary dictionary, CosName key, CosObject? old) = _changes[index];
                if (old is null)
                {
                    dictionary.Remove(key);
                }
                else
                {
                    dictionary[key] = old;
                }
            }
        }
    }
}
