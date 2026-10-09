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
/// and resources, every indirect object reachable from the trailer and every object number below the trailer's <c>Size</c>, and
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

        var walk = new GraphWalk(document);
        walk.Visit(document.Trailer);
        long size = document.Trailer.TryGetValue(SizeKey, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
        for (int number = 1; number < Math.Min(size, MaxObjectNumber + 1L); number++)
        {
            walk.Visit(new CosReference(number, 0));
        }

        return new DocumentWalkResult(pages, walk.Objects, walk.Streams, walk.DecodedBytes);
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

    /// <summary>Counts what the filter pipeline writes and keeps none of it, so decoding a large file holds no decoded data.</summary>
    private sealed class DiscardingWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[64 * 1024];

        public long Written { get; private set; }

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => Buffer(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => Buffer(sizeHint);

        private byte[] Buffer(int sizeHint)
        {
            if (sizeHint > _buffer.Length)
            {
                _buffer = new byte[sizeHint];
            }

            return _buffer;
        }
    }
}
