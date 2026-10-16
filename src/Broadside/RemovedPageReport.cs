using System.Globalization;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// Before a save, reports each destination that shows a page removed through <see cref="PdfPageCollection.RemoveAt"/> (issue #158):
/// outline items, link annotations of the remaining pages and named destinations.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.3.2.2: an explicit destination's first element is the page object to show. A removed page object is a free
/// object (§7.3.10), so the destination is written as one that shows nothing; each is reported as
/// <see cref="DiagnosticCodes.DestinationPageRemoved"/> and left as it is. Only the <c>Dest</c> of an outline item or link and the
/// <c>D</c> of a go-to action (§12.6.4.2) are checked, named destinations resolved; a structure destination (§12.3.2.3) and
/// actions further down a <c>Next</c> chain are not. Nothing is read when no page was removed.
/// </remarks>
internal static class RemovedPageReport
{
    /// <summary>Reports every destination to a removed page.</summary>
    /// <param name="document">The document about to be saved.</param>
    /// <param name="diagnostics">Where to report.</param>
    /// <exception cref="DiagnosticException">In strict mode, for the first destination found.</exception>
    public static void Report(PdfDocument document, DiagnosticSink diagnostics)
    {
        if (document.Pages.RemovedPages is not { Count: > 0 } removed)
        {
            return;
        }

        if (document.Outline is { } outline)
        {
            var pending = new Stack<PdfOutlineItem>(outline.Items.Reverse());
            while (pending.TryPop(out PdfOutlineItem? item))
            {
                if (ShowsRemoved(item.Destination ?? (item.Action as PdfGoToAction)?.Destination, removed))
                {
                    diagnostics.Report(
                        DiagnosticCodes.DestinationPageRemoved,
                        DiagnosticSeverity.Warning,
                        string.Create(CultureInfo.InvariantCulture, $"The outline item \"{item.Title}\" shows a page that was removed from the document; it is saved showing no page."),
                        objectReference: item.Reference ?? outline.Reference);
                }

                for (int index = item.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push(item.Children[index]);
                }
            }
        }

        foreach (PdfPage page in document.Pages)
        {
            foreach (PdfLinkAnnotation link in page.Annotations.OfType<PdfLinkAnnotation>())
            {
                if (ShowsRemoved(link.Destination ?? (link.Action as PdfGoToAction)?.Destination, removed))
                {
                    diagnostics.Report(
                        DiagnosticCodes.DestinationPageRemoved,
                        DiagnosticSeverity.Warning,
                        "A link annotation shows a page that was removed from the document; it is saved showing no page.",
                        objectReference: link.Reference ?? page.Reference);
                }
            }
        }

        if (document.Names?.Dests is { } tree)
        {
            foreach (KeyValuePair<CosString, CosObject> entry in tree)
            {
                ReportNamed(document, entry.Key.DecodeText(), entry.Value, tree.RootReference, removed, diagnostics);
            }
        }

        document.Catalog.TryGetValue(NavigationNames.Dests, out CosObject? legacyEntry);
        if (document.Resolve(legacyEntry) is CosDictionary legacy)
        {
            foreach (KeyValuePair<CosName, CosObject> entry in legacy)
            {
                ReportNamed(document, entry.Key.Value, entry.Value, legacyEntry as CosReference, removed, diagnostics);
            }
        }
    }

    private static void ReportNamed(PdfDocument document, string name, CosObject value, CosReference? holder, IReadOnlySet<CosReference> removed, DiagnosticSink diagnostics)
    {
        CosObject resolved = document.Resolve(value);
        if (resolved is CosDictionary dictionary && dictionary.TryGetValue(NavigationNames.D, out CosObject? d))
        {
            resolved = document.Resolve(d);
        }

        if (resolved is CosArray { Count: > 0 } array && array[0] is CosReference target && removed.Contains(target))
        {
            diagnostics.Report(
                DiagnosticCodes.DestinationPageRemoved,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The named destination \"{name}\" shows a page that was removed from the document; it is saved showing no page."),
                objectReference: value as CosReference ?? holder);
        }
    }

    private static bool ShowsRemoved(PdfDestination? destination, IReadOnlySet<CosReference> removed)
    {
        PdfExplicitDestination? explicitDestination = destination switch
        {
            PdfExplicitDestination value => value,
            PdfNamedDestination named => named.Resolve(),
            _ => null,
        };
        return explicitDestination is { IsRemote: false, Target: CosReference target } && removed.Contains(target);
    }
}
