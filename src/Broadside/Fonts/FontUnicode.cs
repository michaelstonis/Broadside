using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>The character-code-to-Unicode state shared by the font kinds: the ToUnicode CMap and where it came from.</summary>
/// <remarks>ISO 32000-2 §9.10.2 and §9.10.3. Immutable once built; a font rebuilds it when a source changes.</remarks>
internal abstract class FontUnicode
{
    /// <summary>The most UTF-16 units one code maps to: a 512-byte ToUnicode destination plus an incremented scalar, or a long glyph name.</summary>
    public const int MaxLength = 512;

    /// <summary>The replacement character an unmapped code yields.</summary>
    public const char Replacement = '�';

    private readonly CosStream? _stream;
    private readonly int _version;

    private protected FontUnicode(PdfFont font)
    {
        CosObject? raw = font.Dictionary.TryGetValue(CompositeFontNames.ToUnicode, out CosObject? value) ? value : null;
        CosObject? resolved = font.Document.Resolve(raw);
        _stream = resolved as CosStream;
        _version = _stream?.Version ?? 0;
        ToUnicode = font.Document.GetToUnicode(resolved, raw as CosReference, font);
    }

    /// <summary>Gets the font's ToUnicode CMap, or <see langword="null"/>.</summary>
    public ToUnicodeMap? ToUnicode { get; }

    /// <summary>Gets a value indicating whether the ToUnicode stream is unchanged since the state was built.</summary>
    protected bool IsStreamCurrent => _stream is null || _stream.Version == _version;

    /// <summary>Writes U+FFFD for an unmapped code.</summary>
    public static int WriteReplacement(Span<char> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        destination[0] = Replacement;
        return 1;
    }
}

/// <summary>The Unicode text of the 256 codes of a simple font (§9.10.2: ToUnicode, then the glyph name through the Adobe Glyph List).</summary>
internal sealed class SimpleFontUnicode : FontUnicode
{
    private static readonly Entry ReplacementEntry = new(Replacement.ToString(), UnicodeSource.Unmapped, Flags.None);
    private static readonly Entry PendingEntry = new(Replacement.ToString(), UnicodeSource.Unmapped, Flags.ProgramPending);

    private readonly Entry[] _entries = new Entry[256];

    private SimpleFontUnicode(PdfSimpleFont font, SimpleFontMetrics metrics)
        : base(font)
    {
        Metrics = metrics;
        bool zapfDingbats = font.Standard14 == Standard14Font.ZapfDingbats
            || (font.FaceName is { } face && Standard14Data.TryMatch(face, out Standard14Font matched, out _) && matched == Standard14Font.ZapfDingbats);
        Span<char> buffer = stackalloc char[MaxLength];
        for (int code = 0; code < 256; code++)
        {
            if (ToUnicode is { } map && map.TryMap((uint)code, 1, buffer, out int written, out bool mismatch))
            {
                _entries[code] = new Entry(Text(buffer[..written]), UnicodeSource.ToUnicode, mismatch ? Flags.LengthMismatch : Flags.None);
                continue;
            }

            int named = AdobeGlyphList.MapName(metrics.Names[code], zapfDingbats, buffer, out bool nonStandard);
            _entries[code] = named > 0
                ? new Entry(Text(buffer[..named]), UnicodeSource.GlyphName, nonStandard ? Flags.NonStandardName : Flags.None)
                : PendingEntry;
        }
    }

    [Flags]
    private enum Flags : byte
    {
        None = 0,
        LengthMismatch = 1,
        NonStandardName = 2,
        ProgramPending = 4,
    }

    /// <summary>Gets the metrics (encoding) the text was worked out from.</summary>
    public SimpleFontMetrics Metrics { get; }

    /// <summary>Builds the state of a simple font.</summary>
    public static SimpleFontUnicode Build(PdfSimpleFont font, SimpleFontMetrics metrics) => new(font, metrics);

    /// <summary>Gets a value indicating whether the state is still the font's: same metrics and an unchanged ToUnicode stream.</summary>
    public bool IsCurrent(SimpleFontMetrics metrics) => ReferenceEquals(metrics, Metrics) && IsStreamCurrent;

    /// <summary>Maps a code, recording the deviations its mapping involved once per font.</summary>
    public int Map(PdfSimpleFont font, byte code, Span<char> destination, out UnicodeSource source)
    {
        Entry entry = Volatile.Read(ref _entries[code]);
        if ((entry.Flags & Flags.ProgramPending) != 0)
        {
            entry = ResolveFromProgram(font, code);
            Volatile.Write(ref _entries[code], entry);
        }

        source = entry.Source;
        if ((entry.Flags & Flags.LengthMismatch) != 0 && font.NeedsUnicodeReport(FontUnicodeReports.LengthMismatch, DiagnosticSeverity.Warning))
        {
            font.ReportUnicode(
                FontUnicodeReports.LengthMismatch,
                DiagnosticCodes.ToUnicodeCodeLengthMismatch,
                DiagnosticSeverity.Warning,
                "A simple font's ToUnicode CMap shall write its codes as one byte (ISO 32000-2 §9.10.3); the codes are matched by value.");
        }
        else if ((entry.Flags & Flags.NonStandardName) != 0 && font.NeedsUnicodeReport(FontUnicodeReports.NonStandardName, DiagnosticSeverity.Information))
        {
            font.ReportUnicode(
                FontUnicodeReports.NonStandardName,
                DiagnosticCodes.GlyphNameNonStandard,
                DiagnosticSeverity.Information,
                $"The glyph name /{Metrics.Names[code]} uses lowercase hexadecimal digits, which the Adobe Glyph List specification does not (ISO 32000-2 §9.10.2); read as written.");
        }
        else if (source == UnicodeSource.Unmapped)
        {
            font.ReportUnmapped(code, 1);
        }

        return ToUnicodeMap.Copy(entry.Text, destination);
    }

