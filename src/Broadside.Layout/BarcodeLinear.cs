using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>The EAN-13 and UPC-A (ISO/IEC 15420), Code 39 (ISO/IEC 16388) and GS1-128 element string encoders.</summary>
internal static class BarcodeLinear
{
    /// <summary>The EAN/UPC number set A (odd parity) patterns of the digits 0 to 9, light first (ISO/IEC 15420 Table 1).</summary>
    internal static readonly string[] SetA =
    [
        "0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011",
    ];

    /// <summary>The parity of the left half's six digits by the leading digit of an EAN-13: A for set A, B for set B (ISO/IEC 15420 Table 3).</summary>
    internal static readonly string[] LeadingParity =
    [
        "AAAAAA", "AABABB", "AABBAB", "AABBBA", "ABAABB", "ABBAAB", "ABBBAA", "ABABAB", "ABABBA", "ABBABA",
    ];

    /// <summary>The Code 39 characters, in the order of their check character values 0 to 42 (ISO/IEC 16388 Table 1).</summary>
    internal const string Code39Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>
    /// The Code 39 patterns of <see cref="Code39Characters"/> and then the start/stop character <c>*</c>: nine elements, bar first, 1 for a
    /// wide element (ISO/IEC 16388 Table 1).
    /// </summary>
    internal static readonly string[] Code39Patterns =
    [
        "000110100", "100100001", "001100001", "101100000", "000110001", "100110000", "001110000", "000100101", "100100100", "001100100",
        "100001001", "001001001", "101001000", "000011001", "100011000", "001011000", "000001101", "100001100", "001001100", "000011100",
        "100000011", "001000011", "101000010", "000010011", "100010010", "001010010", "000000111", "100000110", "001000110", "000010110",
        "110000001", "011000001", "111000000", "010010001", "110010000", "011010000", "010000101", "110000100", "011000100", "010101000",
        "010100010", "010001010", "000101010", "010010100",
    ];

    /// <summary>The width of a Code 39 wide element in modules: three times the narrow one, the widest ratio ISO/IEC 16388 §4.3 allows.</summary>
    private const int Code39Wide = 3;

    /// <summary>
    /// The GS1 application identifiers whose data has a predefined length, so no FNC1 separator follows them: the first two digits of the
    /// identifier, its length in digits and its data's length (GS1 General Specifications, Figure 5.10.1-2).
    /// </summary>
    private static readonly Dictionary<string, (int Identifier, int Data)> PredefinedLengths = new(StringComparer.Ordinal)
    {
        ["00"] = (2, 18),
        ["01"] = (2, 14),
        ["02"] = (2, 14),
        ["03"] = (2, 14),
        ["04"] = (2, 16),
        ["11"] = (2, 6),
        ["12"] = (2, 6),
        ["13"] = (2, 6),
        ["14"] = (2, 6),
        ["15"] = (2, 6),
        ["16"] = (2, 6),
        ["17"] = (2, 6),
        ["18"] = (2, 6),
        ["19"] = (2, 6),
        ["20"] = (2, 2),
        ["31"] = (4, 6),
        ["32"] = (4, 6),
        ["33"] = (4, 6),
        ["34"] = (4, 6),
        ["35"] = (4, 6),
        ["36"] = (4, 6),
        ["41"] = (3, 13),
    };

