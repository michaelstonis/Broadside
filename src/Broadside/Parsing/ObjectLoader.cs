using System.Collections.Concurrent;
using System.Globalization;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Microsoft.Extensions.Logging;

namespace Broadside.Parsing;

/// <summary>
/// Loads indirect objects on demand: cross-reference entry, then <c>N G obj</c> … <c>endobj</c> at its offset, then the hooks.
/// The single choke point every object of a document is read through.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.10 and §7.5.4. A reference to an object number with no entry, a free entry, or a different generation resolves
/// to <see cref="CosNull"/> without a diagnostic: the specification defines it so.
/// </para>
/// <para>
/// Order for every object (see <see cref="ObjectLoaderHooks"/>): cache lookup; locate; parse, with hook 1 resolving stream extents;
/// hook 2 decryption; hook 3 cache publication. When the header at an entry's offset does not match, <see cref="LoadMisplaced"/>
/// looks for the object near the offset and then in a scan of the whole file (issue #41).
/// </para>
/// <para>
/// An object stored in an object stream (§7.5.7, a <see cref="XrefEntryKind.Compressed"/> entry) is parsed from its container's
/// decoded data, which is decoded once per container and shared (<see cref="ObjectStream"/>). The container is an ordinary object
/// loaded through this loader, hooks included, so it is decrypted as a whole; its members are never decrypted on their own and skip
/// hooks 1 and 2 (§7.6.2: strings in an object stream are not separately encrypted). Members publish to the cache like any object.
/// A member reference with a generation other than 0 resolves to null (§7.5.7: members have generation 0).
/// </para>
/// </remarks>
internal sealed class ObjectLoader
{
    /// <summary>How many loads may nest (an indirect <c>Length</c> loads while its stream loads) before the inner one reads null.</summary>
    public const int MaxDepth = 32;

    /// <summary>The first window a windowed source is asked for per object: most objects are smaller.</summary>
    private const int InitialObjectWindow = 4096;

    /// <summary>The largest window an object is parsed in; an object that does not end within 1 GiB is read as repaired.</summary>
    private const int MaxObjectWindow = 1 << 30;

    /// <summary>How far before and after a wrong offset the object's header is looked for before the whole file is scanned.</summary>
    private const int NearSearchDistance = 1024;

    private readonly PdfSource _source;
    private readonly DiagnosticSink _diagnostics;
    private readonly StreamDecoder _streams;
    private OnceCache<int, ObjectStream?>? _objectStreams;
    private ConcurrentDictionary<CosReference, ObjectSourceBytes>? _sourceBytes;

