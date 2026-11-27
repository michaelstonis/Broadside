using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Text.Editing;

/// <summary>
/// The ToUnicode entries a text edit adds (issue #641): codes found through a font program's "cmap" that the font's ToUnicode CMap
/// does not map, so text extraction after the edit reads the new text.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.10.3: a ToUnicode CMap's <c>beginbfchar</c> blocks map single codes, written as many bytes as the font's codes, to
/// UTF-16BE text; a CMap may hold several blocks. The extended CMap is a new stream the font dictionary points to, so another font
/// that shares the old stream keeps it; a font without one gets a CMap of the Identity code space that maps only the new codes, the
/// others read as before (§9.10.2's later methods). Pages that share the font gain the entries, which change nothing they show.
/// </remarks>
internal sealed class ToUnicodeExtension
{
    private readonly Dictionary<PdfFont, SortedDictionary<uint, (int Length, string Text)>> _entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets a value indicating whether there is nothing to write.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Adds the entry for a code.</summary>
    public void Add(PdfFont font, EncodedCode code, string text)
    {
        if (!_entries.TryGetValue(font, out SortedDictionary<uint, (int, string)>? entries))
        {
            entries = [];
            _entries[font] = entries;
        }

        entries[code.Value] = (code.Length, text);
    }

    /// <summary>Writes each font's extended CMap and points the font at it; returns the fonts changed.</summary>
    public List<PdfFont> Apply(PdfDocument document)
    {
        List<PdfFont> changed = [];
        foreach ((PdfFont font, SortedDictionary<uint, (int Length, string Text)> entries) in _entries)
        {
            CosObject? raw = font.Dictionary.TryGetValue(CompositeFontNames.ToUnicode, out CosObject? value) ? value : null;
            string block = Block(entries);
            string cmap;
            if (document.Resolve(raw) is CosStream existing)
            {
                string data = Encoding.Latin1.GetString(document.DecodeStream(existing).Span);
                int end = data.LastIndexOf("endcmap", StringComparison.Ordinal);
                cmap = end >= 0 ? string.Concat(data.AsSpan(0, end), block, data.AsSpan(end)) : data + "\n" + block;
            }
            else
            {
                int length = entries.Values.First().Length;
                string low = new('0', 2 * length), high = new('F', 2 * length);
                cmap = "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n"
                    + "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n"
                    + $"1 begincodespacerange\n<{low}> <{high}>\nendcodespacerange\n" + block
                    + "endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n";
            }

            font.Dictionary[CompositeFontNames.ToUnicode] = document.AddObject(new CosStream(new CosDictionary(), Encoding.Latin1.GetBytes(cmap)));
            changed.Add(font);
        }

        _entries.Clear();
        return changed;
    }

    private static string Block(SortedDictionary<uint, (int Length, string Text)> entries)
    {
        var builder = new StringBuilder();
        List<KeyValuePair<uint, (int Length, string Text)>> list = [.. entries];
        for (int start = 0; start < list.Count; start += 100)
        {
            int count = Math.Min(100, list.Count - start);
            builder.Append(CultureInfo.InvariantCulture, $"{count} beginbfchar\n");
            for (int index = start; index < start + count; index++)
            {
                (uint code, (int length, string text)) = (list[index].Key, list[index].Value);
                builder.Append('<').Append(code.ToString("X" + (2 * length).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)).Append("> <");
                foreach (char unit in text)
                {
                    builder.Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                }

                builder.Append(">\n");
            }

            builder.Append("endbfchar\n");
        }

        return builder.ToString();
    }
}
