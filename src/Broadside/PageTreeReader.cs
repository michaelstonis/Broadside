using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Walks a document's page tree into its list of pages, checking each node and page as it goes.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3. Depth first with an explicit stack, in <c>Kids</c> order. Lenient rules, each with a diagnostic: a node
/// without a correct <c>Type</c> is a page when it has no <c>Kids</c> (as pdf.js decides); a node reached twice is skipped (§7.7.3.2
/// forbids it, and it would loop); a kid that is a direct dictionary is accepted; a kid that is not a dictionary is skipped; a
/// <c>Parent</c> that disagrees with the path the node was reached by is reported, and the path wins; a wrong <c>Count</c> is reported.
/// </para>
/// <para>
/// A catalog without a usable <c>Pages</c> entry (§7.7.2) has its pages rebuilt from the page tree nodes and page objects the
/// file holds, with a <c>PagesMissing</c> diagnostic; opening the document has already rebuilt the cross-reference information
/// by scanning when that finds a catalog with a page tree.
/// </para>
/// <para>Each page's attributes are checked here, once, so the <see cref="PdfPage"/> getters can apply defaults silently.</para>
/// </remarks>
internal static class PageTreeReader
{
    /// <summary>The deepest page tree the reader descends into.</summary>
    public const int MaxDepth = 256;

    /// <summary>Reads the pages of <paramref name="document"/>.</summary>
    /// <param name="document">The document.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The pages in page order.</returns>
    public static IReadOnlyList<PdfPage> Read(PdfDocument document, DiagnosticSink diagnostics)
    {
        document.Catalog.TryGetValue(KnownNames.Pages, out CosObject? rootEntry);
        if (document.Resolve(rootEntry) is not CosDictionary root)
        {
            return Recover(document, diagnostics);
        }

        var pages = new List<PdfPage>();
        var walk = new Walk(document, diagnostics);
        var rootReference = rootEntry as CosReference;
        walk.Read(root, rootReference, pages);
        if (root.TryGetValue(KnownNames.Count, out CosObject? count)
            && root.TryGetValue(KnownNames.Kids, out _)
            && (document.Resolve(count) is not CosInteger { Value: var stated } || stated != pages.Count))
        {
            diagnostics.Report(
                PageDiagnostics.PageTreeCountMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The page tree root's Count is not the number of pages found by walking the tree ({pages.Count})."),
                objectReference: rootReference);
        }

        return pages;
    }

    /// <summary>
    /// Rebuilds the list of pages of a document whose catalog has no usable <c>Pages</c> entry (§7.7.2) from the objects the file
    /// holds: first each page tree node no other node is the parent of, walked as a root, in object number order; then each page
    /// object no walk reached, in object number order, inheriting through its <c>Parent</c> chain (§7.7.3.4).
    /// </summary>
    /// <remarks>
    /// The cross-reference information was already rebuilt by scanning when that found a catalog with a page tree, so this runs
    /// only when no catalog in the file has one. In strict mode the missing page tree throws instead.
    /// </remarks>
    private static List<PdfPage> Recover(PdfDocument document, DiagnosticSink diagnostics)
    {
        const string missing = "The catalog has no Pages entry that resolves to the page tree root";
        if (diagnostics.IsStrict)
        {
            diagnostics.Report(DocumentDiagnostics.PagesMissing, DiagnosticSeverity.Error, missing + ".");
        }

        var nodes = new List<(CosDictionary Node, CosReference Reference)>();
        var pageObjects = new List<(CosDictionary Page, CosReference Reference)>();
        foreach (CosReference reference in document.ObjectsInFile())
        {
            if (document.Resolve(reference) is not CosDictionary dictionary || !dictionary.TryGetValue(KnownNames.Type, out CosObject? type))
            {
                continue;
            }

            if (KnownNames.Pages.Equals(type))
            {
                if (!dictionary.TryGetValue(KnownNames.Parent, out CosObject? parent) || document.Resolve(parent) is not CosDictionary)
                {
                    nodes.Add((dictionary, reference));
                }
            }
            else if (KnownNames.Page.Equals(type))
            {
                pageObjects.Add((dictionary, reference));
            }
        }

        var pages = new List<PdfPage>();
        var walk = new Walk(document, diagnostics);
        foreach ((CosDictionary node, CosReference reference) in nodes)
        {
            if (walk.IsUnvisited(node, reference))
            {
                walk.Read(node, reference, pages);
            }
        }

        var added = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach ((CosDictionary dictionary, CosReference reference) in pageObjects)
        {
            if (walk.IsUnvisited(dictionary, reference) && added.Add(dictionary))
            {
                var page = new PdfPage(document, dictionary, reference, AncestorsOf(document, dictionary, reference, diagnostics));
                CheckPage(page, diagnostics);
                pages.Add(page);
            }
        }

        diagnostics.Report(
            DocumentDiagnostics.PagesMissing,
            pages.Count == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            pages.Count == 0
                ? missing + ", and the file holds no page objects; the document has no pages."
                : string.Create(CultureInfo.InvariantCulture, $"{missing}; the {pages.Count} pages are the page objects found in the file, under each page tree node without a parent and then on their own, in object number order."));
        return pages;
    }

