using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>A PDF document: the entry point for reading a file and the root of the document model.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5 (file structure) and §7.7.2 (document catalog). Opening reads the header, the cross-reference information, the
/// trailer and the catalog; every other object, the page tree included, loads on first use. The document is a live view over its
/// COS objects (<see cref="Catalog"/>, <see cref="Trailer"/>, <see cref="Resolve"/>), so a change made through the COS layer is
/// visible through the document model.
/// </para>
/// <para>
/// Reading is lenient by default: deviations are repaired and recorded in <see cref="Diagnostics"/>. In strict mode the first
/// deviation throws a <see cref="DiagnosticException"/>; deviations in objects loaded after open (the page tree, for instance) throw
/// from the member that loads them.
/// </para>
/// <para>Safe for concurrent reads from several threads while nobody changes it; changes are single-threaded.</para>
/// </remarks>
public sealed class PdfDocument : IDisposable
{
    private readonly PdfSource _source;
    private readonly DiagnosticSink _diagnostics;
    private readonly ObjectLoader _loader;
    private readonly StreamDecoder _streams;

    private PdfDocument(
        PdfSource source,
        DiagnosticSink diagnostics,
        ObjectLoader loader,
        StreamDecoder streams,
        CosDictionary catalog,
        IReadOnlyList<PdfRevision> revisions,
        PdfLinearization? linearization)
    {
        _source = source;
        _diagnostics = diagnostics;
        _loader = loader;
        _streams = streams;
        Catalog = catalog;
        Revisions = revisions;
        Linearization = linearization;
        Pages = new PdfPageCollection(() => PageTreeReader.Read(this, diagnostics));
    }

    /// <summary>
    /// Gets the version of the specification the document conforms to: the header's version, or the catalog's <c>Version</c> entry
    /// when that is later.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.5.2 and §7.7.2, Table 29. Read from the catalog on every call. When neither states a valid version, 1.4.
    /// </remarks>
    public PdfVersion Version
    {
        get
        {
            PdfVersion? catalogVersion = ReadCatalogVersion(Catalog, diagnostics: null);
            PdfVersion? headerVersion = _loader.Header.Version;
            return (headerVersion, catalogVersion) switch
            {
                ({ } header, { } catalog) => catalog > header ? catalog : header,
                ({ } header, null) => header,
                (null, { } catalog) => catalog,
                _ => FileHeader.DefaultVersion,
            };
        }
    }

    /// <summary>Gets the document catalog dictionary, the root of the document's object hierarchy.</summary>
    /// <remarks>ISO 32000-2 §7.7.2.</remarks>
    public CosDictionary Catalog { get; }

    /// <summary>Gets the trailer dictionary: the newest trailer, with entries only older trailers hold filled in.</summary>
    /// <remarks>ISO 32000-2 §7.5.5, Table 15.</remarks>
    public CosDictionary Trailer => _loader.CrossReference.Trailer;

    /// <summary>Gets the pages, in page order. The page tree is read on first use.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.</remarks>
    public PdfPageCollection Pages { get; }

    /// <summary>Gets the revisions of the file, oldest first: the original file, then one per incremental update.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.5.6. The document itself always shows the newest revision: for every object number the most recent
    /// cross-reference section that lists it decides, and a number it marks free is deleted even when an older section has the
    /// object. A linearized file without updates has one revision.
    /// </remarks>
    public IReadOnlyList<PdfRevision> Revisions { get; }

    /// <summary>
    /// Gets a value indicating whether the file is linearized: it starts with a linearization parameter dictionary whose file
    /// length (<c>L</c>) is the length of the file.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 Annex F, Table F.1: a file whose length does not match "shall be treated as ordinary PDF file, ignoring
    /// linearization information", which is what appending an update to a linearized file produces (G.7). Lengths count from the
    /// <c>%PDF-</c> header.
    /// </remarks>
    public bool IsLinearized => Linearization is { } linearization && linearization.FileLength == _source.Length - _loader.Header.Offset;

    /// <summary>
    /// Gets the linearization information when the file starts with a valid linearization parameter dictionary, whether or not the
    /// file is still linearized (<see cref="IsLinearized"/>); otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 Annex F, F.3.3.</remarks>
    public PdfLinearization? Linearization { get; }

    /// <summary>Gets the deviations found so far, in the order they were found. Empty for a well-formed file.</summary>
    /// <remarks>
    /// ADR 0005. A snapshot: objects load lazily, so reading more of the document can add diagnostics, which a later call returns.
    /// </remarks>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Snapshot();

