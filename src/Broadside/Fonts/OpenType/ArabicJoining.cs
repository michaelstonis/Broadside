using System.Globalization;
using System.Text;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// Arabic cursive joining: the Joining_Type of Arabic-script characters and the positional form (isolated, final, medial, initial)
/// each takes in a run, which selects the <c>isol</c>, <c>fina</c>, <c>medi</c> and <c>init</c> features for its glyph.
/// </summary>
/// <remarks>
/// The Unicode Standard, §9.2 (Arabic, "Cursive Joining", rules R1–R7) and ArabicShaping.txt (Joining_Type), as in the OpenType
/// specification's "Developing OpenType Fonts for Arabic Script". The table lists the dual-joining (D), right-joining (R) and
/// join-causing (C) characters of the Arabic, Arabic Supplement and Arabic Extended-A/B blocks; characters it does not list are
/// transparent (T) when they are non-spacing or enclosing marks or format characters other than ZWNJ, and non-joining (U)
/// otherwise. Syriac, N'Ko, Mongolian and the other joining scripts are issue #230.
/// </remarks>
internal static class ArabicJoining
{
    private const byte None = 0;
    private const byte Dual = (byte)'D';
    private const byte Right = (byte)'R';
    private const byte Causing = (byte)'C';
    private const byte Transparent = (byte)'T';

    // Joining_Type D, R and C (ArabicShaping.txt, Unicode 16.0), sorted by start.
    private static readonly (int Start, int End, byte Type)[] Ranges =
    [
        (0x0620, 0x0620, Dual), (0x0622, 0x0625, Right), (0x0626, 0x0626, Dual), (0x0627, 0x0627, Right), (0x0628, 0x0628, Dual),
        (0x0629, 0x0629, Right), (0x062A, 0x062E, Dual), (0x062F, 0x0632, Right), (0x0633, 0x063F, Dual), (0x0640, 0x0640, Causing),
        (0x0641, 0x0647, Dual), (0x0648, 0x0648, Right), (0x0649, 0x064A, Dual), (0x066E, 0x066F, Dual), (0x0671, 0x0673, Right),
        (0x0675, 0x0677, Right), (0x0678, 0x0687, Dual), (0x0688, 0x0699, Right), (0x069A, 0x06BF, Dual), (0x06C0, 0x06C0, Right),
        (0x06C1, 0x06C2, Dual), (0x06C3, 0x06CB, Right), (0x06CC, 0x06CC, Dual), (0x06CD, 0x06CD, Right), (0x06CE, 0x06CE, Dual),
        (0x06CF, 0x06CF, Right), (0x06D0, 0x06D1, Dual), (0x06D2, 0x06D3, Right), (0x06D5, 0x06D5, Right), (0x06EE, 0x06EF, Right),
        (0x06FA, 0x06FC, Dual), (0x06FF, 0x06FF, Dual), (0x074D, 0x074D, Right), (0x074E, 0x0758, Dual), (0x0759, 0x075B, Right),
        (0x075C, 0x076A, Dual), (0x076B, 0x076C, Right), (0x076D, 0x0770, Dual), (0x0771, 0x0771, Right), (0x0772, 0x0772, Dual),
        (0x0773, 0x0774, Right), (0x0775, 0x0777, Dual), (0x0778, 0x0779, Right), (0x077A, 0x077F, Dual), (0x0870, 0x0882, Right),
        (0x0883, 0x0885, Causing), (0x0886, 0x0886, Dual), (0x0889, 0x088D, Dual), (0x088E, 0x088E, Right), (0x08A0, 0x08A9, Dual),
        (0x08AA, 0x08AC, Right), (0x08AE, 0x08AE, Right), (0x08AF, 0x08B0, Dual), (0x08B1, 0x08B2, Right), (0x08B3, 0x08B8, Dual),
        (0x08B9, 0x08B9, Right), (0x08BA, 0x08C8, Dual), (0x200D, 0x200D, Causing),
    ];

    /// <summary>The positional forms, as the feature mask bits of <see cref="ShapePlan"/>.</summary>
    public enum Form
    {
        /// <summary>No positional form (non-joining or transparent).</summary>
        None,

        /// <summary>Not joined on either side (<c>isol</c>).</summary>
        Isolated,

        /// <summary>Joined to the preceding character only (<c>fina</c>).</summary>
        Final,

        /// <summary>Joined on both sides (<c>medi</c>).</summary>
        Medial,

        /// <summary>Joined to the following character only (<c>init</c>).</summary>
        Initial,
    }

    /// <summary>Whether <paramref name="scalar"/> is an Arabic-script letter that joins (its Joining_Type is D, R or C).</summary>
    public static bool Joins(int scalar) => TypeOf(scalar) is Dual or Right or Causing;

    /// <summary>
    /// Assigns each glyph of the run (one per character, in logical order, before any substitution) its positional form by the
    /// characters it came from.
    /// </summary>
    public static void AssignForms(ReadOnlySpan<char> text, GlyphBuffer buffer, Span<Form> forms)
    {
        int previous = -1;
        byte previousType = None;
        for (int index = 0; index < buffer.Length; index++)
        {
            Rune.DecodeFromUtf16(text[buffer[index].Cluster..], out Rune rune, out _);
            byte type = TypeOf(rune.Value);
            forms[index] = Form.None;
            if (type == Transparent)
            {
                continue;
            }

            // R1–R7: a character joins the previous one when that one joins to its left (D or C) and this one to its right (D, R or C).
            bool joined = previousType is Dual or Causing && type is Dual or Right or Causing;
            if (joined && previous >= 0)
            {
                forms[previous] = forms[previous] switch
                {
                    Form.Isolated => Form.Initial,
                    Form.Final => Form.Medial,
                    Form other => other,
                };
            }

            forms[index] = type is Dual or Right or Causing ? (joined ? Form.Final : Form.Isolated) : Form.None;
            previous = index;
            previousType = type;
        }
    }

    private static byte TypeOf(int scalar)
    {
        int low = 0;
        int high = Ranges.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            (int start, int end, byte type) = Ranges[middle];
            if (scalar < start)
            {
                high = middle - 1;
            }
            else if (scalar > end)
            {
                low = middle + 1;
            }
            else
            {
                return type;
            }
        }

        if (scalar == 0x200C)
        {
            return None;
        }

        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(scalar);
        bool prependedMark = scalar is (>= 0x0600 and <= 0x0605) or 0x06DD or 0x0890 or 0x0891 or 0x08E2;
        return !prependedMark && category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            ? Transparent
            : None;
    }
}
