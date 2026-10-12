using Broadside.Caching;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// The linearization information of a file that begins with a linearization parameter dictionary: the dictionary, the objects the
/// first page needs, and the hint tables.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 Annex F, F.3.3 and Table F.1. A linearized file is organized so that its first page can be displayed before the
/// rest of the file has arrived. Reading never relies on linearization: objects are located through the cross-reference
/// sections as in any file. This view exists for consumers that fetch byte ranges progressively.
/// </para>
/// <para>
/// A file keeps its parameter dictionary when an update is appended, but its <c>L</c> entry then no longer matches the file
/// length and the file is no longer linearized (Table F.1, G.7); <see cref="PdfDocument.IsLinearized"/> tells the two apart. The
/// hints may then be stale.
/// </para>
/// <para>The typed properties read <see cref="Dictionary"/> on every call; an entry that is not an integer reads as 0.</para>
/// </remarks>
public sealed class PdfLinearization
{
    private readonly OnceCache<int, PdfLinearizationHints?> _hints = new();
    private readonly Func<PdfLinearizationHints?> _readHints;

    internal PdfLinearization(CosDictionary dictionary, CosReference reference, IReadOnlyList<CosReference> firstPageObjects, Func<PdfLinearizationHints?> readHints)
    {
        Dictionary = dictionary;
        Reference = reference;
        FirstPageObjects = firstPageObjects;
        _readHints = readHints;
    }

    /// <summary>Gets the linearization parameter dictionary, the first object in the file.</summary>
    /// <remarks>ISO 32000-2 F.3.3, Table F.1.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the linearization parameter dictionary.</summary>
    /// <remarks>ISO 32000-2 F.3.3: nothing in the document refers to it, but the first-page cross-reference section lists it.</remarks>
    public CosReference Reference { get; }

    /// <summary>Gets the file length the dictionary states (<c>L</c>), in bytes from the <c>%PDF-</c> header.</summary>
    /// <remarks>ISO 32000-2 Table F.1. When it differs from the actual length, the file is not linearized.</remarks>
    public long FileLength => ReadInteger(Names.L);

    /// <summary>Gets the number of pages the dictionary states (<c>N</c>).</summary>
    /// <remarks>ISO 32000-2 Table F.1.</remarks>
    public int PageCount => (int)Math.Clamp(ReadInteger(Names.N), 0, int.MaxValue);

    /// <summary>Gets the object number of the first page's page object (<c>O</c>).</summary>
    /// <remarks>ISO 32000-2 Table F.1.</remarks>
    public int FirstPageObjectNumber => (int)Math.Clamp(ReadInteger(Names.O), 0, int.MaxValue);

    /// <summary>Gets the zero-based page number of the first page (<c>P</c>); 0 when absent.</summary>
    /// <remarks>ISO 32000-2 Table F.1 and F.3.4.</remarks>
    public int FirstPageNumber => (int)Math.Clamp(ReadInteger(Names.P), 0, int.MaxValue);

    /// <summary>
    /// Gets the objects the first-page cross-reference section lists, in object number order: the linearization parameter
    /// dictionary, the catalog and the other document-level objects needed at open, the first page's objects and the primary hint
    /// stream. Empty when the file's original revision has no separate first-page section.
    /// </summary>
    /// <remarks>ISO 32000-2 F.3.4, F.3.5 and F.3.6 (parts 2 to 6 of a linearized file).</remarks>
    public IReadOnlyList<CosReference> FirstPageObjects { get; }

    /// <summary>
    /// Gets the page offset and shared object hint tables, read on first use (hint streams are decoded through the document's
    /// filters); <see langword="null"/> when the hint streams cannot be decoded or read (a diagnostic says why).
    /// </summary>
    /// <remarks>ISO 32000-2 F.3.6, F.4.1, F.4.2 and F.4.3.</remarks>
    public PdfLinearizationHints? Hints => _hints.GetOrCreate(
        0,
        _readHints,
        static (_, read) => new Created<PdfLinearizationHints?>(read()),
        static (_, _) => null);

    private long ReadInteger(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) && value is CosInteger integer ? integer.Value : 0;

    /// <summary>The parameter dictionary's keys (Table F.1).</summary>
    internal static class Names
    {
        public static readonly CosName Linearized = new("Linearized");
        public static readonly CosName L = new("L");
        public static readonly CosName H = new("H");
        public static readonly CosName O = new("O");
        public static readonly CosName E = new("E");
        public static readonly CosName N = new("N");
        public static readonly CosName T = new("T");
        public static readonly CosName P = new("P");
    }
}
