using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>One page label range: the pages from <see cref="StartPageIndex"/> to the next range, labelled by one page label dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.4.2, Table 161. Page <c>i</c> of a range that starts at page index <c>k</c> is labelled <see cref="Prefix"/>
/// followed by the number <see cref="FirstNumber"/> + (<c>i</c> − <c>k</c>) written in <see cref="Style"/>. Letters repeat rather
/// than carry: A to Z, then AA to ZZ, then AAA. Roman numerals beyond 3999 have no standard form; like Acrobat, an M is written per
/// thousand.
/// </para>
/// <para>
/// A live view over the page label dictionary. Deviations are repaired and reported when read: an unknown style reads as decimal
/// (<c>PageLabelStyleInvalid</c>), a start that is not a positive integer as 1 (an integral real as its value;
/// <c>PageLabelStartInvalid</c>), a prefix that is not a text string as empty and one holding U+0000 up to it
/// (<c>PageLabelPrefixInvalid</c>), a <c>Type</c> other than <c>PageLabel</c> is ignored (<c>PageLabelInvalid</c>).
/// </para>
/// </remarks>
public sealed class PdfPageLabelRange
{
    /// <summary>The longest label written in letters or roman numerals; a longer one is written in decimal instead.</summary>
    internal const int MaxNumeralLength = 64;

    private static readonly CosName SKey = new("S");
    private static readonly CosName PKey = new("P");
    private static readonly CosName StKey = new("St");
    private static readonly CosName PageLabelType = new("PageLabel");

    private readonly DictionaryView _view;

    internal PdfPageLabelRange(PdfDocument document, int startPageIndex, CosDictionary dictionary, CosReference? reference)
    {
        _view = new DictionaryView(document, dictionary, reference);
        StartPageIndex = startPageIndex;
    }

    /// <summary>Gets the index of the first page of the range (the number tree key).</summary>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public int StartPageIndex { get; }

    /// <summary>Gets the page label dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public CosDictionary Dictionary => _view.Dictionary;

    /// <summary>Gets the numbering style (<c>S</c>); <see cref="PdfPageLabelStyle.None"/> when absent: the label is the prefix alone.</summary>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public PdfPageLabelStyle Style
    {
        get
        {
            CheckType();
            CosObject? value = _view.Get(SKey);
            if (value is null)
            {
                return PdfPageLabelStyle.None;
            }

            switch ((value as CosName)?.Value)
            {
                case "D":
                    return PdfPageLabelStyle.Arabic;
                case "R":
                    return PdfPageLabelStyle.UppercaseRoman;
                case "r":
                    return PdfPageLabelStyle.LowercaseRoman;
                case "A":
                    return PdfPageLabelStyle.UppercaseLetters;
                case "a":
                    return PdfPageLabelStyle.LowercaseLetters;
                default:
                    _view.Report(DiagnosticCodes.PageLabelStyleInvalid, "A page label's S entry shall be D, R, r, A or a; the range is numbered in decimal.");
                    return PdfPageLabelStyle.Arabic;
            }
        }
    }

    /// <summary>Gets the label prefix (<c>P</c>); empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public string Prefix
    {
        get
        {
            switch (_view.Get(PKey))
            {
                case null:
                    return string.Empty;
                case CosString text:
                    string prefix = text.DecodeText();
                    int nul = prefix.IndexOf('\0', StringComparison.Ordinal);
                    if (nul < 0)
                    {
                        return prefix;
                    }

                    _view.Report(DiagnosticCodes.PageLabelPrefixInvalid, "A page label prefix holds U+0000; it is cut there.");
                    return prefix[..nul];
                default:
                    _view.Report(DiagnosticCodes.PageLabelPrefixInvalid, "A page label's P entry shall be a text string; it is ignored.");
                    return string.Empty;
            }
        }
    }

    /// <summary>Gets the number of the range's first page (<c>St</c>), at least 1; 1 when absent.</summary>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public int FirstNumber
    {
        get
        {
            switch (_view.Get(StKey))
            {
                case null:
                    return 1;
                case CosInteger { Value: >= 1 and <= int.MaxValue } start:
                    return (int)start.Value;
                case CosReal { Value: >= 1 and <= int.MaxValue } real when double.IsInteger(real.Value):
                    _view.Report(DiagnosticCodes.PageLabelStartInvalid, "A page label's St entry shall be an integer; it is a real holding an integral value, used as that integer.");
                    return (int)real.Value;
                default:
                    _view.Report(DiagnosticCodes.PageLabelStartInvalid, "A page label's St entry shall be an integer of 1 or more; the range starts at 1.");
                    return 1;
            }
        }
    }

    /// <summary>Returns the label of the page at <paramref name="offset"/> from the range's first page.</summary>
    internal string GetLabel(int offset) => CreateFormatter().Format(offset);

    /// <summary>Reads the range's style, prefix and first number once, for labelling many of its pages.</summary>
    internal LabelFormatter CreateFormatter() => new(this, Style, Prefix, FirstNumber);

    private static string Decimal(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A, ..., Z, AA, ..., ZZ, AAA: the letter for (n − 1) mod 26, repeated (n − 1) / 26 + 1 times.</summary>
    private static string? Letters(long number, char a)
    {
        long count = ((number - 1) / 26) + 1;
        return count > MaxNumeralLength ? null : new string((char)(a + ((number - 1) % 26)), (int)count);
    }

    /// <summary>Subtractive roman numerals; one M per thousand, so 4000 is MMMM.</summary>
    private static string? Roman(long number, bool upper)
    {
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        if (number / 1000 > MaxNumeralLength / 2)
        {
            return null;
        }

        var text = new StringBuilder();
        for (int index = 0; index < values.Length; index++)
        {
            while (number >= values[index])
            {
                text.Append(symbols[index]);
                number -= values[index];
            }
        }

        return upper ? text.ToString() : text.ToString().ToLowerInvariant();
    }

    private void CheckType()
    {
        if (_view.Get(KnownNames.Type) is { } type && !PageLabelType.Equals(type))
        {
            _view.Report(DiagnosticCodes.PageLabelInvalid, "A page label dictionary's Type entry, if present, shall be PageLabel; it is ignored.");
        }
    }

    /// <summary>
    /// Labels pages of one range from its entries read once: the prefix is decoded once and shared, so a range without a numbering
    /// style gives every page the same string.
    /// </summary>
    internal sealed class LabelFormatter(PdfPageLabelRange range, PdfPageLabelStyle style, string prefix, int firstNumber)
    {
        /// <summary>Returns the label of the page at <paramref name="offset"/> from the range's first page.</summary>
        public string Format(int offset)
        {
            if (style == PdfPageLabelStyle.None)
            {
                return prefix;
            }

            long number = firstNumber + (long)offset;
            string? numeral = style switch
            {
                PdfPageLabelStyle.UppercaseRoman => Roman(number, upper: true),
                PdfPageLabelStyle.LowercaseRoman => Roman(number, upper: false),
                PdfPageLabelStyle.UppercaseLetters => Letters(number, 'A'),
                PdfPageLabelStyle.LowercaseLetters => Letters(number, 'a'),
                _ => Decimal(number),
            };
            if (numeral is null)
            {
                range._view.Report(DiagnosticCodes.PageLabelTooLong, "A page number is too large to write in letters or roman numerals; it is written in decimal.");
                numeral = Decimal(number);
            }

            return prefix.Length == 0 ? numeral : prefix + numeral;
        }
    }
}