    /// <summary>
    /// The chain of dictionaries a page object's <c>Parent</c> entries lead to, nearest first, for inheritance (§7.7.3.4). The chain
    /// stops before a page object: a page is a leaf of the tree (§7.7.3.3), never a node another page inherits from or is listed
    /// under. The stop is reported as <c>PageTreeParentMismatch</c>.
    /// </summary>
    private static PageTreeAncestor? AncestorsOf(PdfDocument document, CosDictionary page, CosReference reference, DiagnosticSink diagnostics)
    {
        var chain = new List<(CosDictionary Node, CosReference? Reference)>();
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { page };
        CosDictionary current = page;
        while (chain.Count < MaxDepth
            && current.TryGetValue(KnownNames.Parent, out CosObject? parent)
            && document.Resolve(parent) is CosDictionary node
            && seen.Add(node))
        {
            if (node.TryGetValue(KnownNames.Type, out CosObject? type) && KnownNames.Page.Equals(type))
            {
                diagnostics.Report(
                    PageDiagnostics.PageTreeParentMismatch,
                    DiagnosticSeverity.Warning,
                    "A Parent entry on the page's chain refers to a page object, not a page tree node; the page inherits nothing from it or past it.",
                    objectReference: reference);
                break;
            }

            chain.Add((node, parent as CosReference));
            current = node;
        }

        PageTreeAncestor? ancestors = null;
        for (int index = chain.Count - 1; index >= 0; index--)
        {
            ancestors = new PageTreeAncestor(chain[index].Node, ancestors, chain[index].Reference);
        }

        return ancestors;
    }

    private static List<Frame> ReadKids(
        PdfDocument document,
        Frame frame,
        PageTreeAncestor ancestors,
        HashSet<CosReference> visitedReferences,
        HashSet<CosDictionary> visitedDictionaries,
        DiagnosticSink diagnostics)
    {
        var kids = new List<Frame>();
        frame.Node.TryGetValue(KnownNames.Kids, out CosObject? kidsEntry);
        if (document.Resolve(kidsEntry) is not CosArray array)
        {
            diagnostics.Report(
                PageDiagnostics.PageTreeKidsInvalid,
                DiagnosticSeverity.Error,
                "A page tree node has no Kids array; it contributes no pages.",
                objectReference: frame.Reference);
            return kids;
        }

        foreach (CosObject kid in array)
        {
            var reference = kid as CosReference;
            if (reference is null && kid is CosDictionary)
            {
                diagnostics.Report(
                    PageDiagnostics.PageTreeKidNotIndirect,
                    DiagnosticSeverity.Warning,
                    "A Kids entry shall be an indirect reference; it is a direct dictionary, used as is.",
                    objectReference: frame.Reference);
            }

            if (document.Resolve(kid) is not CosDictionary child)
            {
                diagnostics.Report(
                    PageDiagnostics.PageTreeKidInvalid,
                    DiagnosticSeverity.Error,
                    "A Kids entry does not resolve to a page tree node or page object; it is skipped.",
                    objectReference: reference ?? frame.Reference);
                continue;
            }

            if ((reference is not null && !visitedReferences.Add(reference)) || !visitedDictionaries.Add(child))
            {
                diagnostics.Report(
                    PageDiagnostics.PageTreeCycle,
                    DiagnosticSeverity.Error,
                    "A page tree node or page is referenced more than once in the page tree; the repeat is skipped.",
                    objectReference: reference ?? frame.Reference);
                continue;
            }

            kids.Add(new Frame(child, reference, ancestors, frame.Reference, frame.Depth + 1));
        }

        return kids;
    }

