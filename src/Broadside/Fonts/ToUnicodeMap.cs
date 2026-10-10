using System.Text;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// A ToUnicode CMap (or a Registry-Ordering-UCS2 table): character codes to Unicode text. Immutable once built and shared across
/// threads; a lookup is a binary search and allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.10.3; Adobe TN 5014 §7.4. A <c>bfchar</c> maps one code to a string; a <c>bfrange</c> with a string maps its
/// first code to the string and each next code to the string with its last character incremented; a <c>bfrange</c> with an array
/// maps each code to its own element. Ranges are kept as ranges, never expanded code by code, so <c>&lt;0000&gt; &lt;FFFF&gt;
/// &lt;0000&gt;</c> is one entry. Where entries overlap, the later one wins (TN 5014 §5.2); a CMap's own entries win over the one
/// it uses (<c>UseCMap</c>, <c>usecmap</c>).
/// </para>
/// <para>
/// The "last byte" rule of §9.10.3 (the increment never carries) is undefined when broken; producers rely on the carry, so the last
/// character is incremented as a whole scalar value (<c>&lt;0000&gt; &lt;01FF&gt; &lt;0000&gt;</c> maps 0x100 to U+0100, as
/// pdf.js and PDFBox read it). A result past U+10FFFF or in the surrogate range is U+FFFD.
/// </para>
/// </remarks>
internal sealed class ToUnicodeMap
{
    private readonly IntervalTable[] _tables;
    private readonly CMapFile.BfEntry[] _entries;
    private readonly char[] _text;
    private readonly (int Start, int Length)[] _elements;
    private readonly bool _identity;

    private ToUnicodeMap(IntervalTable[] tables, CMapFile.BfEntry[] entries, char[] text, (int Start, int Length)[] elements, ToUnicodeMap? parent, bool identity)
    {
        _tables = tables;
        _entries = entries;
        _text = text;
        _elements = elements;
        Parent = parent;
        _identity = identity;
    }

    /// <summary>Gets the map a ToUnicode entry naming <c>Identity-H</c> or <c>Identity-V</c> stands for: each code is its own UTF-16 unit.</summary>
    public static ToUnicodeMap Identity { get; } = new([IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty, IntervalTable.Empty], [], [], [], parent: null, identity: true);

    /// <summary>Gets the ToUnicode CMap this one uses, or <see langword="null"/>.</summary>
    public ToUnicodeMap? Parent { get; }

    /// <summary>Gets the number of entries, the parent's not counted.</summary>
    public int Count => _entries.Length;

    /// <summary>Builds the map of a parsed ToUnicode CMap file.</summary>
    /// <param name="file">The file, read with Unicode destinations.</param>
    /// <param name="parent">The ToUnicode CMap it uses, if any.</param>
    /// <returns>The map.</returns>
    public static ToUnicodeMap Create(CMapFile file, ToUnicodeMap? parent)
    {
        var tables = new IntervalTable[4];
        for (int index = 0; index < 4; index++)
        {
            tables[index] = IntervalTable.Build(file.Bf[index], increment: false, laterWins: true);
        }

        return new ToUnicodeMap(tables, [.. file.BfEntries], [.. file.BfText], [.. file.BfArrayElements], parent, identity: false);
    }

    /// <summary>Parses a ToUnicode CMap or a UCS2 table outside a document: a <c>usecmap</c> can only name <c>Identity-H</c> or <c>Identity-V</c>.</summary>
    /// <param name="data">The CMap file.</param>
    /// <param name="context">Limits, reading mode and diagnostics.</param>
    /// <returns>The map.</returns>
    public static ToUnicodeMap Parse(ReadOnlySpan<byte> data, CMapContext context)
    {
        CMapFile file = CMapParser.Parse(data, context, unicodeDestinations: true);
        ToUnicodeMap? parent = null;
        if (file.UseCMapName is { } name)
        {
            if (name is "Identity-H" or "Identity-V")
            {
                parent = Identity;
            }
            else
            {
                context.Report(DiagnosticCodes.CMapUseCMapInvalid, DiagnosticSeverity.Warning, $"The ToUnicode CMap uses /{name}, which is not a ToUnicode CMap (ISO 32000-2 §9.10.3); read without it.");
            }
        }

        return Create(file, parent);
    }

