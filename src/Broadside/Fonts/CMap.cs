using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// A CMap: how the bytes of a string shown in a composite font split into character codes, and which CID each code selects.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.5 and §9.7.6.2-§9.7.6.3; Adobe TN 5014 §5 and §7.4 (the CMap file format); Adobe TN 5099 §1.3. The two
/// Identity CMaps (§9.7.5.2, Table 116) are built in (<see cref="IdentityH"/>, <see cref="IdentityV"/>); an embedded CMap stream is
/// parsed by <see cref="Parse(ReadOnlySpan{byte}, CMapContext)"/> or reached through <see cref="PdfType0Font.Encoding"/>. The other
/// predefined CMaps of Table 116 ship in a separate package.
/// </para>
/// <para>
/// A CMap that uses another (<c>usecmap</c>, <c>UseCMap</c>) keeps it as its <see cref="Parent"/>: its own mappings win, the
/// parent's apply to codes it does not map, and its codespace is its own ranges and the parent's. Where a CMap's own mappings
/// overlap, the later one wins (TN 5014 §5.2). Ranges are kept as ranges, never expanded code by code.
/// </para>
/// <para>
/// Thread safety: a CMap is immutable and shared by every font and thread that uses it. Reading a code and looking up its CID
/// allocate nothing.
/// </para>
/// </remarks>
public sealed class CMap
{
    private const int MaxCid = 0xFFFF;

    private readonly CodespaceRange[] _codespace;
    private readonly int[] _firstOfLength;
    private readonly IntervalTable[] _cids;
    private readonly IntervalTable[] _notdefs;
    private readonly int _minLength;
    private readonly bool _isIdentity;

    private CMap(
        string? name,
        WritingMode writingMode,
        CidSystemInfo? systemInfo,
        CodespaceRange[] codespace,
        IntervalTable[] cids,
        IntervalTable[] notdefs,
        CMap? parent,
        bool isIdentity)
    {
        Name = name;
        WritingMode = writingMode;
        SystemInfo = systemInfo;
        Parent = parent;
        _isIdentity = isIdentity;
        _cids = cids;
        _notdefs = notdefs;
        _codespace = [.. codespace.OrderBy(range => range.Length)];
        _firstOfLength = new int[6];
        for (int length = 1; length <= 5; length++)
        {
            int first = Array.FindIndex(_codespace, range => range.Length >= length);
            _firstOfLength[length] = first < 0 ? _codespace.Length : first;
        }

        _minLength = _codespace.Length > 0 ? _codespace[0].Length : 2;
    }

    /// <summary>Gets the predefined <c>Identity-H</c> CMap: two-byte codes 0000 to FFFF, each mapping to the CID of the same value, horizontal.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.2, Table 116; Adobe TN 5099 §1.5.</remarks>
    public static CMap IdentityH { get; } = CreateIdentity("Identity-H", WritingMode.Horizontal);

    /// <summary>Gets the predefined <c>Identity-V</c> CMap: as <see cref="IdentityH"/>, for vertical writing.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.2, Table 116; Adobe TN 5099 §1.5.</remarks>
    public static CMap IdentityV { get; } = CreateIdentity("Identity-V", WritingMode.Vertical);

    /// <summary>Gets the CMap's name (<c>CMapName</c>), or <see langword="null"/> when the file gives none.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.3, Table 118; Adobe TN 5014 §7.3.</remarks>
    public string? Name { get; }

    /// <summary>Gets the writing mode the CMap selects (<c>WMode</c>); horizontal unless the file says 1.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.3, Table 118; Adobe TN 5014 §5.2: the file's value wins over the stream dictionary's and is not inherited through <c>usecmap</c>.</remarks>
    public WritingMode WritingMode { get; }

    /// <summary>Gets the character collection the CMap maps to (<c>CIDSystemInfo</c>), or <see langword="null"/> when the file gives none.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, Table 114, and §9.7.5.3, Table 118.</remarks>
    public CidSystemInfo? SystemInfo { get; }

