using System.Collections.Concurrent;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;

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

    /// <summary>How far before and after a wrong offset the object's header is looked for before the whole file is scanned.</summary>
    private const int NearSearchDistance = 1024;

    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName N = new("N");
    private static readonly CosName First = new("First");

    /// <summary>The object streams this thread is decoding, across loaders, so a container that needs its own member ends.</summary>
    [ThreadStatic]
    private static List<(ObjectLoader Loader, int Number)>? _decoding;

    private readonly PdfSource _source;
    private readonly DiagnosticSink _diagnostics;
    private readonly StreamDecoder _streams;
    private ConcurrentDictionary<int, ObjectStream>? _objectStreams;
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

    /// <summary>Gets the cross-reference information the loader locates objects with.</summary>
    public CrossReference CrossReference { get; }

    /// <summary>Gets the hooks.</summary>
    public ObjectLoaderHooks Hooks { get; }

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

        if (!CrossReference.TryGetEntry(reference.ObjectNumber, out XrefEntry entry))
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

        CosObject loaded;
        ObjectLoadContext context;
        switch (entry.Kind)
        {
            case XrefEntryKind.InUse when entry.Generation == reference.Generation:
                context = new ObjectLoadContext(reference, Header.Offset + entry.Offset, ObjectOrigin.FileBody, depth);
                loaded = ParseIndirectObject(context);
                break;
            case XrefEntryKind.Compressed when reference.Generation == 0:
                return LoadCompressed(reference, entry, depth);
            default:
                return CosNull.Instance;
        }

        loaded = Hooks.Decryptor.Decrypt(loaded, context);
        return Hooks.Cache.Publish(reference, loaded);
    }

    /// <summary>Loads an object stored in an object stream (§7.5.7): container, then the member at the entry's index.</summary>
    private CosObject LoadCompressed(CosReference reference, XrefEntry entry, int depth)
    {
        int containerNumber = (int)entry.Offset;
        if (IsDecoding(containerNumber))
        {
            _diagnostics.Report(
                DiagnosticCodes.ObjectStreamCycle,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"Object stream {containerNumber} needs this object, which it holds, to be decoded; read as null."),
                objectReference: reference);
            return CosNull.Instance;
        }

        ConcurrentDictionary<int, ObjectStream> objectStreams = ObjectStreams;
        ObjectStream container = objectStreams.TryGetValue(containerNumber, out ObjectStream? cached)
            ? cached
            : objectStreams.GetOrAdd(containerNumber, ReadObjectStream(containerNumber, reference, depth));
        CosObject loaded = container.Parse(reference, entry.Generation, _diagnostics, out ReadOnlyMemory<byte>? memberBytes);
        if (memberBytes is { } bytes)
        {
            SourceBytes[reference] = ObjectSourceBytes.InObjectStream(bytes);
        }

        return Hooks.Cache.Publish(reference, loaded);
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

        List<(ObjectLoader Loader, int Number)> decoding = _decoding ??= [];
        decoding.Add((this, number));
        try
        {
            if (Load(reference, depth + 1) is not CosStream stream)
            {
                _diagnostics.Report(
                    DiagnosticCodes.ObjectStreamInvalid,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The cross-reference entry places the object in object {number}, which is not a stream; read as null."),
                    objectReference: member);
                return ObjectStream.Unreadable(reference);
            }

            if (!stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type) || !ObjStm.Equals(type))
            {
                _diagnostics.Report(
                    DiagnosticCodes.ObjectStreamTypeInvalid,
                    DiagnosticSeverity.Warning,
                    "The object stream's Type entry shall be /ObjStm; the stream is read as one.",
                    objectReference: reference);
            }

            CosObject count = Resolve(stream.Dictionary.TryGetValue(N, out CosObject? n) ? n : null, depth + 1);
            CosObject first = Resolve(stream.Dictionary.TryGetValue(First, out CosObject? f) ? f : null, depth + 1);
            return ObjectStream.Read(reference, _streams.Decode(stream), count, first, _diagnostics);
        }
        finally
        {
            decoding.Remove((this, number));
        }
    }

    /// <summary>Gets the decoded object streams by object number, created on first use so a file without them pays nothing.</summary>
    private ConcurrentDictionary<int, ObjectStream> ObjectStreams
    {
        get
        {
            ConcurrentDictionary<int, ObjectStream>? objectStreams = Volatile.Read(ref _objectStreams);
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

    private bool IsDecoding(int containerNumber) => _decoding is { Count: > 0 } decoding && decoding.Contains((this, containerNumber));

    private CosObject Resolve(CosObject? value, int depth) => value is CosReference reference ? Load(reference, depth) : value ?? CosNull.Instance;

    /// <summary>
    /// Handles an entry whose offset does not hold the expected <c>N G obj</c> header: looks for the header near the stated offset,
    /// then anywhere in the file (the newest copy), and parses the object where it is found (issue #41).
    /// </summary>
    private CosObject LoadMisplaced(in ObjectLoadContext context)
    {
        CosReference reference = context.Reference;
        long nearStart = context.Offset - NearSearchDistance;
        bool found = FileScan.Run(_source, nearStart, context.Offset + NearSearchDistance)
            .TryFindObject(reference.ObjectNumber, reference.Generation, context.Offset, out long offset)
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

    /// <summary>Parses <c>N G obj</c> object <c>endobj</c> at the context's offset (§7.3.10).</summary>
    private CosObject ParseIndirectObject(in ObjectLoadContext context)
    {
        ReadOnlySpan<byte> window = _source.GetWindow(context.Offset).Span;
        var lexer = new CosLexer(window);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long number)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || !StructureTokens.TryReadKeyword(ref lexer, "obj"u8)
            || number != context.Reference.ObjectNumber
            || generation != context.Reference.Generation)
        {
            return LoadMisplaced(context);
        }

        lexer.SkipWhitespaceAndComments();
        int bodyStart = lexer.Position;
        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(window, repairs, bodyStart, new LengthResolver(this, context));
        CosObject value = parser.ParseObject();
        int bodyEnd = parser.Position;
        foreach (CosRepair repair in repairs.All)
        {
            _diagnostics.Report(repair.Code, DiagnosticSeverity.Warning, repair.Message, context.Offset + repair.Offset, context.Reference);
        }

        lexer.Position = parser.Position;
        CosToken end = lexer.Next();
        if (!StructureTokens.IsKeyword(window, end, "endobj"u8))
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

    /// <summary>Adapts hook 1 to the parser's resolver contract for one object load.</summary>
    private sealed class LengthResolver(ObjectLoader loader, ObjectLoadContext context) : IStreamLengthResolver
    {
        public long? ResolveLength(CosObject lengthEntry) => loader.Hooks.StreamExtent.ResolveLength(lengthEntry, loader, context);
    }
}
