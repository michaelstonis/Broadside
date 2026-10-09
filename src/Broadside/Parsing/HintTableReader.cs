using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Reads the page offset and shared object hint tables of a linearized file (F.4.2 and F.4.3).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.3.6 and F.4.1 to F.4.3. The primary hint stream is the object at <c>H[0]</c>; an overflow stream at <c>H[2]</c> is
/// appended to it. The page offset table starts at 0, the shared object table at the primary stream's <c>S</c> entry. The data is a
/// bit stream read most significant bit first.
/// </para>
/// <para>
/// Alignment follows qpdf, which is validated against Acrobat: every column of items (all pages' item 1, all pages' item 2, …)
/// starts on a byte boundary, where F.4.1 says only that each table does. Offsets at or past the hint stream are moved past it
/// (F.4.1 says "greater than"; a first page stored right after the hint stream needs "or equal").
/// </para>
/// <para>
/// Hints are information only. A table that cannot be read yields <see langword="null"/> and a
/// <see cref="DiagnosticCodes.LinearizationHintsInvalid"/> diagnostic; it never affects how the document reads. A hint stream with
/// a filter yields <see langword="null"/> without a diagnostic until stream decoding (issue #38) is wired in here.
/// </para>
/// </remarks>
internal static class HintTableReader
{
    /// <summary>The most pages, shared references or shared groups a table may describe; more is treated as a malformed table.</summary>
    private const int MaxEntries = 1 << 20;

    private static readonly CosName Filter = new("Filter");
    private static readonly CosName SharedObjectTable = new("S");

    /// <summary>Reads the hint tables.</summary>
    /// <param name="source">The file.</param>
    /// <param name="loader">The object loader.</param>
    /// <param name="parameters">The linearization parameter dictionary, already validated.</param>
    /// <param name="hintStreams">The <c>H</c> array: offset and length of the primary hint stream, then of the overflow stream.</param>
    /// <param name="diagnostics">Where to report a malformed table.</param>
    /// <returns>The tables, or <see langword="null"/>.</returns>
    public static PdfLinearizationHints? Read(PdfSource source, ObjectLoader loader, CosDictionary parameters, long[] hintStreams, DiagnosticSink diagnostics)
    {
        CosStream? primary = LoadStream(source, loader, hintStreams[0]);
        CosStream? overflow = null;
        if (primary is null || (hintStreams.Length == 4 && (overflow = LoadStream(source, loader, hintStreams[2])) is null))
        {
            return Invalid(diagnostics, "The H entry of the linearization parameter dictionary does not point at a hint stream.");
        }

        if (primary.Dictionary.ContainsKey(Filter) || (overflow?.Dictionary.ContainsKey(Filter) ?? false))
        {
            return null;
        }

        byte[] data = [.. primary.EncodedData.Span, .. overflow is null ? [] : overflow.EncodedData.Span];
        if (!primary.Dictionary.TryGetValue(SharedObjectTable, out CosObject? sharedEntry)
            || sharedEntry is not CosInteger { Value: >= 0 } shared
            || shared.Value >= data.Length)
        {
            return Invalid(diagnostics, "The primary hint stream's S entry does not give the position of the shared object hint table.");
        }

        var layout = new HintTableLayout(
            loader.Header.Offset,
            hintStreams[0],
            hintStreams[1],
            ReadInteger(parameters, PdfLinearization.Names.O),
            ReadInteger(parameters, PdfLinearization.Names.N));
        PdfLinearizationHints? hints = Parse(data, (int)shared.Value, layout, out string? error);
        return error is null ? hints : Invalid(diagnostics, error);
    }

    /// <summary>Decodes the page offset table at the start of <paramref name="data"/> and the shared object table at <paramref name="sharedTable"/>.</summary>
    /// <param name="data">The decoded, concatenated hint stream data.</param>
    /// <param name="sharedTable">The position of the shared object hint table in <paramref name="data"/>.</param>
    /// <param name="layout">What turning table positions into file offsets needs.</param>
    /// <param name="error">Why the tables cannot be read, or <see langword="null"/>.</param>
    /// <returns>The tables, or <see langword="null"/> with <paramref name="error"/> set.</returns>
    public static PdfLinearizationHints? Parse(ReadOnlySpan<byte> data, int sharedTable, HintTableLayout layout, out string? error)
    {
        error = null;
        if (layout.PageCount is < 0 or > MaxEntries)
        {
            error = "The linearization parameter dictionary's N entry is too large for a hint table.";
            return null;
        }

        if (sharedTable < 0 || sharedTable >= data.Length)
        {
            error = "The primary hint stream's S entry does not give the position of the shared object hint table.";
            return null;
        }

        var pageReader = new HintBitReader(data);
        if (!TryReadPages(ref pageReader, layout, out List<PdfPageHint> pages, out long firstPageLocation))
        {
            error = "The page offset hint table is truncated or malformed.";
            return null;
        }

        var sharedReader = new HintBitReader(data[sharedTable..]);
        if (!TryReadSharedObjects(ref sharedReader, layout, firstPageLocation, out List<PdfSharedObjectHint> groups))
        {
            error = "The shared object hint table is truncated or malformed.";
            return null;
        }

        return new PdfLinearizationHints(pages, groups);
    }