    /// <summary>Initializes a new instance of the <see cref="ObjectLoader"/> class.</summary>
    /// <param name="source">The file.</param>
    /// <param name="header">The header; entry offsets are relative to it.</param>
    /// <param name="crossReference">The cross-reference information.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="hooks">The hooks.</param>
    /// <param name="streams">The filter pipeline object streams are decoded with.</param>
    public ObjectLoader(
        PdfSource source,
        FileHeader header,
        CrossReference crossReference,
        DiagnosticSink diagnostics,
        ObjectLoaderHooks hooks,
        StreamDecoder streams)
    {
        _source = source;
        _streams = streams;
        Header = header;
        CrossReference = crossReference;
        _diagnostics = diagnostics;
        Hooks = hooks;
        Scan = new Lazy<FileScan>(() => FileScan.Run(source), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the file header.</summary>
    public FileHeader Header { get; }

    /// <summary>Gets the filter pipeline the document's streams are decoded with.</summary>
    public StreamDecoder Streams => _streams;

    /// <summary>Gets the cross-reference information the loader locates objects with.</summary>
    public CrossReference CrossReference { get; }

    /// <summary>Gets the hooks.</summary>
    public ObjectLoaderHooks Hooks { get; }

    /// <summary>Gets the logger loads are traced through (<see cref="ObjectLog"/>), or <see langword="null"/>.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Gets the scan of the whole file that objects whose entries are wrong are searched in, run on first use (issue #41). The open
    /// path shares one scan between cross-reference repair and the loader.
    /// </summary>
    public Lazy<FileScan> Scan { get; init; }

    /// <summary>
    /// Finds the source bytes of an object this loader has loaded and read without repairs, so a writer can copy them instead of
    /// re-serializing the object (issue #44).
    /// </summary>
    /// <param name="reference">The object's number and generation.</param>
    /// <param name="bytes">Where the object's bytes are.</param>
    /// <returns><see langword="true"/> when the object was loaded, read without any repair, and its bytes are known.</returns>
    public bool TryGetSourceBytes(CosReference reference, out ObjectSourceBytes bytes)
    {
        if (Volatile.Read(ref _sourceBytes) is { } sourceBytes)
        {
            return sourceBytes.TryGetValue(reference, out bytes);
        }

        bytes = default;
        return false;
    }

    /// <summary>Returns <paramref name="value"/>, or the object it refers to when it is an indirect reference.</summary>
    /// <param name="value">A direct object, a reference, or <see langword="null"/> for an absent entry.</param>
    /// <returns>The direct object; <see cref="CosNull"/> for an absent entry or a reference to nothing.</returns>
    public CosObject Resolve(CosObject? value) => value switch
    {
        null => CosNull.Instance,
        CosReference reference => Load(reference),
        _ => value,
    };

    /// <summary>Loads the object <paramref name="reference"/> names.</summary>
    /// <param name="reference">The object's number and generation.</param>
    /// <returns>The object, or <see cref="CosNull"/>.</returns>
    public CosObject Load(CosReference reference) => Load(reference, depth: 0);

    /// <summary>Loads the object <paramref name="reference"/> names, <paramref name="depth"/> loads deep.</summary>
    /// <param name="reference">The object's number and generation.</param>
    /// <param name="depth">How many loads are in progress beneath this one.</param>
    /// <returns>The object, or <see cref="CosNull"/>.</returns>
    public CosObject Load(CosReference reference, int depth)
    {
        if (Hooks.Cache.TryGet(reference, out CosObject? cached))
        {
            return cached;
        }

        if (!CrossReference.TryGetEntry(reference.ObjectNumber, out XrefEntry entry)
            || entry.Kind switch
            {
                XrefEntryKind.InUse => entry.Generation != reference.Generation,
                XrefEntryKind.Compressed => reference.Generation != 0,
                _ => true,
            })
        {
            return CosNull.Instance;
        }

        if (depth > MaxDepth)
        {
            _diagnostics.Report(
                DiagnosticCodes.ReferenceChainTooDeep,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"Loading the object needs more than {MaxDepth} nested loads; read as null."),
                objectReference: reference);
            return CosNull.Instance;
        }

        // Hook 3 runs the load at most once per object, however many threads ask (issue #45).
        return Hooks.Cache.GetOrLoad(
            reference,
            (Loader: this, Entry: entry, Depth: depth),
            static (reference, state) => state.Loader.LoadUncached(reference, state.Entry, state.Depth),
            static (reference, state) => state.Loader.ReportCycle(reference, state.Entry));
    }

    /// <summary>Locates and parses an object nobody has loaded yet, then runs hook 2.</summary>
    private Created<CosObject> LoadUncached(CosReference reference, XrefEntry entry, int depth)
    {
        if (entry.Kind == XrefEntryKind.Compressed)
        {
            return LoadCompressed(reference, entry, depth);
        }

        var context = new ObjectLoadContext(reference, Header.Offset + entry.Offset, ObjectOrigin.FileBody, depth);

        // Offset 0 is the header, never an object (an object right after it would otherwise parse from there, past the comment).
        CosObject loaded = entry.Offset == 0 ? LoadMisplaced(context) : ParseIndirectObject(context);
        if (Logger is { } logger)
        {
            ObjectLog.ObjectParsed(logger, reference.ObjectNumber, reference.Generation, context.Offset, objectStream: null);
        }

        return new(Hooks.Decryptor.Decrypt(loaded, context));
    }

    /// <summary>Reads as null an object whose loading needs the object itself, which the cache detected; the null is not cached.</summary>
    private CosNull ReportCycle(CosReference reference, XrefEntry entry)
    {
        if (entry.Kind == XrefEntryKind.Compressed)
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectStreamCycle,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"Object stream {entry.Offset} needs this object, which it holds, to be decoded; read as null."),
                objectReference: reference);
        }
        else
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectReferenceCycle,
                DiagnosticSeverity.Error,
                "Loading the object needs the object itself (a stream's Length refers back to it); read as null where it is needed.",
                objectReference: reference);
        }

        return CosNull.Instance;
    }

    /// <summary>Loads an object stored in an object stream (§7.5.7): container, then the member at the entry's index.</summary>
    private Created<CosObject> LoadCompressed(CosReference reference, XrefEntry entry, int depth)
    {
        int containerNumber = (int)entry.Offset;
        ObjectStream? container = ObjectStreams.GetOrCreate(
            containerNumber,
            (Loader: this, Member: reference, Depth: depth),
            static (number, state) => new Created<ObjectStream?>(state.Loader.ReadObjectStream(number, state.Member, state.Depth)),
            static (_, _) => null);
        if (container is null)
        {
            // The container is being decoded further up this thread's stack (its N, First, Filter or Length is one of its own
            // members) or by a thread waiting for this one. The null depends on how the member was reached: not cached.
            ReportCycle(reference, entry);
            return new(CosNull.Instance, Keep: false);
        }

        CosObject loaded = container.Parse(reference, entry.Generation, _diagnostics, out ReadOnlyMemory<byte>? memberBytes);
        if (memberBytes is { } bytes)
        {
            SourceBytes[reference] = ObjectSourceBytes.InObjectStream(bytes);
        }

        if (Logger is { } logger)
        {
            ObjectLog.ObjectParsed(logger, reference.ObjectNumber, reference.Generation, offset: null, containerNumber);
        }

        return new(loaded);
    }

    /// <summary>Loads and decodes object stream <paramref name="number"/> and reads its header (§7.5.7, Table 16).</summary>
    private ObjectStream ReadObjectStream(int number, CosReference member, int depth)
    {
        var reference = new CosReference(number, 0);
        if (CrossReference.TryGetEntry(number, out XrefEntry entry) && entry.Kind == XrefEntryKind.Compressed)
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectStreamNested,
                DiagnosticSeverity.Error,
                "The object stream holding this object is itself listed as stored in an object stream, which a stream cannot be; read as null.",
                objectReference: member);
            return ObjectStream.Unreadable(reference);
        }

        if (Load(reference, depth + 1) is not CosStream stream)
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectStreamInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The cross-reference entry places the object in object {number}, which is not a stream; read as null."),
                objectReference: member);
            return ObjectStream.Unreadable(reference);
        }

        if (!stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type) || !KnownNames.ObjStm.Equals(type))
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectStreamTypeInvalid,
                DiagnosticSeverity.Warning,
                "The object stream's Type entry shall be /ObjStm; the stream is read as one.",
                objectReference: reference);
        }

        CosObject count = Resolve(stream.Dictionary.TryGetValue(KnownNames.N, out CosObject? n) ? n : null, depth + 1);
        CosObject first = Resolve(stream.Dictionary.TryGetValue(KnownNames.First, out CosObject? f) ? f : null, depth + 1);
        ObjectStream container = ObjectStream.Read(reference, _streams.Decode(stream), count, first, _diagnostics);
        if (Logger is { } logger)
        {
            ObjectLog.ObjectStreamDecoded(logger, number, container.ObjectNumbers.Count);
        }

        return container;
    }

    /// <summary>Gets the decoded object streams by object number, created on first use so a file without them pays nothing.</summary>
    private OnceCache<int, ObjectStream?> ObjectStreams
    {
        get
        {
            OnceCache<int, ObjectStream?>? objectStreams = Volatile.Read(ref _objectStreams);
            return objectStreams ?? Interlocked.CompareExchange(ref _objectStreams, new(), null) ?? _objectStreams;
        }
    }

    /// <summary>Gets the recorded source bytes by object, created on first use.</summary>
    private ConcurrentDictionary<CosReference, ObjectSourceBytes> SourceBytes
    {
        get
        {
            ConcurrentDictionary<CosReference, ObjectSourceBytes>? sourceBytes = Volatile.Read(ref _sourceBytes);
            return sourceBytes ?? Interlocked.CompareExchange(ref _sourceBytes, new(), null) ?? _sourceBytes;
        }
    }

    private CosObject Resolve(CosObject? value, int depth) => value is CosReference reference ? Load(reference, depth) : value ?? CosNull.Instance;

    /// <summary>
    /// Handles an entry whose offset does not hold the expected <c>N G obj</c> header: looks for the header near the stated offset,
    /// then anywhere in the file (the newest copy), and parses the object where it is found (issue #41). An entry at offset 0 (the
    /// header) says nothing about where the object is: only the newest copy in the file is looked for.
    /// </summary>
    private CosObject LoadMisplaced(in ObjectLoadContext context)
    {
        CosReference reference = context.Reference;
        long nearStart = context.Offset - NearSearchDistance;
        bool atHeader = context.Offset == Header.Offset;
        bool found = (!atHeader && FileScan.Run(_source, nearStart, context.Offset + NearSearchDistance)
                .TryFindObject(reference.ObjectNumber, reference.Generation, context.Offset, out long offset))
            || Scan.Value.TryFindObject(reference.ObjectNumber, reference.Generation, near: null, out offset);
        if (!found || offset == context.Offset)
        {
            _diagnostics.Report(
                DiagnosticCodes.XrefEntryOffsetInvalid,
                DiagnosticSeverity.Error,
                "The cross-reference entry's offset does not point at the object's header, and the file holds no such header; read as null.",
                context.Offset,
                reference);
            return CosNull.Instance;
        }

        _diagnostics.Report(
            DiagnosticCodes.XrefEntryOffsetInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The cross-reference entry's offset does not point at the object's header; the object is read from offset {offset}, where its header is."),
            context.Offset,
            reference);
        return ParseIndirectObject(context with { Offset = offset });
    }

    /// <summary>
    /// Parses <c>N G obj</c> object <c>endobj</c> at the context's offset (§7.3.10). A source that reads in windows (a stream) is
    /// asked for a small window first and a larger one while the object does not end inside it.
    /// </summary>
    private CosObject ParseIndirectObject(in ObjectLoadContext context)
    {
        long available = _source.Length - context.Offset;
        int minimum = Math.Min(InitialObjectWindow, _source.InitialWindow);
        while (true)
        {
            ReadOnlyMemory<byte> window = _source.GetWindow(context.Offset, minimum);
            bool truncated = window.Length < available;
            if (ParseIndirectObject(context, window.Span, final: !truncated || window.Length >= MaxObjectWindow) is { } value)
            {
                return value;
            }

            minimum = (int)Math.Min(MaxObjectWindow, (long)window.Length * 2);
        }
    }

    /// <summary>
    /// Parses the object in <paramref name="window"/>; returns <see langword="null"/>, reporting nothing, when it does not end inside a
    /// window that is not <paramref name="final"/>.
    /// </summary>
    private CosObject? ParseIndirectObject(in ObjectLoadContext context, ReadOnlySpan<byte> window, bool final)
    {
        var lexer = new CosLexer(window);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long number)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || !StructureTokens.TryReadKeyword(ref lexer, "obj"u8)
            || number != context.Reference.ObjectNumber
            || generation != context.Reference.Generation)
        {
            // A header cut off by the end of a window that is not final is read again in a larger one.
            return !final && lexer.Position >= window.Length ? null : LoadMisplaced(context);
        }

        lexer.SkipWhitespaceAndComments();
        int bodyStart = lexer.Position;
        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(window, repairs, bodyStart, new StreamLengthResolver(this, context), new StreamDataFactory(_source, context.Offset));
        CosObject value = parser.ParseObject();
        int bodyEnd = parser.Position;
        lexer.Position = parser.Position;
        CosToken end = lexer.Next();
        bool ended = StructureTokens.IsKeyword(window, end, "endobj"u8);
        // Not final: read again in a larger window unless the object, and the token after it, end inside this one.
        if (!final && (!ended || repairs.Count > 0 || end.End >= window.Length))
        {
            return null;
        }

        foreach (CosRepair repair in repairs.All)
        {
            _diagnostics.Report(repair.Code, DiagnosticSeverity.Warning, repair.Message, context.Offset + repair.Offset, context.Reference);
        }

        if (!ended)
        {
            _diagnostics.Report(
                DiagnosticCodes.MissingEndobj,
                DiagnosticSeverity.Warning,
                "The object is not followed by the endobj keyword; it ends here.",
                context.Offset + end.Start,
                context.Reference);
        }
        else if (repairs.Count == 0)
        {
            // Only bytes read without any repair may be copied by a writer; a repaired object is re-serialized (issue #44).
            SourceBytes[context.Reference] = ObjectSourceBytes.InFile(context.Offset + bodyStart, bodyEnd - bodyStart);
        }

        return value;
    }

    /// <summary>Keeps a parsed stream's data in the source: the parser's window starts at <paramref name="windowOffset"/>.</summary>
    private sealed class StreamDataFactory(PdfSource source, long windowOffset) : IStreamDataFactory
    {
        public CosStream CreateStream(CosDictionary dictionary, int start, int length) => source.CreateStream(dictionary, windowOffset + start, length);
    }
}
