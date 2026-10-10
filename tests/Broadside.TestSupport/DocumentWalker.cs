using System.Buffers;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>What one <see cref="DocumentWalker.Walk"/> pass read.</summary>
/// <param name="Pages">The page count.</param>
/// <param name="Objects">How many distinct indirect objects resolved to something other than null.</param>
/// <param name="Streams">How many distinct streams were decoded through the filter pipeline.</param>
/// <param name="DecodedBytes">The total length of the decoded stream data.</param>
public sealed record DocumentWalkResult(int Pages, int Objects, int Streams, long DecodedBytes);

/// <summary>
/// Reads everything the document model exposes, so that "the file opens" means more than "the trailer parsed" (objects load
/// lazily, issue #45): version, trailer, revisions, linearization and hint tables, security, every page's boxes, rotation, user unit
/// and resources, the outline and every named destination (issue #70), every action through <see cref="ActionWalker"/> (issue #73), every annotation with its appearances (issue #71), the interactive form through <see cref="FormWalker"/> (issue #74), every indirect object reachable from the trailer and every object number below the trailer's <c>Size</c>, and
/// every stream decoded through the filter pipeline (image filters that are not implemented yet end in a diagnostic, not an
/// exception). Used by the real-world corpus gate (issue #47) and meant to be shared with the open-and-walk fuzz target and
/// benchmark (issue #48): link this file as source there, as <see cref="CorpusLocator"/> is. Public API only.
/// </summary>
/// <remarks>ISO 32000-2 §7 (file structure, objects, filters, encryption), §7.7.3 (page tree).</remarks>
public static class DocumentWalker
{
    /// <summary>The highest object number resolved by number alone; a damaged trailer may claim any <c>Size</c>.</summary>
    public const int MaxObjectNumber = 8_388_607;

    private static readonly CosName SizeKey = new("Size");

    private static readonly CosName DestsKey = new("Dests");