    /// <summary>Maps a character code to its Unicode text.</summary>
    /// <param name="code">The code's value.</param>
    /// <param name="length">The code's length in bytes, 1 to 4.</param>
    /// <param name="destination">Where the UTF-16 text goes; text that does not fit is cut at a scalar boundary.</param>
    /// <param name="written">The number of UTF-16 units written (0 for a code mapped to an empty string).</param>
    /// <param name="lengthMismatch">Set when the code is mapped only under another code length than its own.</param>
    /// <returns>Whether the map has the code.</returns>
    /// <remarks>
    /// The codespace of a ToUnicode CMap is not used to split codes: the font's encoding does (§9.10.3 wants them consistent). A
    /// code is looked up under its own length first (in this map and then the one it uses), then by value under the other lengths,
    /// since simple fonts often come with two-byte ToUnicode codes and composite fonts with one-byte ones.
    /// </remarks>
    public bool TryMap(uint code, int length, Span<char> destination, out int written, out bool lengthMismatch)
    {
        lengthMismatch = false;
        if (length is >= 1 and <= 4)
        {
            for (ToUnicodeMap? map = this; map is not null; map = map.Parent)
            {
                if (map.TryMapExact(code, length, destination, out written))
                {
                    return true;
                }
            }
        }

        for (int other = 1; other <= 4; other++)
        {
            if (other == length || (other < 4 && code >> (8 * other) != 0))
            {
                continue;
            }

            for (ToUnicodeMap? map = this; map is not null; map = map.Parent)
            {
                if (!map._identity && map.TryMapExact(code, other, destination, out written))
                {
                    lengthMismatch = true;
                    return true;
                }
            }
        }

        written = 0;
        return false;
    }

    private bool TryMapExact(uint code, int length, Span<char> destination, out int written)
    {
        if (_identity)
        {
            if (code <= 0xFFFF)
            {
                written = Write(char.IsSurrogate((char)code) ? '�' : (char)code, destination);
                return true;
            }

            written = 0;
            return false;
        }

        if (!_tables[length - 1].TryFind(code, out int index))
        {
            written = 0;
            return false;
        }

        CMapFile.BfEntry entry = _entries[index];
        uint offset = code - entry.Low;
        if (entry.IsArray)
        {
            if (offset >= (uint)entry.Count)
            {
                written = 0;
                return false;
            }

            (int start, int count) = _elements[entry.Start + (int)offset];
            written = Copy(_text.AsSpan(start, count), destination);
            return true;
        }

        ReadOnlySpan<char> text = _text.AsSpan(entry.Start, entry.Count);
        if (offset == 0 || text.IsEmpty)
        {
            written = Copy(text, destination);
            return true;
        }

        int last = text.Length >= 2 && char.IsSurrogatePair(text[^2], text[^1]) ? 2 : 1;
        int scalar = last == 2 ? char.ConvertToUtf32(text[^2], text[^1]) : text[^1];
        long value = scalar + (long)offset;
        int prefix = Copy(text[..^last], destination);
        if (prefix < text.Length - last)
        {
            written = prefix;
            return true;
        }

        Rune rune = value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF ? Rune.ReplacementChar : new Rune((int)value);
        written = prefix + (rune.TryEncodeToUtf16(destination[prefix..], out int units) ? units : 0);
        return true;
    }

    private static int Write(char unit, Span<char> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        destination[0] = unit;
        return 1;
    }

    /// <summary>Copies UTF-16 text, cutting it where it does not fit without splitting a surrogate pair.</summary>
    internal static int Copy(ReadOnlySpan<char> text, Span<char> destination)
    {
        int count = Math.Min(text.Length, destination.Length);
        if (count < text.Length && count > 0 && char.IsHighSurrogate(text[count - 1]))
        {
            count--;
        }

        text[..count].CopyTo(destination);
        return count;
    }
}