    /// <summary>Gets the CMap this one uses (<c>usecmap</c>, the stream's <c>UseCMap</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.3, Table 118, and §9.7.5.4 (a); Adobe TN 5014 §5.4.</remarks>
    public CMap? Parent { get; }

    /// <summary>Gets a value indicating whether this is one of the built-in Identity CMaps.</summary>
    /// <remarks>ISO 32000-2 §9.7.5.2.</remarks>
    public bool IsIdentity => _isIdentity;

    /// <summary>Parses a CMap file, such as the data of an embedded CMap stream.</summary>
    /// <param name="data">The CMap file, decoded.</param>
    /// <param name="context">Limits, reading mode and the diagnostics sink; <see langword="null"/> for a lenient stand-alone parse.</param>
    /// <returns>The CMap. Damage never fails the parse in lenient mode: what can be read is kept and each repair recorded.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation from the CMap file format.</exception>
    /// <remarks>
    /// <para>
    /// Adobe TN 5014 §7 (lexical elements, operators, file order) and ISO 32000-2 §9.7.5.4: <c>begincodespacerange</c>,
    /// <c>begincidrange</c>, <c>begincidchar</c>, <c>beginnotdefrange</c>, <c>beginnotdefchar</c>, <c>usecmap</c>, <c>CMapName</c>,
    /// <c>WMode</c> and <c>CIDSystemInfo</c>. <c>beginbfchar</c> and <c>beginbfrange</c> are not allowed in a CMap used as an encoding
    /// (§9.7.5.4 c): their destinations are read as CIDs, as viewers do, with a diagnostic. The block counts are not trusted: each
    /// block runs to its end operator. Operators glued to what follows them (<c>endcidchar1</c>) are split.
    /// </para>
    /// <para>
    /// A <c>usecmap</c> standing alone can only resolve <c>Identity-H</c> or <c>Identity-V</c>; another name of Table 116 is
    /// recorded as unavailable and only its codespace is used, and any other name is recorded and the CMap read without a parent.
    /// Inside a document, predefined CMaps come from the engine's font resolvers (the Broadside.Fonts.Cmaps package), and the
    /// stream's <c>UseCMap</c> entry may name another embedded CMap stream.
    /// </para>
    /// </remarks>
    public static CMap Parse(ReadOnlySpan<byte> data, CMapContext? context = null)
    {
        context ??= new CMapContext();
        CMapFile file = CMapParser.Parse(data, context);
        CMap? parent = null;
        if (file.UseCMapName is { } name)
        {
            parent = FindBuiltIn(name);
            if (parent is null)
            {
                parent = PredefinedCMapTable.CreateFallback(name);
                context.Report(
                    DiagnosticCodes.CMapUnavailable,
                    DiagnosticSeverity.Information,
                    parent is null
                        ? $"The CMap uses /{name}, which is not available (Adobe TN 5014 §7.4, usecmap); read without it."
                        : $"The CMap uses the predefined CMap /{name} (ISO 32000-2 §9.7.5.2, Table 116), whose mappings a stand-alone parse does not have; only its codespace is used.");
            }
        }

        return Create(file, parent, file.WMode ?? 0, context);
    }

    /// <summary>Reads the character code at the start of <paramref name="text"/>.</summary>
    /// <param name="text">The rest of a string shown in a composite font.</param>
    /// <returns>
    /// The code and its length. An invalid code (one in no codespace range) is returned with <see cref="CharacterCode.IsValid"/>
    /// <see langword="false"/> and the length of the codespace range that best matches its first bytes; never more bytes than
    /// <paramref name="text"/> has. An empty <paramref name="text"/> gives a code of length 0.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §9.7.6.2: one byte is matched against the one-byte codespace ranges, then two against the two-byte ranges, up to
    /// four. §9.7.6.3 for an invalid code: when no range admits its first byte, the shortest codes are used; otherwise the range
    /// matching the most leading bytes (the shortest of equals) gives the length. Allocates nothing.
    /// </remarks>
    public CharacterCode ReadCode(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty)
        {
            return default;
        }

