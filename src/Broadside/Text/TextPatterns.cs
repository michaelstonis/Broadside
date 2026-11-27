using System.Text.RegularExpressions;

namespace Broadside.Text;

/// <summary>
/// Ready-made queries for personal data that redaction jobs commonly look for: email addresses, phone numbers, US Social Security
/// numbers, payment card numbers, IBANs, IPv4 addresses and URLs. Each property returns a new <see cref="TextQuery"/>, which the
/// caller may change.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.10: the patterns run on the text a page shows, in which the words of a line and the lines are joined by single
/// spaces (<see cref="SearchableText.Text"/>), so a value broken across two lines still matches when its parts are separated by
/// characters the pattern allows. Each is a regular expression with <see cref="RegexOptions.CultureInvariant"/>; those with a check
/// digit or reserved ranges reject the rest in <see cref="TextQuery.MatchFilter"/>.
/// </para>
/// <para>
/// They are starting points, not a guarantee: each finds the common written forms and will miss others (a number written in words,
/// one split by unexpected punctuation) and match a few values that are not personal data (a part number that happens to pass the
/// Luhn check). Review the matches, with <see cref="PdfDocument.AddRedactAnnotations"/>, before applying them to documents whose
/// contents are not known.
/// </para>
/// </remarks>
public static class TextPatterns
{
    // A local part, an @ and a domain of at least two labels whose last one is letters.
    private const string EmailPattern =
        @"(?<![\w.%+-])[A-Za-z0-9](?:[A-Za-z0-9._%+-]{0,62}[A-Za-z0-9_%+-])?@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?![\w-])";

    // E.164 (+ and 7 to 15 digits, in groups), North American (NXX) NXX-XXXX with an optional +1 or 1, and European national forms
    // starting with a 0 trunk prefix, grouped by spaces, slashes or hyphens; the filter counts the digits.
    private const string PhonePattern =
        @"(?<![\w+])(?:"
        + @"\+[1-9]\d{0,3}(?:[ .\-]?\(?\d{1,4}\)?){2,6}"
        + @"|(?:1[ .\-]?)?(?:\(\d{3}\)|\d{3})[ .\-]?\d{3}[ .\-]\d{4}"
        + @"|0\d{1,4}(?:[ /\-]?\d{2,4}){2,4}"
        + @")(?![\w])";

    // AAA-GG-SSSS, with hyphens or spaces between the groups; the filter rejects the reserved area and group numbers.
    private const string SsnPattern = @"(?<![\w-])(\d{3})([ \-])(\d{2})\2(\d{4})(?![\w-])";

    // 13 to 19 digits, single spaces or hyphens allowed between them and no digit group just before or after; the filter counts them
    // and runs the Luhn check.
    private const string CardPattern = @"(?<!\d[ \-]?)\d(?:[ \-]?\d){12,18}(?![ \-]?\d)";

    // A country code, two check digits and up to 30 letters or digits, in groups of four when printed; the filter runs mod 97.
    private const string IbanPattern = @"(?<![A-Za-z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]){11,30}(?![A-Za-z0-9])";

    // Four decimal octets 0 to 255, not part of a longer dotted number.
    private const string IPv4Pattern =
        @"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\.?\d)";

    // http, https or ftp with ://, or a host starting with www.; trailing punctuation of the sentence is left out.
    private const string UrlPattern =
        @"(?<![\w@/])(?:(?:https?|ftp)://|www\.)[^\s<>""'`]*[^\s<>""'`.,;:!?)\]}]";

    private static readonly Regex Email = Build(EmailPattern, ignoreCase: true);
    private static readonly Regex Phone = Build(PhonePattern, ignoreCase: false);
    private static readonly Regex Ssn = Build(SsnPattern, ignoreCase: false);
    private static readonly Regex Card = Build(CardPattern, ignoreCase: false);
    private static readonly Regex IbanExpression = Build(IbanPattern, ignoreCase: false);
    private static readonly Regex IPv4 = Build(IPv4Pattern, ignoreCase: false);
    private static readonly Regex UrlExpression = Build(UrlPattern, ignoreCase: true);

    /// <summary>Gets a new query for email addresses: a local part, <c>@</c>, and a domain whose last label is letters (<c>name@example.com</c>).</summary>
    /// <remarks>ISO 32000-2 §9.10. Ignores case. Quoted local parts and addresses with an IP literal are not found.</remarks>
    public static TextQuery EmailAddress => Create(Email, null);

    /// <summary>
    /// Gets a new query for phone numbers: E.164 with a leading <c>+</c> (<c>+44 20 7946 0958</c>), North American numbers
    /// (<c>(555) 010-0199</c>, <c>555-010-0199</c>, <c>+1 555 010 0199</c>) and European national numbers with a leading 0
    /// (<c>030 1234567</c>, <c>01 23 45 67 89</c>).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.10. A match has 7 to 15 digits (E.164's maximum), 9 or more when it starts with the trunk prefix 0. Numbers
    /// written with no separators and no <c>+</c>, and groups separated by periods, are not found, so that order numbers and dates are
    /// not taken for phone numbers.
    /// </remarks>
    public static TextQuery PhoneNumber => Create(Phone, static match => IsPhoneNumber(match.Text));

