using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Opening a remote file through HTTP range requests and prefetching its pages (issue #207).</summary>
public sealed partial class PdfDocument
{
    /// <summary>The page tree entries the page tree reader resolves on each node (§7.7.3.2, §7.7.3.3, Table 30 and Table 31).</summary>
    private static readonly CosName[] PageTreeKeys =
    [
        KnownNames.Kids, KnownNames.Count, KnownNames.Type, KnownNames.MediaBox, KnownNames.CropBox, KnownNames.BleedBox,
        KnownNames.TrimBox, KnownNames.ArtBox, KnownNames.Rotate, KnownNames.Resources, KnownNames.UserUnit,
    ];

    /// <summary>
    /// The entries a page's prefetch does not follow: back to the page tree, to other pages' objects, or to data that reading the
    /// page never uses (§7.7.3.3 Table 31: Parent, B, Thumb, PieceInfo, Metadata; §12.5.2 Table 166: P).
    /// </summary>
    private static readonly HashSet<CosName> PrefetchSkippedKeys =
    [
        KnownNames.Parent, new("P"), new("B"), new("Thumb"), new("PieceInfo"), new("Metadata"),
    ];

    /// <summary>The catalog's optional content properties (§8.11.4.2, Table 99), which decide what a page's content shows.</summary>
    private static readonly CosName OCPropertiesKey = new("OCProperties");

    /// <summary>The most bytes the open fetches ahead for a linearized file's first page; what lies beyond is fetched on a miss.</summary>
    private const long MaxFirstPageFetch = 16L << 20;

    /// <summary>
    /// Opens the file at <paramref name="uri"/>: the head first; then, for a linearized file, the rest of its first-page section and
    /// hint stream (Annex G, G.2, issue #208), else the tail; then, in as few round trips as the file's layout allows, the
    /// structure, the catalog and the page tree (not read for a linearized file, whose hint tables locate the pages), fetched
    /// asynchronously so that nothing blocks.
    /// </summary>
    /// <param name="client">The client; not disposed.</param>
    /// <param name="uri">The file.</param>
    /// <param name="configuration">The engine's configuration.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The document.</returns>
    internal static async Task<PdfDocument> OpenRemoteAsync(HttpClient client, Uri uri, EngineConfiguration configuration, CancellationToken cancellationToken)
    {
        PdfSource source = await HttpRangeSource.OpenAsync(client, uri, configuration.RangeSource, configuration.StreamBufferLimit, cancellationToken).ConfigureAwait(false);
        if (source is not HttpRangeSource range)
        {
            return Read(source, configuration);
        }

        PdfDocument? document = null;
        try
        {
            // A linearized file holds everything its first page needs before E, with its hint stream (F.3, G.2); any other file
            // starts at its end (§7.5.5).
            if (HttpRangeSource.TryRun(() => LinearizationReader.ReadParameters(range, FileHeader.Locate(range, new DiagnosticSink(strict: false))), out LinearizationParameters? parameters) is null
                && parameters is not null)
            {
                await range.FetchRangeAsync(0, Math.Min(parameters.FirstPageSectionEnd, MaxFirstPageFetch), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                int tail = HttpRangeSource.HeadLength(configuration.RangeSource);
                await range.FetchRangeAsync(range.Length - tail, tail, cancellationToken).ConfigureAwait(false);
            }

            // Each attempt is a whole open: one that misses is dropped (with what it recorded) and run again once the bytes are
            // here, so the observer sees only the diagnostics of the attempt that completes.
            document = await range.RunAsync(() => ReadAttempt(range, configuration), cancellationToken).ConfigureAwait(false);
            CrossReference crossReference = document._loader.CrossReference;
            range.AddObjectBoundaries(ObjectBoundaries(
                crossReference.IsDeferred ? [crossReference.FirstPageSection!] : crossReference.Sections,
                document._loader.Header.Offset));
            if (await range.RunAsync(() => document.Pages.UsesHints && document.Pages[0] is not null && document.Pages.UsesHints, cancellationToken).ConfigureAwait(false))
            {
                range.AddObjectBoundaries(await range.RunAsync(document.HintBoundaries, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                await document.PrefetchPageTreeAsync(range, cancellationToken).ConfigureAwait(false);
            }

            _ = await range.RunAsync(() => document.Pages.Count, cancellationToken).ConfigureAwait(false);
            return document;
        }
        catch
        {
            if (document is null)
            {
                range.Dispose();
            }
            else
            {
                document.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Runs a synchronous read of the document, fetching asynchronously whatever bytes of a remote file it needs that have not
    /// arrived, and returns what it returns. For a document whose bytes are all at hand, it runs the read once.
    /// </summary>
    /// <typeparam name="T">What the read returns.</typeparam>
    /// <param name="read">
    /// The read: any synchronous use of the document, such as enumerating every page, reading the outline, the structure tree or
    /// the named destinations. It runs again after each fetch, so it must not have side effects beyond reading the document.
    /// </param>
    /// <param name="cancellationToken">Cancels the fetches.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is <see langword="null"/>.</exception>
    /// <exception cref="HttpRequestException">The server answered with an error status, or could not be reached.</exception>
    /// <exception cref="IOException">The server's answers are inconsistent: the file changed while it was read, for instance.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// <para>
    /// Spec #33, viewer capability 5; ISO 32000-2 Annex G. Where <see cref="PdfOptions.SynchronousRangeFetch"/> is off (always on the
    /// browser, which cannot block on HTTP), a synchronous read that needs bytes not fetched yet throws
    /// <see cref="PdfRangeNotLoadedException"/>. This is the asynchronous way to run it: each miss is fetched (up to the end of the
    /// object it falls in) and the read runs again, until it completes. A read that fails on a miss leaves nothing cached.
    /// </para>
    /// <para>
    /// A linearized file's page offset hint table answers a page's index and finds its page object without the rest of the file
    /// (<see cref="PdfPageCollection"/>), so <see cref="PdfPageCollection.PrefetchAsync(int, CancellationToken)"/> suffices for one
    /// page. What the file stores after its pages (Annex F, F.3.10: the page tree, the outline, the structure tree, the name trees)
    /// is reached through its main cross-reference section at the end of the file, which a read through this method fetches when it
    /// needs it.
    /// </para>
    /// </remarks>
    public Task<T> ReadAsync<T>(Func<PdfDocument, T> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_source is HttpRangeSource range)
        {
            return range.RunAsync(() => read(this), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(read(this));
    }

    /// <summary>Fetches the objects reading <paramref name="page"/> needs; nothing for a document whose bytes are all at hand.</summary>
    /// <param name="page">A page of this document.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the objects are cached.</returns>
    internal Task PrefetchAsync(PdfPage page, CancellationToken cancellationToken)
    {
        if (_source is not HttpRangeSource range)
        {
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
        }

        return PrefetchPageAsync(range, page, cancellationToken);
    }

    /// <summary>Gets the page at <paramref name="index"/>, fetching its page object and what reading it needs first.</summary>
    /// <param name="index">The page index.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The page.</returns>
    internal Task<PdfPage> PrefetchPageAsync(int index, CancellationToken cancellationToken)
    {
        if (_source is not HttpRangeSource range)
        {
            return cancellationToken.IsCancellationRequested ? Task.FromCanceled<PdfPage>(cancellationToken) : Task.FromResult(Pages[index]);
        }

        return PrefetchRemotePageAsync(range, index, cancellationToken);
    }

    private async Task<PdfPage> PrefetchRemotePageAsync(HttpRangeSource range, int index, CancellationToken cancellationToken)
    {
        PdfPage page = await range.RunAsync(() => Pages[index], cancellationToken).ConfigureAwait(false);
        await PrefetchPageAsync(range, page, cancellationToken).ConfigureAwait(false);
        return page;
    }

    private static PdfDocument ReadAttempt(HttpRangeSource source, EngineConfiguration configuration)
    {
        if (configuration.DiagnosticObserver is not { } observer)
        {
            return Read(source, configuration);
        }

        var deferred = new DeferredObserver(observer);
        try
        {
            PdfDocument document = Read(source, configuration.WithDiagnosticObserver(deferred.Observe));
            deferred.Release();
            return document;
        }
        catch (Exception exception) when (exception is not PdfRangeNotLoadedException)
        {
            // Strict mode's throw, or a file that cannot be read, is this attempt's outcome: what it recorded is logged.
            deferred.Release();
            throw;
        }
    }

    /// <summary>Where each object and cross-reference section starts, absolute: the ends of the byte ranges a miss fetches up to.</summary>
    private static IEnumerable<long> ObjectBoundaries(IEnumerable<XrefSection> sections, long header)
    {
        foreach (XrefSection section in sections)
        {
            yield return section.Offset;
            foreach (XrefEntry entry in section.Entries.Values)
            {
                if (entry.Kind == XrefEntryKind.InUse)
                {
                    yield return header + entry.Offset;
                }
            }
        }
    }

    /// <summary>
    /// Where each hinted page and shared object group starts and ends, the hint streams, and the main cross-reference section (F.4.2,
    /// F.4.3, Table F.1 <c>H</c> and <c>T</c>), absolute: so a miss in a page fetches that page's range, and no more.
    /// </summary>
    private long[] HintBoundaries()
    {
        var boundaries = new List<long>();
        if (Linearization is not { Hints: { } hints } linearization)
        {
            return [];
        }

        foreach (PdfPageHint page in hints.Pages)
        {
            boundaries.Add(page.Offset);
            boundaries.Add(page.Offset + page.Length);
        }

        foreach (PdfSharedObjectHint group in hints.SharedObjects)
        {
            boundaries.Add(group.Offset);
            boundaries.Add(group.Offset + group.Length);
        }

        long header = _loader.Header.Offset;
        if (linearization.Dictionary.TryGetValue(PdfLinearization.Names.H, out CosObject? h) && h is CosArray array)
        {
            for (int index = 0; index + 1 < array.Count; index += 2)
            {
                if (array[index] is CosInteger { Value: var offset } && array[index + 1] is CosInteger { Value: var length })
                {
                    boundaries.Add(header + offset);
                    boundaries.Add(header + offset + length);
                }
            }
        }

        if (linearization.Dictionary.TryGetValue(PdfLinearization.Names.T, out CosObject? t) && t is CosInteger { Value: var main })
        {
            boundaries.Add(header + main);
        }

        if (_loader.CrossReference.FirstPageSection?.Trailer.TryGetValue(KnownNames.Prev, out CosObject? prev) == true && prev is CosInteger { Value: var previous })
        {
            boundaries.Add(header + previous);
        }

        return [.. boundaries];
    }

    /// <summary>
    /// Fetches every object the page tree reader resolves, a level of the tree at a time, so that walking the tree afterwards misses
    /// nothing (§7.7.3).
    /// </summary>
    private async Task PrefetchPageTreeAsync(HttpRangeSource source, CancellationToken cancellationToken)
    {
        Catalog.TryGetValue(KnownNames.Pages, out CosObject? root);
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        List<CosObject> level = root is null ? [] : [root];
        for (int depth = 0; level.Count > 0 && depth <= PageTreeReader.MaxDepth; depth++)
        {
            CosObject[] nodes = await ResolveAllAsync(source, level, cancellationToken).ConfigureAwait(false);
            var entries = new List<(CosName Key, CosObject Value)>();
            foreach (CosObject node in nodes)
            {
                if (node is CosDictionary dictionary && visited.Add(dictionary))
                {
                    foreach (CosName key in PageTreeKeys)
                    {
                        if (dictionary.TryGetValue(key, out CosObject? value))
                        {
                            entries.Add((key, value));
                        }
                    }
                }
            }

            CosObject[] values = await ResolveAllAsync(source, [.. entries.Select(static entry => entry.Value)], cancellationToken).ConfigureAwait(false);
            level = [];
            var elements = new List<CosObject>();
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] is CosArray array)
                {
                    (entries[index].Key.Equals(KnownNames.Kids) ? level : elements).AddRange(array);
                }
            }

            _ = await ResolveAllAsync(source, elements, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches the objects reachable from <paramref name="page"/>, its inherited resources and the document's optional content
    /// properties, without following the entries in <see cref="PrefetchSkippedKeys"/> or entering another page or page tree node.
    /// </summary>
    private async Task PrefetchPageAsync(HttpRangeSource source, PdfPage page, CancellationToken cancellationToken)
    {
        var visitedReferences = new HashSet<CosReference>();
        var visitedContainers = new HashSet<CosObject>(ReferenceEqualityComparer.Instance) { page.Dictionary };
        var next = new List<CosObject>();
        Expand(page.Dictionary, isRoot: true);
        CosObject? resources = await source.RunAsync(() => (CosObject?)page.Resources, cancellationToken).ConfigureAwait(false);
        Visit(resources);
        Catalog.TryGetValue(OCPropertiesKey, out CosObject? optionalContent);
        Visit(optionalContent);
        while (next.Count > 0)
        {
            CosObject[] level = [.. next];
            next.Clear();
            foreach (CosObject value in await ResolveAllAsync(source, level, cancellationToken).ConfigureAwait(false))
            {
                if (visitedContainers.Add(value))
                {
                    Expand(value, isRoot: false);
                }
            }
        }

        void Visit(CosObject? value)
        {
            switch (value)
            {
                case CosReference reference when visitedReferences.Add(reference):
                    next.Add(reference);
                    break;
                case CosDictionary or CosArray or CosStream when visitedContainers.Add(value):
                    Expand(value, isRoot: false);
                    break;
            }
        }

        void Expand(CosObject container, bool isRoot)
        {
            CosDictionary? dictionary = container switch
            {
                CosStream stream => stream.Dictionary,
                CosDictionary plain => plain,
                _ => null,
            };
            if (dictionary is not null)
            {
                if (!isRoot && dictionary.TryGetValue(KnownNames.Type, out CosObject? type)
                    && (KnownNames.Page.Equals(type) || KnownNames.Pages.Equals(type) || KnownNames.Catalog.Equals(type)))
                {
                    return;
                }

                foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                {
                    if (!PrefetchSkippedKeys.Contains(entry.Key))
                    {
                        Visit(entry.Value);
                    }
                }
            }
            else if (container is CosArray array)
            {
                foreach (CosObject element in array)
                {
                    Visit(element);
                }
            }
        }
    }

    /// <summary>
    /// Resolves each value through the object loader, fetching what the resolutions miss in parallel, a round of requests at a time,
    /// until all are resolved.
    /// </summary>
    private async Task<CosObject[]> ResolveAllAsync(HttpRangeSource source, IReadOnlyList<CosObject> values, CancellationToken cancellationToken)
    {
        var resolved = new CosObject[values.Count];
        List<int> pending = [.. Enumerable.Range(0, values.Count)];
        var misses = new HttpRangeSource.MissTracker();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var missed = new List<int>();
            var fetches = new List<PdfRangeNotLoadedException>();
            foreach (int index in pending)
            {
                CosObject value = values[index];
                if (HttpRangeSource.TryRun(() => Resolve(value), out CosObject result) is { } miss)
                {
                    missed.Add(index);
                    fetches.Add(miss);
                }
                else
                {
                    resolved[index] = result;
                }
            }

            if (fetches.Count > 0)
            {
                misses.Check(fetches[0]);
                await Task.WhenAll(fetches.Select(miss => source.FetchMissAsync(miss, cancellationToken))).ConfigureAwait(false);
            }

            pending = missed;
        }

        return resolved;
    }

    /// <summary>Holds the diagnostics of an open attempt until the attempt completes; drops them with an attempt that misses.</summary>
    private sealed class DeferredObserver(Action<Diagnostic> target)
    {
        private readonly Lock _gate = new();
        private List<Diagnostic>? _held = [];

        public void Observe(Diagnostic diagnostic)
        {
            lock (_gate)
            {
                if (_held is not null)
                {
                    _held.Add(diagnostic);
                    return;
                }
            }

            target(diagnostic);
        }

        public void Release()
        {
            List<Diagnostic> held;
            lock (_gate)
            {
                held = _held ?? [];
                _held = null;
            }

            foreach (Diagnostic diagnostic in held)
            {
                target(diagnostic);
            }
        }
    }
}