        if (_isIdentity)
        {
            return text.Length >= 2
                ? new CharacterCode(BinaryPrimitives.ReadUInt16BigEndian(text), 2, IsValid: true)
                : new CharacterCode(text[0], 1, IsValid: false);
        }

        uint code = 0;
        int longest = Math.Min(4, text.Length);
        for (int length = 1; length <= longest; length++)
        {
            code = (code << 8) | text[length - 1];
            for (int index = _firstOfLength[length]; index < _firstOfLength[length + 1]; index++)
            {
                if (_codespace[index].Contains(code))
                {
                    return new CharacterCode(code, length, IsValid: true);
                }
            }
        }

        return ReadInvalidCode(text);
    }

    /// <summary>Returns the CID a code selects: its character mapping, else its notdef mapping, else 0.</summary>
    /// <param name="code">A code read by <see cref="ReadCode"/>.</param>
    /// <returns>The CID, 0 to 65,535; 0 for an invalid code.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.7.6.2 and §9.7.6.3. This is the CMap's part only: when the CIDFont has no glyph for the CID, the font consults
    /// the notdef mappings again (<see cref="TryGetNotdefCid"/>) and then uses CID 0.
    /// </remarks>
    public int GetCid(CharacterCode code) =>
        TryGetCid(code, out int cid) || TryGetNotdefCid(code, out cid) ? cid : 0;

    /// <summary>Looks up a code's character mapping (<c>cidchar</c>, <c>cidrange</c>), in this CMap and then its parents.</summary>
    /// <param name="code">A code read by <see cref="ReadCode"/>.</param>
    /// <param name="cid">The CID; 0 when there is none.</param>
    /// <returns>Whether the code is mapped; never for an invalid code or a CID past 65,535.</returns>
    /// <remarks>ISO 32000-2 §9.7.6.2; Adobe TN 5014 §5.4 (a using CMap's mappings win over its parent's).</remarks>
    public bool TryGetCid(CharacterCode code, out int cid)
    {
        if (code.IsValid && code.Length is >= 1 and <= 4)
        {
            for (CMap? cmap = this; cmap is not null; cmap = cmap.Parent)
            {
                if (cmap._isIdentity)
                {
                    if (code.Length == 2)
                    {
                        cid = (int)code.Value;
                        return true;
                    }
                }
                else if (cmap._cids[code.Length - 1].TryFind(code.Value, out cid))
                {
                    if (cid <= MaxCid)
                    {
                        return true;
                    }

                    break;
                }
            }
        }

        cid = 0;
        return false;
    }

    /// <summary>Looks up a code's notdef mapping (<c>notdefchar</c>, <c>notdefrange</c>), in this CMap and then its parents.</summary>
    /// <param name="code">A code read by <see cref="ReadCode"/>.</param>
    /// <param name="cid">The substitute CID; 0 when there is none.</param>
    /// <returns>Whether the code has a notdef mapping.</returns>
    /// <remarks>ISO 32000-2 §9.7.6.3; Adobe TN 5014 §7.4 (<c>beginnotdefchar</c>, <c>beginnotdefrange</c>).</remarks>
    public bool TryGetNotdefCid(CharacterCode code, out int cid)
    {
        if (code.IsValid && code.Length is >= 1 and <= 4)
        {
            for (CMap? cmap = this; cmap is not null; cmap = cmap.Parent)
            {
                if (cmap._notdefs[code.Length - 1].TryFind(code.Value, out cid) && cid <= MaxCid)
                {
                    return true;
                }
            }
        }

        cid = 0;
        return false;
    }

    /// <summary>Returns the built-in CMap of a predefined name, or <see langword="null"/> when the core does not have it.</summary>
    internal static CMap? FindBuiltIn(string name) => name switch
    {
        "Identity-H" => IdentityH,
        "Identity-V" => IdentityV,
        _ => null,
    };

    /// <summary>Builds a CMap from a parsed file and its parent.</summary>
    internal static CMap Create(CMapFile file, CMap? parent, int writingMode, CMapContext context)
    {
        var codespace = new List<CodespaceRange>(file.Codespace);
        if (parent is not null)
        {
            foreach (CodespaceRange range in parent._codespace)
            {
                if (!codespace.Contains(range))
                {
                    codespace.Add(range);
                }
            }
        }

        if (codespace.Count == 0)
        {
            DeriveCodespace(file, codespace);
            context.Report(
                DiagnosticCodes.CMapCodespaceMissing,
                DiagnosticSeverity.Warning,
                "The CMap defines no codespace range (Adobe TN 5014 §7.4, begincodespacerange); codes take the lengths its mappings use.");
        }

        var cids = new IntervalTable[4];
        var notdefs = new IntervalTable[4];
        for (int index = 0; index < 4; index++)
        {
            cids[index] = IntervalTable.Build(file.Cids[index], increment: true, laterWins: true);
            notdefs[index] = IntervalTable.Build(file.Notdefs[index], increment: false, laterWins: true);
        }

        return new CMap(file.Name, writingMode == 1 ? WritingMode.Vertical : WritingMode.Horizontal, file.SystemInfo, [.. codespace], cids, notdefs, parent, isIdentity: false);
    }

    /// <summary>A codespace for a CMap that has none: every byte value for each code length its mappings use, else two bytes.</summary>
    private static void DeriveCodespace(CMapFile file, List<CodespaceRange> codespace)
    {
        for (int index = 0; index < 4; index++)
        {
            if (file.Cids[index].Count > 0 || file.Notdefs[index].Count > 0)
            {
                uint high = index == 3 ? uint.MaxValue : (1u << (8 * (index + 1))) - 1;
                codespace.Add(new CodespaceRange(index + 1, 0, high));
            }
        }

        if (codespace.Count == 0)
        {
            codespace.Add(new CodespaceRange(2, 0, 0xFFFF));
        }
    }

    /// <summary>A predefined CMap without its mappings (§9.7.6.3: every code selects CID 0), for one no font resolver supplies.</summary>
    internal static CMap CreateFallback(string name, int writingMode, CidSystemInfo systemInfo, CodespaceRange[] codespace)
    {
        IntervalTable[] empty = [IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty];
        return new CMap(name, writingMode == 1 ? WritingMode.Vertical : WritingMode.Horizontal, systemInfo, codespace, empty, empty, parent: null, isIdentity: false);
    }

    private static CMap CreateIdentity(string name, WritingMode writingMode)
    {
        IntervalTable[] empty = [IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty];
        return new CMap(name, writingMode, new CidSystemInfo("Adobe", "Identity", 0), [new CodespaceRange(2, 0, 0xFFFF)], empty, empty, parent: null, isIdentity: true);
    }

    /// <summary>§9.7.6.3: the length of an invalid code from the codespace range that best matches its leading bytes.</summary>
    private CharacterCode ReadInvalidCode(ReadOnlySpan<byte> text)
    {
        int bestLength = 0;
        int bestMatched = 0;
        foreach (CodespaceRange range in _codespace)
        {
            int limit = Math.Min(range.Length, text.Length);
            int matched = 0;
            while (matched < limit && range.Admits(matched, text[matched]))
            {
                matched++;
            }

            if (matched > bestMatched || (matched == bestMatched && matched > 0 && range.Length < bestLength))
            {
                bestMatched = matched;
                bestLength = range.Length;
            }
        }

        int length = Math.Min(bestMatched == 0 ? _minLength : bestLength, text.Length);
        uint code = 0;
        for (int index = 0; index < length; index++)
        {
            code = (code << 8) | text[index];
        }

        return new CharacterCode(code, length, IsValid: false);
    }
}