    /// <summary>Walks <paramref name="document"/>. Every exception the library throws propagates to the caller.</summary>
    /// <param name="document">An open document.</param>
    /// <returns>What was read.</returns>
    public static DocumentWalkResult Walk(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _ = document.Version;
        _ = document.Catalog.Count;
        _ = document.IsEncrypted;
        _ = (document.Security?.Revision, document.Permissions);
        foreach (PdfRevision revision in document.Revisions)
        {
            _ = (revision.Index, revision.Length, revision.Trailer.Count);
        }

        _ = document.IsLinearized;
        if (document.Linearization is { } linearization)
        {
            _ = (linearization.FileLength, linearization.PageCount, linearization.FirstPageObjects.Count);
            _ = (linearization.Hints?.Pages.Count, linearization.Hints?.SharedObjects.Count);
        }

        int pages = 0;
        foreach (PdfPage page in document.Pages)
        {
            pages++;
            _ = (page.MediaBox, page.CropBox, page.BleedBox, page.TrimBox, page.ArtBox, page.Rotation, page.UserUnit, page.Resources?.Count);
        }

        ReadCatalogEssentials(document);
        WalkNavigation(document);
        _ = ActionWalker.Walk(document);
        _ = AnnotationWalker.Walk(document);
        _ = FormWalker.Walk(document);

        var walk = new GraphWalk(document);
        walk.Visit(document.Trailer);
        long size = document.Trailer.TryGetValue(SizeKey, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
        for (int number = 1; number < Math.Min(size, MaxObjectNumber + 1L); number++)
        {
            walk.Visit(new CosReference(number, 0));
        }

        return new DocumentWalkResult(pages, walk.Objects, walk.Streams, walk.DecodedBytes);
    }

    /// <summary>
    /// Reads the outline (every item's title, style, destination and action, named destinations resolved) and every named
    /// destination of the name dictionary's Dests tree and the catalog's Dests dictionary (ISO 32000-2 §12.3).
    /// </summary>
    private static void WalkNavigation(PdfDocument document)
    {
        var pending = new Stack<PdfOutlineItem>(document.Outline?.Items ?? []);
        while (pending.TryPop(out PdfOutlineItem? item))
        {
            _ = (item.Title, item.Color, item.IsBold, item.IsOpen, item.StructureElement);
            WalkDestination(item.Destination);
            if (item.Action is PdfGoToAction goTo)
            {
                WalkDestination(goTo.Destination);
                WalkDestination(goTo.StructureDestination);
            }
            else if (item.Action is PdfUriAction uri)
            {
                _ = (uri.Uri, uri.IsMap);
            }

            foreach (PdfOutlineItem child in item.Children)
            {
                pending.Push(child);
            }
        }

        if (document.Names?.Dests is { } tree)
        {
            foreach (KeyValuePair<CosString, CosObject> entry in tree)
            {
                WalkDestination(document.GetNamedDestination(entry.Key));
            }
        }

        if (document.Resolve(document.Catalog.TryGetValue(DestsKey, out CosObject? dests) ? dests : null) is CosDictionary legacy)
        {
            foreach (CosName name in legacy.Keys)
            {
                WalkDestination(document.GetNamedDestination(name));
            }
        }
    }

    private static void WalkDestination(PdfDestination? destination)
    {
        PdfExplicitDestination? explicitDestination = destination switch
        {
            PdfNamedDestination named => named.Resolve(),
            PdfExplicitDestination value => value,
            _ => null,
        };
        if (explicitDestination is not null)
        {
            _ = (explicitDestination.PageIndex, explicitDestination.TargetKind, explicitDestination.View, explicitDestination.Left,
                explicitDestination.Top, explicitDestination.Right, explicitDestination.Bottom, explicitDestination.Zoom, explicitDestination.IsValid);
        }
    }

    /// <summary>
    /// Reads the document-level entries (issue #69): version, extensions, requirements, layout, mode, viewer preferences, language,
    /// every page label, the Info dictionary, the XMP packet and every property in it, the resolved properties, the file identifier.
    /// </summary>
    private static void ReadCatalogEssentials(PdfDocument document)
    {
        _ = (document.HeaderVersion, document.CatalogVersion, document.PageLayout, document.PageMode, document.Language, document.FileIdentifier);
        foreach (PdfDeveloperExtension extension in document.Extensions)
        {
            _ = (extension.BaseVersion, extension.ExtensionLevel, extension.Url, extension.ExtensionRevision);
        }

        foreach (PdfRequirement requirement in document.Requirements)
        {
            _ = (requirement.RequirementType, requirement.Penalty, requirement.Handlers.Select(handler => handler.Script).ToList());
        }

        if (document.ViewerPreferences is { } preferences)
        {
            _ = (preferences.HideToolbar, preferences.HideMenubar, preferences.HideWindowUI, preferences.FitWindow, preferences.CenterWindow);
            _ = (preferences.DisplayDocTitle, preferences.NonFullScreenPageMode, preferences.Direction, preferences.ViewArea, preferences.ViewClip);
            _ = (preferences.PrintArea, preferences.PrintClip, preferences.PrintScaling, preferences.Duplex, preferences.PickTrayByPdfSize);
            _ = (preferences.PrintPageRange, preferences.NumCopies, preferences.Enforce);
        }

        _ = document.PageLabels?.GetLabels();
        if (document.Information is { } info)
        {
            _ = (info.Title, info.Author, info.Subject, info.Keywords, info.Creator, info.Producer, info.CreationDate, info.ModificationDate, info.Trapped);
        }

        if (document.Metadata?.Packet is { } packet)
        {
            _ = (packet.Title, packet.Creators, packet.Subjects, packet.CreateDate, packet.ModifyDate, packet.PdfAPart, packet.PdfUAPart);
        }

        PdfDocumentProperties properties = document.Properties;
        _ = (properties.Title, properties.Author, properties.CreationDate, properties.ModificationDate);
    }

    /// <summary>An iterative depth-first walk (no recursion: real files nest deeply) over references, containers and streams.</summary>
    private sealed class GraphWalk(PdfDocument document)
    {
        private readonly HashSet<CosReference> _references = [];
        private readonly HashSet<CosObject> _containers = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<CosObject> _pending = new();
        private readonly DiscardingWriter _sink = new();

        public int Objects { get; private set; }

        public int Streams { get; private set; }

        public long DecodedBytes => _sink.Written;

        public void Visit(CosObject root)
        {
            _pending.Push(root);
            while (_pending.TryPop(out CosObject? value))
            {
                switch (value)
                {
                    case CosReference reference:
                        if (_references.Add(reference))
                        {
                            CosObject resolved = document.Resolve(reference);
                            if (resolved is not CosNull)
                            {
                                Objects++;
                                _pending.Push(resolved);
                            }
                        }

                        break;
                    case CosDictionary dictionary when _containers.Add(dictionary):
                        foreach (KeyValuePair<CosName, CosObject> item in dictionary)
                        {
                            _pending.Push(item.Value);
                        }

                        break;
                    case CosArray array when _containers.Add(array):
                        foreach (CosObject item in array)
                        {
                            _pending.Push(item);
                        }

                        break;
                    case CosStream stream when _containers.Add(stream):
                        Streams++;
                        document.DecodeStream(stream, _sink);
                        _pending.Push(stream.Dictionary);
                        break;
                    default:
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Counts what the filter pipeline writes and keeps none of it, so decoding a large file holds no decoded data. The scratch
    /// buffer is per thread and outlives the walk, so the open-and-walk benchmark's <c>Allocated</c> shows the library's
    /// allocations, not the walker's.
    /// </summary>
    private sealed class DiscardingWriter : IBufferWriter<byte>
    {
        [ThreadStatic]
        private static byte[]? _scratch;

        public long Written { get; private set; }

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => Buffer(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => Buffer(sizeHint);

        private static byte[] Buffer(int sizeHint)
        {
            if (_scratch is null || sizeHint > _scratch.Length)
            {
                _scratch = new byte[Math.Max(sizeHint, 64 * 1024)];
            }

            return _scratch;
        }
    }
}