    /// <summary>Decides whether a node is a page (§7.7.3.3) or an intermediate node (§7.7.3.2), reporting a missing or wrong <c>Type</c>.</summary>
    /// <remarks>
    /// A <c>Kids</c> entry whose value is null, or a reference to an object that does not exist, is an absent entry (§7.3.7,
    /// §7.3.10), so it decides the same way as no <c>Kids</c> at all: a writer that renumbers objects writes such a reference as
    /// null, and the node must read the same before and after.
    /// </remarks>
    private static bool IsLeaf(PdfDocument document, Frame frame, DiagnosticSink diagnostics)
    {
        frame.Node.TryGetValue(KnownNames.Type, out CosObject? type);
        if (KnownNames.Page.Equals(type))
        {
            return true;
        }

        if (KnownNames.Pages.Equals(type))
        {
            return false;
        }

        bool hasKids = frame.Node.TryGetValue(KnownNames.Kids, out CosObject? kids) && document.Resolve(kids) is not CosNull;
        diagnostics.Report(
            PageDiagnostics.PageTreeNodeTypeInvalid,
            DiagnosticSeverity.Warning,
            hasKids
                ? "A page tree node's Type is not /Pages; it has Kids, so it is read as an intermediate node."
                : "A page object's Type is not /Page; it has no Kids, so it is read as a page.",
            objectReference: frame.Reference);
        return !hasKids;
    }

    /// <summary>Reports a <c>Parent</c> that is not the node the page tree reached this node through (§7.7.3.2, Table 30).</summary>
    private static void CheckParent(Frame frame, DiagnosticSink diagnostics)
    {
        if (frame.Ancestors is null || frame.ParentReference is not { } expected)
        {
            return;
        }

        if (!frame.Node.TryGetValue(KnownNames.Parent, out CosObject? parent) || !expected.Equals(parent))
        {
            diagnostics.Report(
                PageDiagnostics.PageTreeParentMismatch,
                DiagnosticSeverity.Warning,
                "The Parent entry does not refer to the page tree node whose Kids list this node; inheritance follows the tree.",
                objectReference: frame.Reference);
        }
    }

    /// <summary>Checks the attributes of a page object (Table 31) and reports each one that will read as its default.</summary>
    internal static void CheckPage(PdfPage page, DiagnosticSink diagnostics)
    {
        CosReference? reference = page.Reference;
        switch (page.ReadBox(KnownNames.MediaBox, inheritable: true, out _))
        {
            case PageAttributeState.Absent:
                diagnostics.Report(
                    PageDiagnostics.PageMediaBoxMissing,
                    DiagnosticSeverity.Error,
                    "The page has no MediaBox, on itself or an ancestor; US Letter [0 0 612 792] is used.",
                    objectReference: reference);
                break;
            case PageAttributeState.Invalid:
                ReportInvalidBox(KnownNames.MediaBox, "US Letter [0 0 612 792] is used", reference, diagnostics);
                break;
        }

        CheckOptionalBox(page, KnownNames.CropBox, inheritable: true, "the media box is used", diagnostics);
        CheckOptionalBox(page, KnownNames.BleedBox, inheritable: false, "the crop box is used", diagnostics);
        CheckOptionalBox(page, KnownNames.TrimBox, inheritable: false, "the crop box is used", diagnostics);
        CheckOptionalBox(page, KnownNames.ArtBox, inheritable: false, "the crop box is used", diagnostics);

        switch (page.ReadRotation(out _))
        {
            case PageAttributeState.Repaired:
                diagnostics.Report(
                    PageDiagnostics.PageRotateInvalid,
                    DiagnosticSeverity.Warning,
                    "The page's Rotate shall be an integer; it is a real holding a multiple of 90, used as that integer.",
                    objectReference: reference);
                break;
            case PageAttributeState.Invalid:
                diagnostics.Report(
                    PageDiagnostics.PageRotateInvalid,
                    DiagnosticSeverity.Warning,
                    "The page's Rotate is not a multiple of 90; 0 is used.",
                    objectReference: reference);
                break;
        }

        switch (page.ReadResources())
        {
            case PageAttributeState.Absent:
                diagnostics.Report(
                    PageDiagnostics.PageResourcesMissing,
                    DiagnosticSeverity.Warning,
                    "The page has no Resources, on itself or an ancestor; it is read as having none.",
                    objectReference: reference);
                break;
            case PageAttributeState.Invalid:
                diagnostics.Report(
                    PageDiagnostics.PageResourcesInvalid,
                    DiagnosticSeverity.Error,
                    "The page's Resources is not a dictionary; it is read as having none.",
                    objectReference: reference);
                break;
        }

        if (page.ReadUserUnit(out _) == PageAttributeState.Invalid)
        {
            diagnostics.Report(
                PageDiagnostics.PageUserUnitInvalid,
                DiagnosticSeverity.Warning,
                "The page's UserUnit is not a positive number; 1.0 is used.",
                objectReference: reference);
        }
    }