    /// <summary>Opens the PDF file at <paramref name="path"/> with default options.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all.</exception>
    /// <remarks>ISO 32000-2 §7.5. The same as <see cref="PdfEngine.Open(string)"/> on an engine with default options.</remarks>
    public static PdfDocument Open(string path) => PdfEngine.Default.Open(path);

    /// <summary>Opens the PDF file at <paramref name="path"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="options">The options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.</exception>
    /// <remarks>ISO 32000-2 §7.5. The same as <see cref="PdfEngine.Open(string)"/> on an engine built from <paramref name="options"/>.</remarks>
    public static PdfDocument Open(string path, PdfOptions options) => new PdfEngine(options).Open(path);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position, with default options.</summary>
    /// <param name="stream">The stream. It is not disposed; keep it open and unchanged until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all.</exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static PdfDocument Open(Stream stream) => PdfEngine.Default.Open(stream);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position.</summary>
    /// <param name="stream">The stream. It is not disposed; keep it open and unchanged until the document is disposed.</param>
    /// <param name="options">The options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.</exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static PdfDocument Open(Stream stream, PdfOptions options) => new PdfEngine(options).Open(stream);

    /// <summary>Opens the PDF file held in <paramref name="bytes"/> with default options.</summary>
    /// <param name="bytes">The file. Not copied: do not change it until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all.</exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static PdfDocument Open(ReadOnlyMemory<byte> bytes) => PdfEngine.Default.Open(bytes);

    /// <summary>Opens the PDF file held in <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The file. Not copied: do not change it until the document is disposed.</param>
    /// <param name="options">The options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.</exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static PdfDocument Open(ReadOnlyMemory<byte> bytes, PdfOptions options) => new PdfEngine(options).Open(bytes);

    /// <summary>Creates a new, empty document with default options.</summary>
    /// <returns>The document.</returns>
    /// <exception cref="NotSupportedException">Always, for now: writing arrives in a later version.</exception>
    public static PdfDocument Create() => PdfEngine.Default.Create();

    /// <summary>Creates a new, empty document.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="NotSupportedException">Always, for now: writing arrives in a later version.</exception>
    public static PdfDocument Create(PdfOptions options) => new PdfEngine(options).Create();

    /// <summary>Returns <paramref name="value"/>, or the indirect object it refers to when it is a <see cref="CosReference"/>.</summary>
    /// <param name="value">A COS object from this document, or <see langword="null"/>.</param>
    /// <returns>
    /// The direct object. A reference to an object that does not exist, and <see langword="null"/>, resolve to
    /// <see cref="CosNull.Instance"/>. Resolving the same reference twice returns the same instance.
    /// </returns>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public CosObject Resolve(CosObject? value) => _loader.Resolve(value);

    /// <summary>Returns the data of <paramref name="stream"/> decoded through the filters its <c>Filter</c> entry names.</summary>
    /// <param name="stream">A stream of this document.</param>
    /// <returns>The decoded data; for a stream without filters, its <see cref="CosStream.EncodedData"/> itself.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.3.8.2 and §7.4. Filters come from the engine's options (<see cref="PdfOptions.UseFilter"/>). In lenient mode a
    /// filter that is unknown or not implemented yet leaves the data as decoded up to it, with a diagnostic; damaged data decodes as
    /// far as it can. Decodes on every call; nothing is cached.
    /// </remarks>
    public ReadOnlyMemory<byte> DecodeStream(CosStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return _streams.Decode(stream);
    }

    /// <summary>Decodes the data of <paramref name="stream"/> through the filters its <c>Filter</c> entry names into <paramref name="output"/>.</summary>
    /// <param name="stream">A stream of this document.</param>
    /// <param name="output">Where to write the decoded data.</param>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while decoding.</exception>
    /// <remarks>ISO 32000-2 §7.3.8.2 and §7.4. As <see cref="DecodeStream(CosStream)"/>, into a caller-provided or pooled buffer.</remarks>
    public void DecodeStream(CosStream stream, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(output);
        _streams.Decode(stream, output);
    }

    /// <inheritdoc/>
    public void Dispose() => _source.Dispose();