    /// <summary>
    /// Checks the digits of an EAN-13 (12, or 13 with a check digit) and returns the 13 digits; the check digit is added or verified
    /// (ISO/IEC 15420 §5.2.2: weights 1 and 3 alternating from the left).
    /// </summary>
    public static string EanDigits(string digits, int length, string symbology)
    {
        if (digits.Length != length && digits.Length != length + 1)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"{symbology} takes {length} digits, or {length + 1} with the check digit; got {digits.Length} characters."),
                nameof(digits));
        }

        for (int index = 0; index < digits.Length; index++)
        {
            if (digits[index] is < '0' or > '9')
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"{symbology} encodes digits only; '{digits[index]}' at index {index} is not a digit."),
                    nameof(digits));
            }
        }

        char check = CheckDigit(digits.AsSpan(0, length));
        if (digits.Length == length + 1 && digits[length] != check)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The check digit of {symbology} {digits} is {digits[length]}; it should be {check}."),
                nameof(digits));
        }

        return string.Concat(digits.AsSpan(0, length), [check]);
    }

    /// <summary>The modulo 10 check digit of EAN/UPC data: weight 3 on the digit next to the check digit, alternating with 1 leftward.</summary>
    public static char CheckDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        for (int index = 0; index < digits.Length; index++)
        {
            int weight = (digits.Length - index) % 2 == 1 ? 3 : 1;
            sum += (digits[index] - '0') * weight;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>The 95 modules of an EAN-13 from its 13 digits: guards, six left digits by the leading digit's parity, center guard, six right digits.</summary>
    public static bool[] Ean13(string digits)
    {
        var modules = new StringBuilder(95);
        modules.Append("101");
        string parity = LeadingParity[digits[0] - '0'];
        for (int index = 1; index <= 6; index++)
        {
            string a = SetA[digits[index] - '0'];
            modules.Append(parity[index - 1] == 'A' ? a : SetB(a));
        }

        modules.Append("01010");
        for (int index = 7; index <= 12; index++)
        {
            modules.Append(SetC(SetA[digits[index] - '0']));
        }

        modules.Append("101");
        return ToModules(modules.ToString());
    }

    /// <summary>Number set C (the right half): set A with dark and light exchanged.</summary>
    internal static string SetC(string a) => string.Create(a.Length, a, static (span, a) =>
    {
        for (int index = 0; index < a.Length; index++)
        {
            span[index] = a[index] == '0' ? '1' : '0';
        }
    });

    /// <summary>Number set B (even parity): set C read backward.</summary>
    internal static string SetB(string a)
    {
        char[] c = SetC(a).ToCharArray();
        Array.Reverse(c);
        return new string(c);
    }

    /// <summary>Encodes Code 39 data between start and stop characters, with a narrow space between characters.</summary>
    public static bool[] Code39(string data, bool checkDigit, out string encoded)
    {
        int sum = 0;
        var text = new StringBuilder(data.Length + 1);
        for (int index = 0; index < data.Length; index++)
        {
            char character = data[index];
            int value = Code39Characters.IndexOf(character, StringComparison.Ordinal);
            if (value < 0)
            {
                string why = character is >= 'a' and <= 'z' ? " (Code 39 has no lowercase letters)" : string.Empty;
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Code 39 cannot encode '{character}' (U+{(int)character:X4}) at index {index}{why}."),
                    nameof(data));
            }

            sum += value;
            text.Append(character);
        }

        if (checkDigit)
        {
            text.Append(Code39Characters[sum % 43]);
        }

        encoded = text.ToString();
        var modules = new List<bool>();
        AppendCode39(modules, Code39Patterns[^1]);
        foreach (char character in encoded)
        {
            modules.Add(false);
            AppendCode39(modules, Code39Patterns[Code39Characters.IndexOf(character, StringComparison.Ordinal)]);
        }

        modules.Add(false);
        AppendCode39(modules, Code39Patterns[^1]);
        return [.. modules];
    }

    /// <summary>
    /// Parses a GS1 element string written with its application identifiers in parentheses, such as
    /// <c>(01)09501101530003(17)250101</c>, into Code 128 input: FNC1 first, then each identifier and its data, with FNC1 after each
    /// variable-length element but the last (GS1 General Specifications §5.4.7).
    /// </summary>
    public static int[] Gs1(string data)
    {
        var input = new List<int> { BarcodeCode128.Fnc1 };
        int at = 0;
        while (at < data.Length)
        {
            if (data[at] != '(')
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"A GS1-128 element string starts each element with its application identifier in parentheses; '{data[at]}' at index {at} is not '('."),
                    nameof(data));
            }

            int close = data.IndexOf(')', at + 1);
            if (close < 0)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The application identifier opened at index {at} is not closed with ')'."), nameof(data));
            }

            string identifier = data[(at + 1)..close];
            if (identifier.Length is < 2 or > 4 || !identifier.All(char.IsAsciiDigit))
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The application identifier '{identifier}' at index {at} is not two to four digits."), nameof(data));
            }

            int next = data.IndexOf('(', close + 1);
            int end = next < 0 ? data.Length : next;
            string value = data[(close + 1)..end];
            if (value.Length == 0)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The application identifier ({identifier}) at index {at} has no data."), nameof(data));
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character is < '!' or > 'z' || character is '#' or '$' or '@' or '[' or '\\' or ']' or '^' or '`' or ')')
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"GS1 data cannot hold '{character}' (U+{(int)character:X4}) at index {close + 1 + index}."),
                        nameof(data));
                }
            }

            bool predefined = false;
            if (PredefinedLengths.TryGetValue(identifier[..2], out (int Identifier, int Data) lengths))
            {
                if (identifier.Length != lengths.Identifier)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Application identifiers starting {identifier[..2]} have {lengths.Identifier} digits; ({identifier}) has {identifier.Length}."),
                        nameof(data));
                }

                if (value.Length != lengths.Data)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"The data of ({identifier}) is {lengths.Data} characters long; got {value.Length}."),
                        nameof(data));
                }

                predefined = true;
            }

            foreach (char character in identifier)
            {
                input.Add(character);
            }

            foreach (char character in value)
            {
                input.Add(character);
            }

            if (!predefined && next >= 0)
            {
                input.Add(BarcodeCode128.Fnc1);
            }

            at = end;
        }

        return [.. input];
    }

    private static void AppendCode39(List<bool> modules, string pattern)
    {
        for (int element = 0; element < pattern.Length; element++)
        {
            int width = pattern[element] == '1' ? Code39Wide : 1;
            for (int module = 0; module < width; module++)
            {
                modules.Add(element % 2 == 0);
            }
        }
    }

    private static bool[] ToModules(string pattern)
    {
        bool[] modules = new bool[pattern.Length];
        for (int index = 0; index < pattern.Length; index++)
        {
            modules[index] = pattern[index] == '1';
        }

        return modules;
    }
}
