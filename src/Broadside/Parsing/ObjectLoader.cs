using System.Globalization;
using Broadside.Diagnostics;
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
/// hook 2 decryption; hook 3 cache publication. Issue #39 adds <see cref="XrefEntryKind.Compressed"/> entries in
/// <see cref="LoadCompressed"/>; issue #41 adds recovery when the header at an offset does not match in <see cref="LoadMisplaced"/>.
/// </para>
/// </remarks>
internal sealed class ObjectLoader
{
    /// <summary>How many loads may nest (an indirect <c>Length</c> loads while its stream loads) before the inner one reads null.</summary>
    public const int MaxDepth = 32;

    private readonly PdfSource _source;
    private readonly DiagnosticSink _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="ObjectLoader"/> class.</summary>
    /// <param name="source">The file.</param>
    /// <param name="header">The header; entry offsets are relative to it.</param>
    /// <param name="crossReference">The cross-reference information.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="hooks">The hooks.</param>
    public ObjectLoader(PdfSource source, FileHeader header, CrossReference crossReference, DiagnosticSink diagnostics, ObjectLoaderHooks hooks)
    {
        _source = source;
        Header = header;
        CrossReference = crossReference;
        _diagnostics = diagnostics;
        Hooks = hooks;
    }

    /// <summary>Gets the file header.</summary>
    public FileHeader Header { get; }

    /// <summary>Gets the cross-reference information the loader locates objects with.</summary>
    public CrossReference CrossReference { get; }

    /// <summary>Gets the hooks.</summary>
    public ObjectLoaderHooks Hooks { get; }

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

    /// <summary>Loads an object stored in an object stream (§7.5.7). Issue #39.</summary>
    private static CosNull LoadCompressed(CosReference reference, XrefEntry entry, int depth)
    {
        _ = (reference, entry, depth);
        return CosNull.Instance;
    }

    /// <summary>Handles an entry whose offset does not hold the expected <c>N G obj</c> header. Issue #41 searches for the object.</summary>
    private CosNull LoadMisplaced(in ObjectLoadContext context)
    {
        _diagnostics.Report(
            DiagnosticCodes.XrefEntryOffsetInvalid,
            DiagnosticSeverity.Error,
            "The cross-reference entry's offset does not point at the object's header; read as null.",
            context.Offset,
            context.Reference);
        return CosNull.Instance;
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

        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(window, repairs, lexer.Position, new LengthResolver(this, context));
        CosObject value = parser.ParseObject();
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

        return value;
    }

    /// <summary>Adapts hook 1 to the parser's resolver contract for one object load.</summary>
    private sealed class LengthResolver(ObjectLoader loader, ObjectLoadContext context) : IStreamLengthResolver
    {
        public long? ResolveLength(CosObject lengthEntry) => loader.Hooks.StreamExtent.ResolveLength(lengthEntry, loader, context);
    }
}
