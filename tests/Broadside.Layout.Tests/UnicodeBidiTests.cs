using System.Globalization;
using System.IO.Compression;
using System.Text;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// The bidi seam of flow layout against Unicode's own conformance tests of UAX #9, Unicode Bidirectional Algorithm (revision
/// 50, Unicode 16.0.0): <c>BidiTest.txt</c> (sequences of Bidi_Class values under each paragraph direction) and
/// <c>BidiCharacterTest.txt</c> (code point sequences, with paired brackets), each giving the resolved levels after L1 and the
/// visual order after L2 of one line.
/// </summary>
public sealed class UnicodeBidiTests
{
    private static readonly string[] ClassNames = Enum.GetNames<BidiClass>();

    [Fact]
    public void Every_case_of_the_unicode_bidi_test_resolves_and_reorders_as_the_test_file_says()
    {
        string[] expectedLevels = [];
        string[] expectedOrder = [];
        var failures = new List<string>();
        int cases = 0;
        foreach (string line in TestFileLines("BidiTest"))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("@Levels:", StringComparison.Ordinal))
            {
                expectedLevels = line["@Levels:".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                continue;
            }

            if (line.StartsWith("@Reorder:", StringComparison.Ordinal))
            {
                expectedOrder = line["@Reorder:".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                continue;
            }

            string[] fields = line.Split(';');
            byte[] classes = [.. fields[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(ClassOf)];
            int set = int.Parse(fields[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            foreach ((int bit, int direction) in new[] { (1, -1), (2, 0), (4, 1) })
            {
                if ((set & bit) == 0)
                {
                    continue;
                }

                cases++;
                string? failure = Check(classes, "", direction, expectedLevels, expectedOrder, null);
                if (failure is not null)
                {
                    failures.Add($"{fields[0]} (direction {direction}): {failure}");
                }
            }
        }

        Assert.Equal(770241, cases);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases} cases differ:\n" + string.Join('\n', failures.Take(40)));
    }

    [Fact]
    public void Every_case_of_the_unicode_bidi_character_test_resolves_and_reorders_as_the_test_file_says()
    {
        var failures = new List<string>();
        int cases = 0;
        foreach (string line in TestFileLines("BidiCharacterTest"))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split(';');
            int[] codePoints = [.. fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(static code => int.Parse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
            int direction = int.Parse(fields[1], CultureInfo.InvariantCulture) switch { 0 => 0, 1 => 1, _ => -1 };
            int paragraph = int.Parse(fields[2], CultureInfo.InvariantCulture);
            string[] levels = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] order = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // One UTF-16 string; each code point is checked at its first unit.
            var text = new StringBuilder();
            var firstUnits = new List<int>();
            foreach (int codePoint in codePoints)
            {
                firstUnits.Add(text.Length);
                text.Append(char.ConvertFromUtf32(codePoint));
            }

            string value = text.ToString();
            byte[] classes = new byte[value.Length];
            UnicodeBidi.Classify(value, classes);
            cases++;
            string? failure = Check(classes, value, direction, levels, order, (paragraph, [.. firstUnits]));
            if (failure is not null)
            {
                failures.Add($"{fields[0]} (direction {fields[1]}): {failure}");
            }
        }

        Assert.Equal(91707, cases);
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases} cases differ:\n" + string.Join('\n', failures.Take(40)));
    }

    [Fact]
    public void Resolving_marks_each_paragraph_with_its_own_level()
    {
        // P1: the paragraph separator ends the first paragraph (Hebrew, so right to left); the second starts with Latin.
        string text = "\u05D0\u05D1 c\u2029de \u05D2";
        byte[] levels = new byte[text.Length];

        UnicodeBidi.Resolve(text, -1, levels);

        Assert.Equal([1, 1, 1, 2, 1, 0, 0, 0, 1], levels.Select(static level => level & UnicodeBidi.LevelMask));
        Assert.Equal([1, 1, 1, 1, 1, 0, 0, 0, 0], levels.Select(static level => level >> 7));
    }

    /// <summary>Resolves one paragraph, applies L1 to it as one line, and compares levels and the L2 order (removed units skipped).</summary>
    private static string? Check(byte[] classes, string text, int direction, string[] levels, string[] order, (int Paragraph, int[] Units)? characters)
    {
        byte[] resolved = new byte[classes.Length];
        int paragraph = UnicodeBidi.ResolveParagraph(classes, text, direction, resolved);
        UnicodeBidi.ResetWhitespace(classes, paragraph, resolved);
        int[] units = characters?.Units ?? [.. Enumerable.Range(0, classes.Length)];
        if (characters is { } expected && expected.Paragraph != paragraph)
        {
            return $"paragraph level {paragraph}, expected {expected.Paragraph}";
        }

        var kept = new List<int>();
        for (int index = 0; index < units.Length; index++)
        {
            if (levels[index] == "x")
            {
                continue;
            }

            kept.Add(index);
            if (resolved[units[index]] != int.Parse(levels[index], CultureInfo.InvariantCulture))
            {
                return $"levels [{string.Join(' ', units.Select(unit => resolved[unit]))}], expected [{string.Join(' ', levels)}]";
            }
        }

        byte[] keptLevels = [.. kept.Select(index => resolved[units[index]])];
        int[] visual = new int[kept.Count];
        UnicodeBidi.Reorder(keptLevels, visual);
        string actual = string.Join(' ', visual.Select(position => kept[position]));
        return actual == string.Join(' ', order) ? null : $"order [{actual}], expected [{string.Join(' ', order)}]";
    }

    private static byte ClassOf(string name) => (byte)Array.IndexOf(ClassNames, name);

    private static IEnumerable<string> TestFileLines(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", $"{name}-16.0.0.txt.gz");
        using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
