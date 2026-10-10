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
            diagnostics.Report(
                DiagnosticCodes.PagesMissing,
                DiagnosticSeverity.Error,
                "The catalog has no Pages entry that resolves to the page tree root; the document has no pages.");
            return [];
        }

        var pages = new List<PdfPage>();
        var visitedReferences = new HashSet<CosReference>();
        var visitedDictionaries = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var rootReference = rootEntry as CosReference;
        if (rootReference is not null)
        {
            visitedReferences.Add(rootReference);
        }

        visitedDictionaries.Add(root);
        var stack = new Stack<Frame>();
        stack.Push(new Frame(root, rootReference, null, null, 0));
        while (stack.TryPop(out Frame frame))
        {
            CheckParent(frame, diagnostics);
            if (IsLeaf(frame, diagnostics))
            {
                var page = new PdfPage(document, frame.Node, frame.Reference, frame.Ancestors);
                CheckPage(page, diagnostics);
                pages.Add(page);
                continue;
            }

            if (frame.Depth >= MaxDepth)
            {
                diagnostics.Report(
                    DiagnosticCodes.PageTreeTooDeep,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The page tree is deeper than {MaxDepth} levels; the deeper nodes are skipped."),
                    objectReference: frame.Reference);
                continue;
            }

            var ancestors = new PageTreeAncestor(frame.Node, frame.Ancestors);
            List<Frame> kids = ReadKids(document, frame, ancestors, visitedReferences, visitedDictionaries, diagnostics);
            for (int index = kids.Count - 1; index >= 0; index--)
            {
                stack.Push(kids[index]);
            }
        }

        if (root.TryGetValue(KnownNames.Count, out CosObject? count)
            && root.TryGetValue(KnownNames.Kids, out _)
            && (document.Resolve(count) is not CosInteger { Value: var stated } || stated != pages.Count))
        {
            diagnostics.Report(
                DiagnosticCodes.PageTreeCountMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The page tree root's Count is not the number of pages found by walking the tree ({pages.Count})."),
                objectReference: rootReference);
        }

        return pages;
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
                DiagnosticCodes.PageTreeKidsInvalid,
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
                    DiagnosticCodes.PageTreeKidNotIndirect,
                    DiagnosticSeverity.Warning,
                    "A Kids entry shall be an indirect reference; it is a direct dictionary, used as is.",
                    objectReference: frame.Reference);
            }

            if (document.Resolve(kid) is not CosDictionary child)
            {
                diagnostics.Report(
                    DiagnosticCodes.PageTreeKidInvalid,
                    DiagnosticSeverity.Error,
                    "A Kids entry does not resolve to a page tree node or page object; it is skipped.",
                    objectReference: reference ?? frame.Reference);
                continue;
            }

            if ((reference is not null && !visitedReferences.Add(reference)) || !visitedDictionaries.Add(child))
            {
                diagnostics.Report(
                    DiagnosticCodes.PageTreeCycle,
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
    private static bool IsLeaf(Frame frame, DiagnosticSink diagnostics)
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

        bool hasKids = frame.Node.ContainsKey(KnownNames.Kids);
        diagnostics.Report(
            DiagnosticCodes.PageTreeNodeTypeInvalid,
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
                DiagnosticCodes.PageTreeParentMismatch,
                DiagnosticSeverity.Warning,
                "The Parent entry does not refer to the page tree node whose Kids list this node; inheritance follows the tree.",
                objectReference: frame.Reference);
        }
    }

    /// <summary>Checks the attributes of a page object (Table 31) and reports each one that will read as its default.</summary>
    private static void CheckPage(PdfPage page, DiagnosticSink diagnostics)
    {
        CosReference? reference = page.Reference;
        switch (page.ReadBox(KnownNames.MediaBox, inheritable: true, out _))
        {
            case PageAttributeState.Absent:
                diagnostics.Report(
                    DiagnosticCodes.PageMediaBoxMissing,
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
                    DiagnosticCodes.PageRotateInvalid,
                    DiagnosticSeverity.Warning,
                    "The page's Rotate shall be an integer; it is a real holding a multiple of 90, used as that integer.",
                    objectReference: reference);
                break;
            case PageAttributeState.Invalid:
                diagnostics.Report(
                    DiagnosticCodes.PageRotateInvalid,
                    DiagnosticSeverity.Warning,
                    "The page's Rotate is not a multiple of 90; 0 is used.",
                    objectReference: reference);
                break;
        }

        switch (page.ReadResources())
        {
            case PageAttributeState.Absent:
                diagnostics.Report(
                    DiagnosticCodes.PageResourcesMissing,
                    DiagnosticSeverity.Warning,
                    "The page has no Resources, on itself or an ancestor; it is read as having none.",
                    objectReference: reference);
                break;
            case PageAttributeState.Invalid:
                diagnostics.Report(
                    DiagnosticCodes.PageResourcesInvalid,
                    DiagnosticSeverity.Error,
                    "The page's Resources is not a dictionary; it is read as having none.",
                    objectReference: reference);
                break;
        }

        if (page.ReadUserUnit(out _) == PageAttributeState.Invalid)
        {
            diagnostics.Report(
                DiagnosticCodes.PageUserUnitInvalid,
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
            DiagnosticCodes.PageBoxInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The page's {key.Value} is not a rectangle of four numbers; {fallback}."),
            objectReference: reference);

    /// <summary>A node waiting to be visited.</summary>
    /// <param name="Node">The node's dictionary.</param>
    /// <param name="Reference">The node's reference, or <see langword="null"/> for a direct dictionary.</param>
    /// <param name="Ancestors">The nodes above it, nearest first; <see langword="null"/> for the root.</param>
    /// <param name="ParentReference">The reference of the node whose <c>Kids</c> listed it.</param>
    /// <param name="Depth">0 for the root.</param>
    private readonly record struct Frame(CosDictionary Node, CosReference? Reference, PageTreeAncestor? Ancestors, CosReference? ParentReference, int Depth);
}