    private static string Text(ReadOnlySpan<char> text) => text.IsEmpty ? string.Empty : new string(text);

    /// <summary>The fourth method, beyond §9.10.2: the code point the font's program gives the code's glyph, when the font kind has one.</summary>
    private static Entry ResolveFromProgram(PdfSimpleFont font, byte code) =>
        font.TryGetProgramCodePoint(code, out int codePoint)
            ? new Entry(char.ConvertFromUtf32(codePoint), UnicodeSource.FontProgram, Flags.None)
            : ReplacementEntry;

    /// <summary>The text of one code, how it was found, and what to report about it.</summary>
    private sealed record Entry(string Text, UnicodeSource Source, Flags Flags);
}

/// <summary>
/// The Unicode state of a Type 0 font (§9.10.2): its ToUnicode CMap, and for a font of a known character collection the
/// Registry-Ordering-UCS2 table its CIDs map through.
/// </summary>
internal sealed class Type0FontUnicode : FontUnicode
{
    private static readonly string[] Collections = ["GB1", "CNS1", "Japan1", "Korea1", "KR"];

    private Type0FontUnicode(PdfType0Font font, Type0FontState state)
        : base(font)
    {
        State = state;
        CidSystemInfo? collection = null;
        if (font.Get(FontNames.Encoding) is CosName { Value: not ("Identity-H" or "Identity-V") } encoding && PredefinedCMapTable.Contains(encoding.Value))
        {
            collection = state.Encoding.SystemInfo;
        }

        if (collection is null && state.Descendant?.SystemInfo is { Registry: "Adobe" } info && Collections.Contains(info.Ordering))
        {
            collection = info;
        }

        if (collection is not null)
        {
            Ucs2Name = $"{collection.Registry}-{collection.Ordering}-UCS2";
            Ucs2 = font.Document.FindCidToUnicode(Ucs2Name);
        }
    }

    /// <summary>Gets the CMap and descendant the state was built for.</summary>
    public Type0FontState State { get; }

    /// <summary>Gets the name of the UCS2 table the font's CIDs map through (§9.10.2 step c), or <see langword="null"/> when the third method does not apply.</summary>
    public string? Ucs2Name { get; }

    /// <summary>Gets the UCS2 table, or <see langword="null"/> when it does not apply or no font resolver has it.</summary>
    public ToUnicodeMap? Ucs2 { get; }

    /// <summary>Builds the state of a Type 0 font.</summary>
    public static Type0FontUnicode Build(PdfType0Font font, Type0FontState state) => new(font, state);

    /// <summary>Gets a value indicating whether the state is still the font's.</summary>
    public bool IsCurrent(Type0FontState state) => ReferenceEquals(state, State) && IsStreamCurrent;

    /// <summary>Maps a code, recording the deviations its mapping involved once per font.</summary>
    public int Map(PdfType0Font font, CharacterCode code, Span<char> destination, out UnicodeSource source)
    {
        if (ToUnicode is { } map && map.TryMap(code.Value, code.Length, destination, out int written, out bool mismatch))
        {
            if (mismatch && font.NeedsUnicodeReport(FontUnicodeReports.LengthMismatch, DiagnosticSeverity.Warning))
            {
                font.ReportUnicode(
                    FontUnicodeReports.LengthMismatch,
                    DiagnosticCodes.ToUnicodeCodeLengthMismatch,
                    DiagnosticSeverity.Warning,
                    $"The font's ToUnicode CMap maps code 0x{code.Value:X} only under another code length than the {code.Length} byte(s) its CMap reads (ISO 32000-2 §9.10.3); matched by value.");
            }

            source = UnicodeSource.ToUnicode;
            return written;
        }

        CMap cmap = State.Encoding;
        int cid = cmap.GetCid(code);
        bool missing = false;
        if (Ucs2Name is not null)
        {
            if (Ucs2 is { } table)
            {
                if (cid != 0 && table.TryMap((uint)cid, 2, destination, out written, out _) && !(written == 1 && destination[0] == Replacement))
                {
                    source = UnicodeSource.CidCollection;
                    return written;
                }
            }
            else
            {
                missing = true;
                if (font.NeedsUnicodeReport(FontUnicodeReports.Ucs2Missing, DiagnosticSeverity.Information))
                {
                    font.ReportUnicode(
                    FontUnicodeReports.Ucs2Missing,
                    DiagnosticCodes.TextUcs2CmapMissing,
                    DiagnosticSeverity.Information,
                    $"The font's text maps through the {Ucs2Name} table (ISO 32000-2 §9.10.2), which is not available: add the Broadside.Fonts.Cmaps package and call options.UsePredefinedCMaps(), or register a font resolver that supplies it.");
                }
            }
        }

        if (State.Descendant is { } descendant && descendant.Program is { } program
            && descendant.TryGetGlyphId(cid, out int glyphId) && program.CharacterMapSelection.TryGetCodePoint(glyphId, out int codePoint))
        {
            source = UnicodeSource.FontProgram;
            return new System.Text.Rune(codePoint).TryEncodeToUtf16(destination, out written) ? written : 0;
        }

        source = UnicodeSource.Unmapped;
        if (!missing)
        {
            font.ReportUnmapped(code.Value, code.Length);
        }

        return WriteReplacement(destination);
    }
}

/// <summary>The once-per-font Unicode diagnostics, as bits of <see cref="PdfFont"/>'s report flags.</summary>
internal static class FontUnicodeReports
{
    public const int Unmapped = 1;
    public const int LengthMismatch = 2;
    public const int NonStandardName = 4;
    public const int Ucs2Missing = 8;
}