    /// <summary>The open path shared by every entry point: header, cross-reference information, trailer, catalog.</summary>
    /// <param name="source">The file; owned by the document, disposed if opening fails.</param>
    /// <param name="configuration">The engine's configuration.</param>
    /// <returns>The document.</returns>
    internal static PdfDocument Read(PdfSource source, EngineConfiguration configuration)
    {
        try
        {
            var diagnostics = new DiagnosticSink(configuration.ReadingMode == PdfReadingMode.Strict, configuration.DiagnosticObserver);
            FileHeader header = FileHeader.Locate(source, diagnostics);
            CrossReference crossReference = CrossReferenceReader.Read(source, header, diagnostics) ?? Reconstruct(diagnostics);
            var loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks());
            var streams = new StreamDecoder(configuration.Filters, configuration.MaxDecodedStreamLength, diagnostics, loader.Resolve);
            IReadOnlyList<PdfRevision> revisions = ReadRevisions(source, crossReference, diagnostics);
            CosDictionary catalog = ReadCatalog(loader, diagnostics);
            PdfLinearization? linearization = LinearizationReader.Read(source, loader, diagnostics);
            return new PdfDocument(source, diagnostics, loader, streams, catalog, revisions, linearization);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Describes each revision: its byte range and its own trailer (§7.5.6).</summary>
    private static PdfRevision[] ReadRevisions(PdfSource source, CrossReference crossReference, DiagnosticSink diagnostics)
    {
        long[] ends = RevisionReader.ReadEnds(source, crossReference, diagnostics);
        var revisions = new PdfRevision[ends.Length];
        for (int index = 0; index < ends.Length; index++)
        {
            revisions[index] = new PdfRevision(index, ends[index], crossReference.Revisions[index].Newest.Trailer);
        }

        return revisions;
    }

    /// <summary>Rebuilds the cross-reference information by scanning the file when it cannot be read (§7.5.4). Issue #41.</summary>
    private static CrossReference Reconstruct(DiagnosticSink diagnostics) =>
        throw new DiagnosticException(diagnostics.Snapshot() is [.., var last]
            ? last
            : new Diagnostic(DiagnosticCodes.StartxrefMissing, DiagnosticSeverity.Error, "The cross-reference information cannot be read."));

    /// <summary>Loads the catalog through the trailer's <c>Root</c> and checks its <c>Type</c> and <c>Version</c> (§7.7.2).</summary>
    private static CosDictionary ReadCatalog(ObjectLoader loader, DiagnosticSink diagnostics)
    {
        CosDictionary trailer = loader.CrossReference.Trailer;
        trailer.TryGetValue(KnownNames.Root, out CosObject? root);
        if (root is CosDictionary)
        {
            diagnostics.Report(
                DiagnosticCodes.RootNotIndirect,
                DiagnosticSeverity.Warning,
                "The trailer's Root entry shall be an indirect reference; it is a direct dictionary.");
        }

        if (loader.Resolve(root) is not CosDictionary catalog)
        {
            diagnostics.Fail(DiagnosticCodes.RootMissing, "The trailer has no Root entry that resolves to the catalog dictionary.");
            return null;
        }

        if (!catalog.TryGetValue(KnownNames.Type, out CosObject? type) || !KnownNames.Catalog.Equals(type))
        {
            diagnostics.Report(
                DiagnosticCodes.CatalogTypeInvalid,
                DiagnosticSeverity.Warning,
                "The catalog dictionary's Type entry shall be /Catalog.",
                objectReference: root as CosReference);
        }

        _ = ReadCatalogVersion(catalog, diagnostics, root as CosReference);
        return catalog;
    }

    /// <summary>Reads the catalog's <c>Version</c> entry; reports deviations when <paramref name="diagnostics"/> is given.</summary>
    private static PdfVersion? ReadCatalogVersion(CosDictionary catalog, DiagnosticSink? diagnostics, CosReference? catalogReference = null)
    {
        if (!catalog.TryGetValue(KnownNames.Version, out CosObject? entry))
        {
            return null;
        }

        if (entry is CosName name && PdfVersion.TryParse(name.Bytes, out PdfVersion version))
        {
            return version;
        }

        if (entry is CosNumber number && PdfVersion.TryFromNumber(number, out version))
        {
            diagnostics?.Report(
                DiagnosticCodes.CatalogVersionNotName,
                DiagnosticSeverity.Warning,
                "The catalog's Version entry shall be a name such as /1.7; it is a number, read as that version.",
                objectReference: catalogReference);
            return version;
        }

        diagnostics?.Report(
            DiagnosticCodes.CatalogVersionInvalid,
            DiagnosticSeverity.Warning,
            "The catalog's Version entry is not a version name such as /1.7; it is ignored.",
            objectReference: catalogReference);
        return null;
    }
}