    /// <summary>Gets a new query for US Social Security numbers written <c>AAA-GG-SSSS</c> or <c>AAA GG SSSS</c>.</summary>
    /// <remarks>
    /// ISO 32000-2 §9.10. Numbers the Social Security Administration never issues are rejected: area 000, 666 or 900 to 999, group
    /// 00 and serial 0000. Nine digits with no separator are not found.
    /// </remarks>
    public static TextQuery UsSocialSecurityNumber => Create(Ssn, static match => IsSocialSecurityNumber(match.Text));

    /// <summary>
    /// Gets a new query for payment card numbers: 13 to 19 digits, optionally grouped by single spaces or hyphens
    /// (<c>4111 1111 1111 1111</c>), that pass the Luhn check (ISO/IEC 7812-1).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10. A number that fails the check digit is not returned.</remarks>
    public static TextQuery CreditCardNumber => Create(Card, static match => IsLuhnValid(match.Text));

    /// <summary>
    /// Gets a new query for International Bank Account Numbers (ISO 13616): a two-letter country code, two check digits and the
    /// account, 15 to 34 characters, printed in groups of four or not (<c>GB82 WEST 1234 5698 7654 32</c>), whose check digits pass
    /// the mod 97 test (ISO 7064).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10. Case is matched: IBANs are printed in capitals.</remarks>
    public static TextQuery Iban => Create(IbanExpression, static match => IsIbanValid(match.Text));

    /// <summary>Gets a new query for IPv4 addresses in dotted decimal (<c>192.0.2.10</c>), each part 0 to 255.</summary>
    /// <remarks>ISO 32000-2 §9.10. A version number with four parts (<c>1.2.3.4</c>) is matched too.</remarks>
    public static TextQuery IPv4Address => Create(IPv4, null);

    /// <summary>
    /// Gets a new query for web addresses that start with <c>http://</c>, <c>https://</c>, <c>ftp://</c> or <c>www.</c>, up to the
    /// next white space; a trailing period, comma or closing bracket is left out.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10. Ignores case. A bare domain (<c>example.com</c>) is not matched.</remarks>
    public static TextQuery Url => Create(UrlExpression, null);

    /// <summary>Whether a text's digits pass the Luhn check and number 13 to 19.</summary>
    internal static bool IsLuhnValid(ReadOnlySpan<char> text)
    {
        int sum = 0, count = 0;
        for (int index = text.Length - 1; index >= 0; index--)
        {
            char c = text[index];
            if (!char.IsAsciiDigit(c))
            {
                continue;
            }

            int digit = c - '0';
            if (count++ % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return count is >= 13 and <= 19 && sum % 10 == 0;
    }

    /// <summary>Whether a text is an IBAN whose check digits pass ISO 7064 mod 97-10, spaces ignored.</summary>
    internal static bool IsIbanValid(ReadOnlySpan<char> text)
    {
        Span<char> compact = stackalloc char[34];
        int length = 0;
        foreach (char c in text)
        {
            if (c == ' ')
            {
                continue;
            }

            if (length == compact.Length || !char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }

            compact[length++] = char.ToUpperInvariant(c);
        }

        if (length < 15 || !char.IsAsciiLetter(compact[0]) || !char.IsAsciiLetter(compact[1]) || !char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3]))
        {
            return false;
        }

        // The first four characters move to the end; letters count as 10 to 35; the remainder of the number by 97 is 1.
        int remainder = 0;
        for (int step = 0; step < length; step++)
        {
            char c = compact[(step + 4) % length];
            int value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value >= 10 ? ((remainder * 100) + value) % 97 : ((remainder * 10) + value) % 97;
        }

        return remainder == 1;
    }

    /// <summary>Whether <c>AAA-GG-SSSS</c> (any one separator) is a number that can be issued.</summary>
    internal static bool IsSocialSecurityNumber(ReadOnlySpan<char> text)
    {
        if (text.Length != 11)
        {
            return false;
        }

        int area = Number(text[..3]), group = Number(text.Slice(4, 2)), serial = Number(text[7..]);
        return area is > 0 and < 900 and not 666 && group > 0 && serial > 0;
    }

    private static int Number(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char c in digits)
        {
            value = (value * 10) + (c - '0');
        }

        return value;
    }

    private static int CountDigits(ReadOnlySpan<char> text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether a phone number candidate has a plausible number of digits: 7 to 15, at least 9 after a trunk prefix 0.</summary>
    internal static bool IsPhoneNumber(ReadOnlySpan<char> text)
    {
        int digits = CountDigits(text);
        return digits is >= 7 and <= 15 && (text.IsEmpty || text[0] != '0' || digits >= 9);
    }

    private static Regex Build(string pattern, bool ignoreCase) =>
        new(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(2));

    // Regex is safe to share between threads; with TextQuery's default timeout, each query runs the shared instance until its case or
    // timeout changes.
    private static TextQuery Create(Regex regex, Func<TextMatch, bool>? filter) => TextQuery.FromRegex(regex).WithMatchFilter(filter);
}
