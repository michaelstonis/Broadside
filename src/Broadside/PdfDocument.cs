using System.Buffers;
using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security;
using Broadside.Structure;
using Broadside.Writing;

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
/// from the member that loads them, so strict validation at open is not exhaustive.
/// </para>
/// <para>
/// Repairs (ISO 32000-2 §7.5 describes the structure but not its repair; ADR 0005): a wrong <c>startxref</c> or <c>Prev</c> offset
/// reads the nearest cross-reference section; an entry whose offset does not hold its object is looked up near that offset, then
/// anywhere in the file; a missing or unusable cross-reference table or trailer is rebuilt by scanning the file for object headers,
/// object streams and trailers, and the catalog is found by its type when no trailer leads to it; a wrong stream <c>Length</c> is
/// recovered from <c>endstream</c> (or <c>endobj</c>), without changing the <c>Length</c> entry; a missing <c>endobj</c> ends the
/// object at the next keyword.
/// </para>
/// <para>
/// Objects load lazily through the cross-reference table (§7.5.4: it "permits random access to indirect objects ... so that the
/// entire PDF file need not be read"), are parsed once, and are kept: every reference to an object resolves to the same instance.
/// A file opened from a path is memory-mapped and a seekable stream is read in place, so memory grows with the objects used, not
/// with the file: stream data stays in the file and is read each time it is asked for (<see cref="CosStream.EncodedData"/>). A
/// 2 GiB file opens and reads its last page in a few megabytes of managed memory. A non-seekable stream is copied when opened.
/// </para>
/// <para>
/// <b>Concurrency contract.</b> While nobody mutates the document, any number of threads may read it at the same time: resolve
/// objects, walk pages, decode streams, read diagnostics, render. Each object is still parsed once, and every thread sees the same
/// instance. Mutation (changing a COS object reachable from the document) is single-threaded and the caller's responsibility to
/// synchronize: no read may run while a mutation runs, and reading concurrently with a mutation is undefined. Disposing the
/// document while another thread reads it is a caller error too; reads after <see cref="Dispose"/> throw
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public sealed partial class PdfDocument : IDisposable
{
    private static readonly PdfVersion Pdf20 = new(2, 0);

    private readonly PdfSource _source;
    private readonly DiagnosticSink _diagnostics;
    private readonly ObjectLoader _loader;
    private readonly StreamDecoder _streams;
    private readonly ConditionalWeakTable<CosDictionary, PdfFont> _fonts = [];
    private readonly ConditionalWeakTable<CosDictionary, NameTreeReader> _nameTrees = [];
    private readonly ConditionalWeakTable<CosDictionary, NumberTreeReader> _numberTrees = [];
    private StructureContext? _structure;
    private PdfOptionalContentProperties? _optionalContent;

    private PdfDocument(
        PdfSource source,
        DiagnosticSink diagnostics,
        ObjectLoader loader,
        StreamDecoder streams,
        CosDictionary catalog,
        IReadOnlyList<PdfRevision> revisions,
        PdfLinearization? linearization,
        PdfSecurity? security)
    {
        Security = security;
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

    /// <summary>Gets the document's name dictionary, or <see langword="null"/> when the catalog has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (<c>Names</c>, PDF 1.2), and §7.7.4. Read from the catalog on every call. An entry that is not a
    /// dictionary reads as none, with a diagnostic.
    /// </remarks>
    public PdfNameDictionary? Names
    {
        get
        {
            if (!Catalog.TryGetValue(NavigationNames.Names, out CosObject? entry))
            {
                return null;
            }

            if (Resolve(entry) is CosDictionary names)
            {
                return new PdfNameDictionary(this, names);
            }

            _diagnostics.Report(
                DiagnosticCodes.NameDictionaryInvalid,
                DiagnosticSeverity.Warning,
                "The catalog's Names entry is not a dictionary; the document is read as having no name dictionary.",
                objectReference: entry as CosReference);
            return null;
        }
    }

    /// <summary>Gets the document outline (bookmarks), or <see langword="null"/> when the catalog has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (<c>Outlines</c>), and §12.3.3. Read from the catalog on every call. An entry that is not a
    /// dictionary reads as none, with a diagnostic.
    /// </remarks>
    public PdfOutline? Outline
    {
        get
        {
            if (!Catalog.TryGetValue(NavigationNames.Outlines, out CosObject? entry))
            {
                return null;
            }

            switch (Resolve(entry))
            {
                case CosDictionary outline:
                    return new PdfOutline(this, outline, entry as CosReference);
                case CosNull:
                    return null;
                default:
                    _diagnostics.Report(
                        DiagnosticCodes.OutlineInvalid,
                        DiagnosticSeverity.Warning,
                        "The catalog's Outlines entry is not a dictionary; the document is read as having no outline.",
                        objectReference: entry as CosReference);
                    return null;
            }
        }
    }

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

    /// <summary>Gets a value indicating whether the document is encrypted (its trailer has an <c>Encrypt</c> entry).</summary>
    /// <remarks>
    /// ISO 32000-2 §7.6. An encrypted document opens transparently: strings and streams read through it are decrypted, so its COS
    /// objects hold plaintext.
    /// </remarks>
    public bool IsEncrypted => Security is not null;

    /// <summary>Gets how the document is encrypted and what the credential that opened it allows, or <see langword="null"/> when it is not encrypted.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004.</remarks>
    public PdfSecurity? Security { get; }

    /// <summary>
    /// Gets the operations the document permits: <see cref="PdfPermissions.All"/> when it is not encrypted or was opened with the
    /// owner password, otherwise those its security handler grants.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 22, and §7.6.5.2, Table 24. Readers are expected to respect them; PDF does not enforce them.</remarks>
    public PdfPermissions Permissions => Security?.Permissions ?? PdfPermissions.All;

    /// <summary>Gets the document's mark information: whether it is a tagged PDF (<see cref="PdfMarkInfo.Marked"/>) and which conventions it uses.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (<c>MarkInfo</c>), §14.7.1, Table 353. Never <see langword="null"/>: without the dictionary every flag is false.</remarks>
    public PdfMarkInfo MarkInfo => new(this);

    /// <summary>Gets the root of the document's structure tree, or <see langword="null"/> when the document has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (<c>StructTreeRoot</c>), §14.7.2. Read when first asked for; an untagged document has no structure
    /// tree and no diagnostic. A <c>StructTreeRoot</c> that is not a dictionary is ignored with a <c>StructTreeRootInvalid</c> diagnostic.
    /// The views share indexes built on first use (see <see cref="PdfStructureTreeRoot"/>); they are rebuilt when the catalog's
    /// <c>StructTreeRoot</c> entry is replaced.
    /// </remarks>
    public PdfStructureTreeRoot? StructureTree
    {
        get
        {
            if (!Catalog.TryGetValue(StructureNames.StructTreeRoot, out CosObject? value))
            {
                return null;
            }

            CosReference? reference = value as CosReference;
            if (Resolve(value) is not CosDictionary root)
            {
                if (Resolve(value) is not CosNull)
                {
                    _diagnostics.Report(DiagnosticCodes.StructTreeRootInvalid, DiagnosticSeverity.Warning, "The catalog's StructTreeRoot is not a dictionary (Table 29); the document is read without a structure tree.", offset: null, reference);
                }

                return null;
            }

            StructureContext? current = Volatile.Read(ref _structure);
            if (current is null || !ReferenceEquals(current.Root, root))
            {
                if (root.TryGetValue(KnownNames.Type, out CosObject? type) && !StructureNames.StructTreeRoot.Equals(Resolve(type)))
                {
                    _diagnostics.Report(DiagnosticCodes.StructTreeRootInvalid, DiagnosticSeverity.Warning, "The structure tree root's Type is not StructTreeRoot (Table 354); read as the root.", offset: null, reference);
                }

                var created = new StructureContext(this, root, reference);
                StructureContext? previous = Interlocked.CompareExchange(ref _structure, created, current);
                current = ReferenceEquals(previous, current) ? created : previous!;
            }

            return new PdfStructureTreeRoot(current);
        }
    }

    /// <summary>Gets the deviations found so far, in the order they were found. Empty for a well-formed file.</summary>
    /// <remarks>
    /// ADR 0005. A snapshot: objects load lazily, so reading more of the document can add diagnostics, which a later call returns.
    /// </remarks>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Snapshot();

    /// <summary>Opens the PDF file at <paramref name="path"/> with default options.</summary>
    /// <param name="path">The file path. The file is memory-mapped and shared for reading: do not change it until the document is disposed.</param>
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
    /// <remarks>
    /// ISO 32000-2 §7.5. A seekable stream is read in place as objects are used, under a lock (a <see cref="FileStream"/> is
    /// memory-mapped instead); a non-seekable stream is copied first, into memory up to <see cref="PdfOptions.StreamBufferLimit"/> and
    /// into a temporary file beyond it.
    /// </remarks>
    public static PdfDocument Open(Stream stream) => PdfEngine.Default.Open(stream);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position, with default options; a non-seekable stream is read asynchronously.</summary>
    /// <param name="stream">The stream. It is not disposed; a seekable stream must stay open and unchanged until the document is disposed.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all.</exception>
    /// <remarks>ISO 32000-2 §7.5. The same as <see cref="PdfEngine.OpenAsync(Stream, CancellationToken)"/> on an engine with default options.</remarks>
    public static Task<PdfDocument> OpenAsync(Stream stream) => PdfEngine.Default.OpenAsync(stream, CancellationToken.None);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position, with default options; a non-seekable stream is read asynchronously.</summary>
    /// <param name="stream">The stream. It is not disposed; a seekable stream must stay open and unchanged until the document is disposed.</param>
    /// <param name="cancellationToken">Cancels reading a non-seekable stream.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>ISO 32000-2 §7.5. The same as <see cref="PdfEngine.OpenAsync(Stream, CancellationToken)"/> on an engine with default options.</remarks>
    public static Task<PdfDocument> OpenAsync(Stream stream, CancellationToken cancellationToken) =>
        PdfEngine.Default.OpenAsync(stream, cancellationToken);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position; a non-seekable stream is read asynchronously.</summary>
    /// <param name="stream">The stream. It is not disposed; a seekable stream must stay open and unchanged until the document is disposed.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancels reading a non-seekable stream.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>ISO 32000-2 §7.5. The same as <see cref="PdfEngine.OpenAsync(Stream, CancellationToken)"/> on an engine built from <paramref name="options"/>.</remarks>
    public static Task<PdfDocument> OpenAsync(Stream stream, PdfOptions options, CancellationToken cancellationToken = default) =>
        new PdfEngine(options).OpenAsync(stream, cancellationToken);

    /// <summary>Opens the PDF file held by <paramref name="stream"/>, from its current position.</summary>
    /// <param name="stream">The stream. It is not disposed; keep it open and unchanged until the document is disposed.</param>
    /// <param name="options">The options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <exception cref="DiagnosticException">The file cannot be read at all, or, in strict mode, deviates from ISO 32000-2.</exception>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static PdfDocument Open(Stream stream, PdfOptions options) => new PdfEngine(options).Open(stream);

    /// <summary>Opens the PDF file held in <paramref name="bytes"/> with default options.</summary>
    /// <param name="bytes">
    /// The file. Not copied: do not change it while the document or any of its objects is in use; stream data refers into it.
    /// </param>
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

    /// <summary>Creates a new document with one empty US Letter page (612 by 792 points), with default options.</summary>
    /// <returns>The document. Dispose it when done.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2 (catalog), §7.7.3 (page tree) and §7.5.2: the document is PDF 2.0. The same as
    /// <see cref="PdfEngine.Create"/> on an engine with default options.
    /// </remarks>
    public static PdfDocument Create() => PdfEngine.Default.Create();

    /// <summary>Creates a new document with one empty US Letter page (612 by 792 points).</summary>
    /// <param name="options">The options.</param>
    /// <returns>The document. Dispose it when done.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, §7.7.3 and §7.5.2. The same as <see cref="PdfEngine.Create"/> on an engine built from
    /// <paramref name="options"/>.
    /// </remarks>
    public static PdfDocument Create(PdfOptions options) => new PdfEngine(options).Create();

    /// <summary>Writes the document to <paramref name="stream"/> as a complete file with a classic cross-reference table.</summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <exception cref="NotSupportedException">The document is encrypted, or uses object numbers above 8,388,607; saving either arrives in a later version.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while loading the objects to write.</exception>
    /// <remarks>ISO 32000-2 §7.5. As <see cref="Save(Stream, PdfSaveOptions)"/> with default <see cref="PdfSaveOptions"/>.</remarks>
    public void Save(Stream stream) => Save(stream, new PdfSaveOptions());

    /// <summary>Writes the document to <paramref name="stream"/> as a complete file.</summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="options">How to lay the file out.</param>
    /// <exception cref="NotSupportedException">The document is encrypted, or uses object numbers above 8,388,607; saving either arrives in a later version.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while loading the objects to write.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.5: a header, every object, the cross-reference information and the trailer. Objects keep their numbers and
    /// generations. An object that has not changed since it was read, and was read without any repair, is written with exactly the
    /// bytes it was read from; changed, repaired and new objects are serialized. The revisions of an incrementally updated file
    /// collapse into one, and a linearized file is written unlinearized (Annex F).
    /// </para>
    /// <para>
    /// The header keeps the document's version, raised to 1.5 when the layout needs cross-reference streams. The trailer's first
    /// file identifier is kept; the second is derived from the written bytes, so saving the same document the same way always writes
    /// the same bytes (§14.4).
    /// </para>
    /// </remarks>
    public void Save(Stream stream, PdfSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        foreach (ReadOnlyMemory<byte> chunk in PlanSave(options).Write())
        {
            stream.Write(chunk.Span);
        }
    }

    /// <summary>Writes the document to the file at <paramref name="path"/>, replacing it, with a classic cross-reference table.</summary>
    /// <param name="path">The file path. Do not name the file the document was opened from.</param>
    /// <exception cref="NotSupportedException">The document is encrypted, or uses object numbers above 8,388,607; saving either arrives in a later version.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while loading the objects to write.</exception>
    /// <remarks>ISO 32000-2 §7.5. As <see cref="Save(Stream, PdfSaveOptions)"/> with default <see cref="PdfSaveOptions"/>.</remarks>
    public void Save(string path) => Save(path, new PdfSaveOptions());

    /// <summary>Writes the document to the file at <paramref name="path"/>, replacing it.</summary>
    /// <param name="path">The file path. Do not name the file the document was opened from.</param>
    /// <param name="options">How to lay the file out.</param>
    /// <exception cref="NotSupportedException">The document is encrypted, or uses object numbers above 8,388,607; saving either arrives in a later version.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while loading the objects to write.</exception>
    /// <remarks>ISO 32000-2 §7.5. As <see cref="Save(Stream, PdfSaveOptions)"/>.</remarks>
    public void Save(string path, PdfSaveOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FileWriter writer = PlanSave(options);
        using FileStream stream = File.Create(path);
        foreach (ReadOnlyMemory<byte> chunk in writer.Write())
        {
            stream.Write(chunk.Span);
        }
    }

    /// <summary>Writes the document to <paramref name="stream"/> as a complete file, writing asynchronously.</summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="options">How to lay the file out; <see langword="null"/> for the defaults (a classic cross-reference table).</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="NotSupportedException">The document is encrypted, or uses object numbers above 8,388,607; saving either arrives in a later version.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found while loading the objects to write.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.5. Writes exactly the bytes <see cref="Save(Stream, PdfSaveOptions)"/> writes, from the same writer; only the
    /// writes to <paramref name="stream"/> are asynchronous.
    /// </remarks>
    public async Task SaveAsync(Stream stream, PdfSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        foreach (ReadOnlyMemory<byte> chunk in PlanSave(options ?? new PdfSaveOptions()).Write())
        {
            await stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns <paramref name="value"/>, or the indirect object it refers to when it is a <see cref="CosReference"/>.</summary>
    /// <param name="value">A COS object from this document, or <see langword="null"/>.</param>
    /// <returns>
    /// The direct object. A reference to an object that does not exist, and <see langword="null"/>, resolve to
    /// <see cref="CosNull.Instance"/>. Resolving the same reference twice returns the same instance.
    /// </returns>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public CosObject Resolve(CosObject? value) => _loader.Resolve(value);

    /// <summary>Returns a view of the name tree whose root node is <paramref name="root"/>.</summary>
    /// <param name="root">The root node dictionary, or a reference to it, from this document.</param>
    /// <returns>
    /// The tree, read lazily; <see langword="null"/> when <paramref name="root"/> is <see langword="null"/> or resolves to null, or,
    /// with a diagnostic, to something other than a dictionary.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.6. For name trees the document model does not expose by name. Views of the same root share one reader, so
    /// the index a damaged tree needs is built once per document.
    /// </remarks>
    public PdfNameTree? GetNameTree(CosObject? root) =>
        GetNameTreeReader(root) is { } reader ? new PdfNameTree(reader) : null;

    /// <summary>Returns a view of the number tree whose root node is <paramref name="root"/>.</summary>
    /// <param name="root">The root node dictionary, or a reference to it, from this document.</param>
    /// <returns>
    /// The tree, read lazily; <see langword="null"/> when <paramref name="root"/> is <see langword="null"/> or resolves to null, or,
    /// with a diagnostic, to something other than a dictionary.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §7.9.7. For number trees such as the catalog's <c>PageLabels</c> or a structure tree's <c>ParentTree</c>. Views of
    /// the same root share one reader.
    /// </remarks>
    public PdfNumberTree? GetNumberTree(CosObject? root) =>
        GetNumberTreeReader(root) is { } reader ? new PdfNumberTree(reader) : null;

    /// <summary>Looks up the destination a name (PDF 1.1) or string (PDF 1.2) names, as text.</summary>
    /// <param name="name">The destination's name.</param>
    /// <returns>
    /// The explicit destination; <see langword="null"/> when the document has none of that name, or, with a diagnostic, when the
    /// value it has is not a destination.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.4. The name dictionary's <c>Dests</c> tree first (the text as PDFDocEncoding, then UTF-16BE, then every key
    /// decoded: <see cref="PdfNameTree.TryGetValue(string, out CosObject)"/>), then the catalog's <c>Dests</c> dictionary (the text as a
    /// UTF-8 name). A value that is a dictionary resolves to its <c>D</c> entry.
    /// </remarks>
    public PdfExplicitDestination? GetNamedDestination(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Names?.Dests is { } tree && tree.Reader.TryGetRawValue(name, out CosObject? value))
        {
            return ReadNamedDestinationValue(value);
        }

        if (!name.Contains('\0', StringComparison.Ordinal) && LegacyDestination(new CosName(name)) is { } legacy)
        {
            return ReadNamedDestinationValue(legacy);
        }

        return null;
    }

    /// <summary>Looks up the destination a byte string names (PDF 1.2).</summary>
    /// <param name="name">The destination's name.</param>
    /// <returns>The explicit destination, or <see langword="null"/>; see <see cref="GetNamedDestination(string)"/>.</returns>
    /// <remarks>ISO 32000-2 §12.3.2.4 and Annex J.3.3: the <c>Dests</c> tree first, then the catalog's <c>Dests</c> dictionary, by bytes.</remarks>
    public PdfExplicitDestination? GetNamedDestination(CosString name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return FindNamedDestination(name, out _);
    }

    /// <summary>Looks up the destination a name object names (PDF 1.1).</summary>
    /// <param name="name">The destination's name.</param>
    /// <returns>The explicit destination, or <see langword="null"/>; see <see cref="GetNamedDestination(string)"/>.</returns>
    /// <remarks>ISO 32000-2 §12.3.2.4 and Annex J.3.4: the catalog's <c>Dests</c> dictionary first, then the <c>Dests</c> tree, by bytes.</remarks>
    public PdfExplicitDestination? GetNamedDestination(CosName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return FindNamedDestination(name, out _);
    }

    /// <summary>Looks a destination name up: a name in the catalog's <c>Dests</c> first, a string in the tree first, then the other.</summary>
    /// <param name="name">A <see cref="CosName"/> or <see cref="CosString"/>.</param>
    /// <param name="found">Whether the document holds the name, whatever its value.</param>
    /// <returns>The explicit destination, or <see langword="null"/>.</returns>
    internal PdfExplicitDestination? FindNamedDestination(CosObject name, out bool found)
    {
        CosObject? value = null;
        switch (name)
        {
            case CosName key:
                value = LegacyDestination(key) ?? TreeDestination(key.Bytes);
                break;
            case CosString key:
                value = TreeDestination(key.Bytes);
                if (value is null && key.Bytes.Length > 0 && !key.Bytes.Contains((byte)0))
                {
                    value = LegacyDestination(new CosName(key.Bytes));
                }

                break;
        }

        found = value is not null;
        return value is null ? null : ReadNamedDestinationValue(value);
    }

    /// <summary>Reads a named destination's value: a destination array, or a dictionary whose <c>D</c> entry is one (§12.3.2.4).</summary>
    private PdfExplicitDestination? ReadNamedDestinationValue(CosObject value)
    {
        var reference = value as CosReference;
        CosObject resolved = Resolve(value);
        if (resolved is CosDictionary dictionary && dictionary.TryGetValue(NavigationNames.D, out CosObject? d))
        {
            reference = d as CosReference ?? reference;
            resolved = Resolve(d);
        }

        if (resolved is CosArray array)
        {
            return new PdfExplicitDestination(this, array, isRemote: false, reference);
        }

        _diagnostics.Report(
            DiagnosticCodes.DestinationInvalid,
            DiagnosticSeverity.Warning,
            "A named destination's value is neither a destination array nor a dictionary with a D entry; the name shows no page.",
            objectReference: reference);
        return null;
    }

    private CosObject? TreeDestination(ReadOnlySpan<byte> key) =>
        Names?.Dests is { } tree && tree.Reader.TryGetRawValue(new CosString(key), out CosObject? value) ? value : null;

    private CosObject? LegacyDestination(CosName key) =>
        Catalog.TryGetValue(NavigationNames.Dests, out CosObject? entry)
            && Resolve(entry) is CosDictionary dests
            && dests.TryGetValue(key, out CosObject? value)
            ? value
            : null;

    /// <summary>Returns the document's reader for the name tree rooted at <paramref name="root"/>, creating it on first use.</summary>
    /// <param name="root">The root node or a reference to it.</param>
    /// <returns>The reader; <see langword="null"/> when the root does not resolve to a dictionary.</returns>
    internal NameTreeReader? GetNameTreeReader(CosObject? root) =>
        ResolveTreeRoot(root, DiagnosticCodes.NameTreeNodeInvalid, "name tree") is { } node
            ? _nameTrees.GetValue(node, dictionary => new NameTreeReader(dictionary, root as CosReference, Resolve, _diagnostics))
            : null;

    /// <summary>Returns the document's reader for the number tree rooted at <paramref name="root"/>, creating it on first use.</summary>
    /// <param name="root">The root node or a reference to it.</param>
    /// <returns>The reader; <see langword="null"/> when the root does not resolve to a dictionary.</returns>
    internal NumberTreeReader? GetNumberTreeReader(CosObject? root) =>
        ResolveTreeRoot(root, DiagnosticCodes.NumberTreeNodeInvalid, "number tree") is { } node
            ? _numberTrees.GetValue(node, dictionary => new NumberTreeReader(dictionary, root as CosReference, Resolve, _diagnostics))
            : null;

    /// <summary>Gets the diagnostics sink, for document-model views that report what they find on first read.</summary>
    internal DiagnosticSink DiagnosticSink => _diagnostics;
    /// <summary>Gets the document's optional content (layers), or <see langword="null"/> when the catalog has no <c>OCProperties</c>.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.11 and §7.7.2, Table 29 (PDF 1.5). Without <c>OCProperties</c> every optional content structure is ignored and all
    /// content is visible (§8.11.4.2). The same view is returned while the catalog's <c>OCProperties</c> is the same dictionary; its
    /// group list is a snapshot taken on first use.
    /// </remarks>
    public PdfOptionalContentProperties? OptionalContent
    {
        get
        {
            if (Resolve(Catalog.TryGetValue(FileAndLayerNames.OCProperties, out CosObject? entry) ? entry : null) is not CosDictionary dictionary)
            {
                return null;
            }

            PdfOptionalContentProperties? cached = Volatile.Read(ref _optionalContent);
            if (cached is not null && ReferenceEquals(cached.Dictionary, dictionary))
            {
                return cached;
            }

            CosReference? reference = entry as CosReference ?? (Trailer.TryGetValue(KnownNames.Root, out CosObject? root) ? root as CosReference : null);
            var created = new PdfOptionalContentProperties(this, dictionary, reference);
            PdfOptionalContentProperties? raced = Interlocked.CompareExchange(ref _optionalContent, created, cached);
            return raced == cached ? created : (ReferenceEquals(raced!.Dictionary, dictionary) ? raced : created);
        }
    }

    /// <summary>Gets the document's embedded files: the entries of the name dictionary's <c>EmbeddedFiles</c> tree, in tree order.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.4, Table 32 (PDF 1.4), and §7.11.4. Read on every call. An entry whose value is not a file specification is
    /// skipped with a <c>FileSpecificationInvalid</c> diagnostic. Files attached only to annotations or associated files are not
    /// listed here (PDF 2.0 Application Note 002 §6.2): see <see cref="EnumerateAssociatedFiles"/>.
    /// </remarks>
    public IReadOnlyList<PdfEmbeddedFileEntry> EmbeddedFiles
    {
        get
        {
            if (Names?.EmbeddedFiles is not { } tree)
            {
                return [];
            }

            var entries = new List<PdfEmbeddedFileEntry>();
            foreach (KeyValuePair<CosString, CosObject> entry in tree)
            {
                if (PdfFileSpecification.Create(this, entry.Value) is { } file)
                {
                    entries.Add(new PdfEmbeddedFileEntry(entry.Key, file));
                }
                else
                {
                    ViewReading.Warn(this, DiagnosticCodes.FileSpecificationInvalid, $"The EmbeddedFiles entry '{entry.Key.DecodeText()}' is not a file specification; it is skipped.", tree.RootReference);
                }
            }

            return entries;
        }
    }

    /// <summary>Gets the document's portable collection (<c>Collection</c>), or <see langword="null"/> when it is not a portfolio.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (PDF 1.7), and §12.3.5. Read from the catalog on every call.</remarks>
    public PdfCollection? Collection =>
        Resolve(Catalog.TryGetValue(FileAndLayerNames.Collection, out CosObject? entry) ? entry : null) is CosDictionary dictionary
            ? new PdfCollection(this, dictionary, entry as CosReference)
            : null;

    /// <summary>Gets the PDF Declarations of the whole document, from the XMP of the catalog's <c>Metadata</c>.</summary>
    /// <remarks>PDF Declarations §7 and §8. Declarations live only in XMP; there is no catalog key. Read on every call (the packet is parsed once).</remarks>
    public IReadOnlyList<PdfDeclaration> Declarations => PdfDeclaration.Read(Metadata?.Packet);

    /// <summary>Gets the files associated with the whole document (the catalog's <c>AF</c>).</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (PDF 2.0), and §14.13.3. Read on every call.</remarks>
    public IReadOnlyList<PdfFileSpecification> AssociatedFiles => ReadAssociatedFiles(Catalog, CatalogReference);

    /// <summary>Returns the associated files listed in the <c>AF</c> entry of <paramref name="owner"/>.</summary>
    /// <param name="owner">Any dictionary or stream dictionary of this document: a structure element, an annotation, a form field.</param>
    /// <param name="ownerReference">The owner's indirect reference, for diagnostics.</param>
    /// <returns>The file specifications, in order.</returns>
    /// <remarks>
    /// ISO 32000-2 §14.13.2 (PDF 2.0): <c>AF</c> is an array of file specification dictionaries. A single dictionary is read as an
    /// array of one and a file specification string is accepted, both with an <c>AssociatedFilesInvalid</c> diagnostic. An embedded
    /// associated file without <c>Subtype</c>, or with <c>Params</c> but no <c>ModDate</c>, is reported. A missing
    /// <c>AFRelationship</c> means Unspecified and is not reported.
    /// </remarks>
    public IReadOnlyList<PdfFileSpecification> ReadAssociatedFiles(CosDictionary owner, CosReference? ownerReference = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.TryGetValue(FileAndLayerNames.AF, out CosObject? value) ? AssociatedFileReader.Read(this, value, ownerReference) : [];
    }

    /// <summary>Returns the associated files of a marked-content sequence, from the property list of its <c>/AF</c> tag.</summary>
    /// <param name="properties">The property list operand of <c>/AF ... BDC</c>, resolved from the <c>Properties</c> resource, or a reference to it.</param>
    /// <returns>The file specifications.</returns>
    /// <remarks>
    /// ISO 32000-2 §14.13.5 and errata Table 409a: a property list dictionary whose <c>MCAF</c> array lists the files (each shall
    /// have <c>AFRelationship</c>), or, as in the original text's Example 2, a resource that is the array itself. Both are accepted.
    /// </remarks>
    public IReadOnlyList<PdfFileSpecification> ReadMarkedContentAssociatedFiles(CosObject? properties) =>
        AssociatedFileReader.ReadMarkedContent(this, properties, properties as CosReference);

    /// <summary>Enumerates every associated file of the document with the object it is associated with.</summary>
    /// <param name="deep">
    /// <see langword="false"/> walks the known locations: the catalog and its metadata, the structure tree, document parts, and every
    /// page with its resources (form and image XObjects, marked-content property lists, recursively through forms) and annotations.
    /// <see langword="true"/> also scans every object of the file for an <c>AF</c> entry (Application Note 002 §3.2: AF may appear on
    /// any object), reporting those as <see cref="PdfAssociatedFileLocation.Other"/>.
    /// </param>
    /// <returns>A lazy sequence; each object is reported once. Nothing is cached.</returns>
    /// <remarks>ISO 32000-2 §14.13 and PDF 2.0 Application Note 002 §6.2.</remarks>
    public IEnumerable<PdfAssociatedFile> EnumerateAssociatedFiles(bool deep = false) => AssociatedFileReader.Enumerate(this, deep);

    /// <summary>Enumerates every object-level metadata stream of the document with the object it describes.</summary>
    /// <param name="deep">
    /// <see langword="false"/> walks the locations Application Note 003 lists: the catalog, optional content groups, threads,
    /// structure elements, document parts, embedded files, pages, XObjects, ICC profiles, embedded font programs, Type 3 fonts, tiling
    /// patterns, shadings, marked-content property lists, annotations and 3D artwork. <see langword="true"/> also scans every object
    /// of the file for a <c>Metadata</c> entry, reporting those as <see cref="PdfMetadataLocation.Other"/>.
    /// </param>
    /// <returns>A lazy sequence; each object is reported once. Nothing is cached.</returns>
    /// <remarks>
    /// ISO 32000-2 §14.3.2 (PDF 1.4), Tables 347 and 348, and PDF 2.0 Application Note 003. A metadata stream without <c>Type</c>
    /// <c>Metadata</c> and <c>Subtype</c> <c>XML</c> is still reported, with a <c>MetadataStreamInvalid</c> diagnostic.
    /// </remarks>
    public IEnumerable<PdfObjectMetadata> EnumerateObjectMetadata(bool deep = false) => AssociatedFileReader.EnumerateMetadata(this, deep);

    /// <summary>Enumerates the reference of every object the cross-reference information lists as in use, by object number.</summary>
    internal IEnumerable<CosReference> EnumerateObjectReferences()
    {
        foreach (KeyValuePair<int, XrefEntry> entry in _loader.CrossReference.Entries.OrderBy(entry => entry.Key))
        {
            switch (entry.Value.Kind)
            {
                case XrefEntryKind.InUse:
                    yield return new CosReference(entry.Key, entry.Value.Generation);
                    break;
                case XrefEntryKind.Compressed:
                    yield return new CosReference(entry.Key, 0);
                    break;
            }
        }
    }

    /// <summary>Returns a view over a file specification held by <paramref name="value"/>, resolving it first.</summary>
    /// <param name="value">A file specification string or dictionary, or a reference to one, of this document.</param>
    /// <returns>The view, or <see langword="null"/> when the value is neither a string nor a dictionary.</returns>
    /// <remarks>ISO 32000-2 §7.11. For file specifications the document model does not reach itself (an action's <c>F</c>, say).</remarks>
    public PdfFileSpecification? GetFileSpecification(CosObject? value) => PdfFileSpecification.Create(this, value);

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

    /// <summary>Returns the font view over a font dictionary of this document.</summary>
    /// <param name="font">A font dictionary, or an indirect reference to one, such as an entry of a resource dictionary's <c>Font</c> subdictionary.</param>
    /// <returns>
    /// The view, of the type the dictionary's <c>Subtype</c> selects; <see langword="null"/> when <paramref name="font"/> is not a
    /// dictionary and does not refer to one. The same dictionary always gives the same view instance.
    /// </returns>
    /// <exception cref="DiagnosticException">In strict mode, when the dictionary's <c>Type</c> or <c>Subtype</c> is not a font's.</exception>
    /// <remarks>ISO 32000-2 §9.5, Table 108, and §7.8.3.</remarks>
    public PdfFont? GetFont(CosObject? font)
    {
        if (Resolve(font) is not CosDictionary dictionary)
        {
            return null;
        }

        if (_fonts.TryGetValue(dictionary, out PdfFont? existing))
        {
            return existing;
        }

        // Two threads may create a view each; the first one added is the one everybody gets.
        PdfFont created = PdfFont.Create(this, dictionary, font as CosReference);
        return _fonts.TryAdd(dictionary, created) || !_fonts.TryGetValue(dictionary, out PdfFont? winner) ? created : winner;
    }

    /// <inheritdoc/>
    public void Dispose() => _source.Dispose();

    private CosDictionary? ResolveTreeRoot(CosObject? root, string code, string kind)
    {
        switch (Resolve(root))
        {
            case CosDictionary dictionary:
                return dictionary;
            case CosNull:
                return null;
            default:
                _diagnostics.Report(
                    code,
                    DiagnosticSeverity.Warning,
                    $"The root of a {kind} is not a dictionary; the tree is read as absent.",
                    objectReference: root as CosReference);
                return null;
        }
    }

    /// <summary>Builds the bytes of a new one-page document and opens them (issue #44).</summary>
    /// <param name="configuration">The engine's configuration.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, §7.7.3, Table 31: a catalog, a page tree root and one US Letter page whose <c>Resources</c> is the empty
    /// dictionary (no resources) and which has no <c>Contents</c> (an empty page). Written as PDF 2.0 with file identifiers.
    /// </remarks>
    internal static PdfDocument CreateNew(EngineConfiguration configuration) => Read(PdfSource.FromMemory(NewDocumentFile()), configuration);

    /// <summary>Writes the file <see cref="CreateNew"/> opens.</summary>
    private static byte[] NewDocumentFile()
    {
        var catalog = new CosReference(1, 0);
        var pages = new CosReference(2, 0);
        var page = new CosReference(3, 0);
        var mediaBox = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(612), new CosInteger(792)]);
        WriterObject[] objects =
        [
            new(catalog, new CosDictionary { [KnownNames.Type] = KnownNames.Catalog, [KnownNames.Pages] = pages }, null),
            new(pages, new CosDictionary { [KnownNames.Type] = KnownNames.Pages, [KnownNames.Kids] = new CosArray([page]), [KnownNames.Count] = new CosInteger(1) }, null),
            new(page, new CosDictionary { [KnownNames.Type] = KnownNames.Page, [KnownNames.Parent] = pages, [KnownNames.MediaBox] = mediaBox, [KnownNames.Resources] = new CosDictionary() }, null),
        ];
        var writer = new FileWriter(
            source: null,
            new PdfVersion(2, 0),
            objects,
            new Dictionary<int, int>(),
            new CosDictionary { [KnownNames.Root] = catalog },
            firstIdentifier: null,
            PdfCrossReferenceLayout.Table);
        using var bytes = new MemoryStream();
        foreach (ReadOnlyMemory<byte> chunk in writer.Write())
        {
            bytes.Write(chunk.Span);
        }

        return bytes.ToArray();
    }

    /// <summary>Plans a full save; throws for what cannot be saved before anything is written.</summary>
    private FileWriter PlanSave(PdfSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SavePlan.Create(_source, _loader, Linearization, options.CrossReferenceLayout);
    }

    /// <summary>The open path shared by every entry point: header, cross-reference information, trailer, catalog.</summary>
    /// <param name="source">The file; owned by the document, disposed if opening fails.</param>
    /// <param name="configuration">The engine's configuration.</param>
    /// <param name="credentials">The credentials for an encrypted document; <see langword="null"/> for the configuration's.</param>
    /// <returns>The document.</returns>
    internal static PdfDocument Read(PdfSource source, EngineConfiguration configuration, PdfCredentials? credentials = null)
    {
        try
        {
            var diagnostics = new DiagnosticSink(configuration.ReadingMode == PdfReadingMode.Strict, configuration.DiagnosticObserver);
            FileHeader header = FileHeader.Locate(source, diagnostics);

            // Cross-reference streams are decoded before any object can be loaded; their Filter and DecodeParms are direct
            // (§7.5.8.2), so until the loader exists a reference resolves to null.
            ObjectLoader? loader = null;
            var streams = new StreamDecoder(
                configuration.Filters,
                configuration.MaxDecodedStreamLength,
                diagnostics,
                value => loader is not null ? loader.Resolve(value) : value is null or CosReference ? CosNull.Instance : value);
            var scan = new Lazy<FileScan>(() => FileScan.Run(source), LazyThreadSafetyMode.ExecutionAndPublication);
            CrossReference? crossReference = CrossReferenceReader.Read(source, header, streams, diagnostics, scan);
            bool reconstructed = crossReference is null;
            crossReference ??= Reconstruct(source, header, scan.Value, streams, diagnostics, reportMissingTrailer: true);
            loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks(), streams) { Scan = scan, Logger = configuration.Logger };

            // Decryption is installed on the loader before the catalog loads (§7.6.2): the Encrypt dictionary loads undecrypted,
            // everything after it decrypted. A rebuilt loader gets it installed again.
            PdfCredentials? offered = credentials ?? configuration.Credentials;
            PdfSecurity? security = DocumentSecurity.Open(
                source, loader, streams, configuration.SecurityHandlers, offered, diagnostics, reportUnusable: reconstructed, out bool encryptUnusable);
            CosDictionary? catalog = encryptUnusable ? null : ReadCatalog(loader, diagnostics, reportUnusableRoot: reconstructed);
            if (catalog is null)
            {
                // §7.5.5: the trailer's Root shall lead to the catalog (and its Encrypt to the encryption dictionary). When it does
                // not, the cross-reference information is not trusted either: rebuild it by scanning, which also finds a catalog no
                // trailer names.
                diagnostics.Report(
                    encryptUnusable ? DiagnosticCodes.EncryptDictionaryInvalid : DiagnosticCodes.RootMissing,
                    DiagnosticSeverity.Warning,
                    encryptUnusable
                        ? "The trailer's Encrypt entry does not resolve to the encryption dictionary; the cross-reference information is rebuilt by scanning the file."
                        : "The trailer has no Root entry that resolves to the catalog dictionary; the cross-reference information is rebuilt by scanning the file.");
                crossReference = Reconstruct(source, header, scan.Value, streams, diagnostics, reportMissingTrailer: false);
                loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks(), streams) { Scan = scan, Logger = configuration.Logger };
                streams.CryptFilter = IdentityCryptFilterHandler.Instance;
                security = DocumentSecurity.Open(
                    source, loader, streams, configuration.SecurityHandlers, offered, diagnostics, reportUnusable: true, out _);
                catalog = ReadCatalog(loader, diagnostics, reportUnusableRoot: true)!;
            }

            IReadOnlyList<PdfRevision> revisions = ReadRevisions(source, crossReference, diagnostics);
            if (security is not null)
            {
                DocumentSecurity.CheckExtensions(security, catalog, loader.Resolve, diagnostics);
            }

            PdfLinearization? linearization = LinearizationReader.Read(source, loader, diagnostics);
            var document = new PdfDocument(source, diagnostics, loader, streams, catalog, revisions, linearization, security);

            // Table 15: ID is "required in PDF 2.0 or if an Encrypt entry is present" (the latter is the security handler's
            // EncryptionIdMissing).
            if (security is null && document.Version >= Pdf20 && !loader.CrossReference.Trailer.ContainsKey(KnownNames.ID))
            {
                diagnostics.Report(
                    DiagnosticCodes.TrailerIdMissing,
                    DiagnosticSeverity.Warning,
                    "The document is PDF 2.0, whose trailer shall have an ID entry; it has none.");
            }

            return document;
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

    /// <summary>
    /// Rebuilds the cross-reference information by scanning the file when it cannot be read or does not lead to the catalog (§7.5.4,
    /// §7.5.5; issue #41). Throws when the file holds no catalog at all.
    /// </summary>
    private static CrossReference Reconstruct(
        PdfSource source,
        FileHeader header,
        FileScan scan,
        StreamDecoder streams,
        DiagnosticSink diagnostics,
        bool reportMissingTrailer)
    {
        CrossReference? crossReference = CrossReferenceReconstructor.Reconstruct(source, header, scan, streams, diagnostics, reportMissingTrailer);
        if (crossReference is null)
        {
            diagnostics.Fail(
                DiagnosticCodes.CatalogNotFound,
                "The cross-reference information cannot be read, and scanning the file finds no catalog dictionary: the file cannot be read.");
        }

        return crossReference;
    }

    /// <summary>Loads the catalog through the trailer's <c>Root</c> and checks its <c>Type</c> and <c>Version</c> (§7.7.2).</summary>
    /// <returns>The catalog, or <see langword="null"/> when <c>Root</c> does not resolve to a dictionary.</returns>
    private static CosDictionary? ReadCatalog(ObjectLoader loader, DiagnosticSink diagnostics, bool reportUnusableRoot)
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
            if (reportUnusableRoot)
            {
                diagnostics.Fail(DiagnosticCodes.RootMissing, "The trailer has no Root entry that resolves to the catalog dictionary.");
            }

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