    /// <summary>Reads the page offset hint table (F.4.2, Tables F.3 and F.4).</summary>
    private static bool TryReadPages(ref HintBitReader reader, HintTableLayout context, out List<PdfPageHint> pages, out long firstPageLocation)
    {
        pages = [];
        long minObjects = reader.Read(32);
        firstPageLocation = reader.Read(32);
        int bitsObjects = reader.ReadWidth();
        long minLength = reader.Read(32);
        int bitsLength = reader.ReadWidth();
        _ = reader.Read(32); // 6: least content stream offset (Acrobat: placeholder)
        _ = reader.ReadWidth(); // 7
        _ = reader.Read(32); // 8: least content stream length (Acrobat: placeholder)
        _ = reader.ReadWidth(); // 9
        int bitsShared = reader.ReadWidth();
        int bitsIdentifier = reader.ReadWidth();
        _ = reader.ReadWidth(); // 12: numerator width; item 5 is not read
        _ = reader.Read(16); // 13: denominator
        if (reader.Failed)
        {
            return false;
        }

        int count = (int)context.PageCount;
        long[] objects = reader.ReadColumn(count, bitsObjects);
        long[] lengths = reader.ReadColumn(count, bitsLength);
        long[] sharedCounts = reader.ReadColumn(count, bitsShared);
        if (reader.Failed || sharedCounts.Sum() > MaxEntries)
        {
            return false;
        }

        // Item 4 of every page, page after page, then aligned (qpdf reads page 0's too when Acrobat writes them).
        var identifiers = new int[count][];
        for (int page = 0; page < count; page++)
        {
            identifiers[page] = new int[sharedCounts[page]];
            for (int index = 0; index < identifiers[page].Length; index++)
            {
                identifiers[page][index] = (int)reader.Read(bitsIdentifier);
            }
        }

        reader.AlignToByte();
        if (reader.Failed)
        {
            return false;
        }

        long location = firstPageLocation;
        long objectNumber = context.FirstPageObjectNumber;
        for (int page = 0; page < count; page++)
        {
            long objectCount = minObjects + objects[page];
            long length = minLength + lengths[page];
            if (objectCount > int.MaxValue || objectNumber + objectCount > int.MaxValue)
            {
                return false;
            }

            pages.Add(new PdfPageHint((int)objectNumber, (int)objectCount, context.Absolute(location), length, identifiers[page]));
            location += length;
            objectNumber = page == 0 ? 1 : objectNumber + objectCount;
        }

        return true;
    }

    /// <summary>Reads the shared object hint table (F.4.3, Tables F.5 and F.6).</summary>
    private static bool TryReadSharedObjects(ref HintBitReader reader, HintTableLayout context, long firstPageLocation, out List<PdfSharedObjectHint> groups)
    {
        groups = [];
        long firstSharedObject = reader.Read(32);
        long firstSharedLocation = reader.Read(32);
        long firstPageEntries = reader.Read(32);
        long totalEntries = reader.Read(32);
        int bitsObjects = reader.ReadWidth();
        long minLength = reader.Read(32);
        int bitsLength = reader.ReadWidth();
        if (reader.Failed || firstPageEntries > totalEntries || totalEntries > MaxEntries)
        {
            return false;
        }

        int count = (int)totalEntries;
        long[] lengths = reader.ReadColumn(count, bitsLength);
        long[] signed = reader.ReadColumn(count, 1);
        if (reader.Failed)
        {
            return false;
        }

        // Item 3, the 128-bit MD5 signature, for each group that has one (qpdf: no alignment needed, the column is aligned).
        foreach (long flag in signed)
        {
            if (flag != 0)
            {
                reader.Skip(128);
            }
        }

        long[] objectCounts = reader.ReadColumn(count, bitsObjects);
        if (reader.Failed)
        {
            return false;
        }

        long location = firstPageLocation;
        long objectNumber = context.FirstPageObjectNumber;
        for (int index = 0; index < count; index++)
        {
            if (index == firstPageEntries)
            {
                location = firstSharedLocation;
                objectNumber = firstSharedObject;
            }

            long objectCount = objectCounts[index] + 1;
            long length = minLength + lengths[index];
            if (objectNumber + objectCount > int.MaxValue)
            {
                return false;
            }

            groups.Add(new PdfSharedObjectHint((int)objectNumber, (int)objectCount, context.Absolute(location), length));
            location += length;
            objectNumber += objectCount;
        }

        return true;
    }

    /// <summary>Loads the stream whose <c>N G obj</c> header is at <paramref name="offset"/> (relative to the header).</summary>
    private static CosStream? LoadStream(PdfSource source, ObjectLoader loader, long offset)
    {
        if (offset > source.Length - loader.Header.Offset)
        {
            return null;
        }

        ReadOnlySpan<byte> window = source.GetWindow(loader.Header.Offset + offset).Span;
        var lexer = new CosLexer(window);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long number)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || !StructureTokens.TryReadKeyword(ref lexer, "obj"u8)
            || number is 0 or > int.MaxValue
            || generation > CosReference.MaxGeneration)
        {
            return null;
        }

        return loader.Load(new CosReference((int)number, (int)generation)) as CosStream;
    }

    private static PdfLinearizationHints? Invalid(DiagnosticSink diagnostics, string message)
    {
        diagnostics.Report(DiagnosticCodes.LinearizationHintsInvalid, DiagnosticSeverity.Warning, message + " The hints are ignored.");
        return null;
    }

    private static long ReadInteger(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && value is CosInteger integer ? integer.Value : 0;
}