    private static void CheckOptionalBox(PdfPage page, CosName key, bool inheritable, string fallback, DiagnosticSink diagnostics)
    {
        if (page.ReadBox(key, inheritable, out _) == PageAttributeState.Invalid)
        {
            ReportInvalidBox(key, fallback, page.Reference, diagnostics);
        }
    }

    private static void ReportInvalidBox(CosName key, string fallback, CosReference? reference, DiagnosticSink diagnostics) =>
        diagnostics.Report(
            PageDiagnostics.PageBoxInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The page's {key.Value} is not a rectangle of four numbers; {fallback}."),
            objectReference: reference);

    /// <summary>One depth-first walk of page tree nodes (§7.7.3), sharing what it has visited across the roots it is given.</summary>
    private sealed class Walk(PdfDocument document, DiagnosticSink diagnostics)
    {
        private readonly HashSet<CosReference> _visitedReferences = [];
        private readonly HashSet<CosDictionary> _visitedDictionaries = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether no walk has reached the node or page yet.</summary>
        public bool IsUnvisited(CosDictionary node, CosReference? reference) =>
            !_visitedDictionaries.Contains(node) && (reference is null || !_visitedReferences.Contains(reference));

        /// <summary>Appends the pages under <paramref name="root"/> to <paramref name="pages"/>, in Kids order.</summary>
        public void Read(CosDictionary root, CosReference? rootReference, List<PdfPage> pages)
        {
            if (rootReference is not null)
            {
                _visitedReferences.Add(rootReference);
            }

            _visitedDictionaries.Add(root);
            var stack = new Stack<Frame>();
            stack.Push(new Frame(root, rootReference, null, null, 0));
            while (stack.TryPop(out Frame frame))
            {
                CheckParent(frame, diagnostics);
                if (IsLeaf(document, frame, diagnostics))
                {
                    var page = new PdfPage(document, frame.Node, frame.Reference, frame.Ancestors);
                    CheckPage(page, diagnostics);
                    pages.Add(page);
                    continue;
                }

                if (frame.Depth >= MaxDepth)
                {
                    diagnostics.Report(
                        PageDiagnostics.PageTreeTooDeep,
                        DiagnosticSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"The page tree is deeper than {MaxDepth} levels; the deeper nodes are skipped."),
                        objectReference: frame.Reference);
                    continue;
                }

                var ancestors = new PageTreeAncestor(frame.Node, frame.Ancestors, frame.Reference);
                List<Frame> kids = ReadKids(document, frame, ancestors, _visitedReferences, _visitedDictionaries, diagnostics);
                for (int index = kids.Count - 1; index >= 0; index--)
                {
                    stack.Push(kids[index]);
                }
            }
        }
    }

    /// <summary>A node waiting to be visited.</summary>
    /// <param name="Node">The node's dictionary.</param>
    /// <param name="Reference">The node's reference, or <see langword="null"/> for a direct dictionary.</param>
    /// <param name="Ancestors">The nodes above it, nearest first; <see langword="null"/> for the root.</param>
    /// <param name="ParentReference">The reference of the node whose <c>Kids</c> listed it.</param>
    /// <param name="Depth">0 for the root.</param>
    private readonly record struct Frame(CosDictionary Node, CosReference? Reference, PageTreeAncestor? Ancestors, CosReference? ParentReference, int Depth);
}
