using System.Globalization;

namespace Broadside.Layout.Flow;

/// <summary>
/// The Code 128 encoder (ISO/IEC 15417) and its GS1-128 profile: code sets A, B and C chosen per character so the symbol is as short as
/// possible, the modulo 103 symbol check character, and FNC1 for GS1 application identifiers.
/// </summary>
internal static class BarcodeCode128
{
    /// <summary>The FNC1 function character in the input sequence: not a character any text can hold, so it cannot clash with one.</summary>
    public const int Fnc1 = -1;

    private const int ShiftValue = 98;
    private const int CodeCValue = 99;
    private const int CodeBValue = 100;
    private const int CodeAValue = 101;
    private const int Fnc1Value = 102;
    private const int StartA = 103;
    private const int Unreachable = int.MaxValue / 2;

    /// <summary>The bar and space widths of symbol characters 0 to 105 and of the stop character (106), bar first (ISO/IEC 15417 Table 1).</summary>
    internal static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private enum CodeSet
    {
        A,
        B,
        C,
    }

    /// <summary>Encodes printable and control ASCII (0 to 127) as the shortest Code 128 symbol.</summary>
    public static bool[] Encode(string data)
    {
        int[] input = new int[data.Length];
        for (int index = 0; index < data.Length; index++)
        {
            char character = data[index];
            if (character > 127)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Code 128 encodes ASCII only; the character U+{(int)character:X4} at index {index} is not ASCII."),
                    nameof(data));
            }

            input[index] = character;
        }

        return Encode(input);
    }

    /// <summary>Encodes a sequence of ASCII values and <see cref="Fnc1"/> markers as the shortest Code 128 symbol.</summary>
    public static bool[] Encode(int[] input)
    {
        List<int> values = Values(input);
        int checksum = values[0];
        for (int index = 1; index < values.Count; index++)
        {
            checksum += index * values[index];
        }

        values.Add(checksum % 103);
        values.Add(106);
        var modules = new List<bool>(values.Count * 11 + 2);
        foreach (int value in values)
        {
            AppendPattern(modules, Patterns[value]);
        }

        return [.. modules];
    }

    /// <summary>The symbol character values of the start character and the data, before the check and stop characters.</summary>
    internal static List<int> Values(int[] input)
    {
        int length = input.Length;

        // cost[position][set]: the fewest symbol characters that encode input[position..] when the code set in force is set.
        int[][] cost = new int[length + 1][];
        for (int position = 0; position <= length; position++)
        {
            cost[position] = new int[3];
        }

        for (int position = length - 1; position >= 0; position--)
        {
            for (int set = 0; set < 3; set++)
            {
                cost[position][set] = Unreachable;
            }

            for (int set = 0; set < 3; set++)
            {
                int best = Unreachable;
                foreach (Step step in Steps(input, position, (CodeSet)set))
                {
                    best = Math.Min(best, step.Cost + cost[position + step.Consumed][(int)step.Next]);
                }

                cost[position][set] = best;
            }

            // A switch of code set costs one character, then the step in the new set.
            for (int set = 0; set < 3; set++)
            {
                for (int other = 0; other < 3; other++)
                {
                    if (other != set)
                    {
                        cost[position][set] = Math.Min(cost[position][set], 1 + Direct(input, cost, position, (CodeSet)other));
                    }
                }
            }
        }

        // The start character picks the first set at no extra cost; B wins a tie, as most encoders start.
        CodeSet current = CodeSet.B;
        int bestStart = length == 0 ? 0 : Direct(input, cost, 0, CodeSet.B);
        foreach (CodeSet set in (ReadOnlySpan<CodeSet>)[CodeSet.C, CodeSet.A])
        {
            int start = length == 0 ? 0 : Direct(input, cost, 0, set);
            if (start < bestStart)
            {
                bestStart = start;
                current = set;
            }
        }

        var values = new List<int> { StartA + (int)current };
        int at = 0;
        while (at < length)
        {
            Step? chosen = null;
            int chosenCost = Unreachable;
            foreach (Step step in Steps(input, at, current))
            {
                int total = step.Cost + cost[at + step.Consumed][(int)step.Next];
                if (total < chosenCost)
                {
                    chosenCost = total;
                    chosen = step;
                }
            }

            int switchCost = Unreachable;
            CodeSet switchTo = current;
            for (int set = 0; set < 3; set++)
            {
                if ((CodeSet)set != current)
                {
                    int total = 1 + Direct(input, cost, at, (CodeSet)set);
                    if (total < switchCost)
                    {
                        switchCost = total;
                        switchTo = (CodeSet)set;
                    }
                }
            }

            if (chosen is null || switchCost < chosenCost)
            {
                values.Add(SwitchValue(switchTo));
                current = switchTo;
                continue;
            }

            values.AddRange(chosen.Value.Values);
            at += chosen.Value.Consumed;
        }

        return values;
    }

    private static int Direct(int[] input, int[][] cost, int position, CodeSet set)
    {
        int best = Unreachable;
        foreach (Step step in Steps(input, position, set))
        {
            best = Math.Min(best, step.Cost + cost[position + step.Consumed][(int)step.Next]);
        }

        return best;
    }

    /// <summary>The ways to encode the input at <paramref name="position"/> without a lasting change of <paramref name="set"/>.</summary>
    private static IEnumerable<Step> Steps(int[] input, int position, CodeSet set)
    {
        int value = input[position];
        if (value == Fnc1)
        {
            yield return new Step(1, 1, set, [Fnc1Value]);
            yield break;
        }

        switch (set)
        {
            case CodeSet.C:
                if (position + 1 < input.Length && IsDigit(value) && IsDigit(input[position + 1]))
                {
                    yield return new Step(1, 2, set, [((value - '0') * 10) + (input[position + 1] - '0')]);
                }

                break;
            case CodeSet.A:
                if (InA(value))
                {
                    yield return new Step(1, 1, set, [ValueInA(value)]);
                }
                else if (InB(value))
                {
                    yield return new Step(2, 1, set, [ShiftValue, value - 32]);
                }

                break;
            default:
                if (InB(value))
                {
                    yield return new Step(1, 1, set, [value - 32]);
                }
                else if (InA(value))
                {
                    yield return new Step(2, 1, set, [ShiftValue, ValueInA(value)]);
                }

                break;
        }
    }

    private static int SwitchValue(CodeSet to) => to switch
    {
        CodeSet.A => CodeAValue,
        CodeSet.B => CodeBValue,
        _ => CodeCValue,
    };

    private static bool IsDigit(int value) => value is >= '0' and <= '9';

    private static bool InA(int value) => value is >= 0 and <= 95;

    private static bool InB(int value) => value is >= 32 and <= 127;

    private static int ValueInA(int value) => value >= 32 ? value - 32 : value + 64;

    private static void AppendPattern(List<bool> modules, string pattern)
    {
        bool dark = true;
        foreach (char width in pattern)
        {
            for (int module = 0; module < width - '0'; module++)
            {
                modules.Add(dark);
            }

            dark = !dark;
        }
    }

    private readonly record struct Step(int Cost, int Consumed, CodeSet Next, int[] Values);
}
