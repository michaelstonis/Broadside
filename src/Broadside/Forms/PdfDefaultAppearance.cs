using System.Buffers;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// A default appearance string (<c>DA</c>) parsed: the font resource name and size of its <c>Tf</c> operator, the fill colour of its
/// <c>g</c>, <c>rg</c> or <c>k</c> operator, its text matrix and every other operator as written. Read through a field
/// (<see cref="PdfField.ParsedDefaultAppearance"/>), the font is also resolved in the default resources.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.4.3, Table 228. The string is content stream syntax (§7.8.2) holding only operators allowed within a text object
/// (Table 50, Figure 9), at least a <c>Tf</c> whose font names an entry of the <c>Font</c> subdictionary of the default resources;
/// a size of 0 means the font is auto-sized. At most one <c>Tm</c> is allowed, whose translation a processor generating an
/// appearance replaces.
/// </para>
/// <para>
/// Parsing never throws: what departs from the clause is listed in <see cref="Problems"/> and what can be read is read. When an
/// operator is repeated, the last one wins. An operator with too few operands or operands of the wrong type is ignored; one with too
/// many uses its last ones, as a content stream's would. <c>q</c>, <c>Q</c> and marked-content operators, though Table 50 allows them
/// in a text object, are ignored because the string is inserted between an appearance stream's own <c>q</c>/<c>Q</c> and
/// <c>BMC</c>/<c>EMC</c> (§12.7.4.3, Example) and they would unbalance it (§8.4.2, §14.6). Tokenizing allocates nothing but the
/// result.
/// </para>
/// </remarks>
public sealed class PdfDefaultAppearance
{
    [ThreadStatic]
    private static OperandArena? _arena;

    private PdfDefaultAppearance(CosName? fontName, double fontSize, PdfColor? color, Matrix? textMatrix, ReadOnlyMemory<byte> otherOperators, PdfDefaultAppearanceProblems problems)
    {
        FontName = fontName;
        FontSize = fontSize;
        Color = color;
        TextMatrix = textMatrix;
        OtherOperators = otherOperators;
        Problems = problems;
    }

    /// <summary>Gets the font resource name of the <c>Tf</c> operator, or <see langword="null"/> when there is no usable one.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3: it shall match a resource name in the <c>Font</c> entry of the default resource dictionary.</remarks>
    public CosName? FontName { get; }

    /// <summary>Gets the font size of the <c>Tf</c> operator: 0 when the font is auto-sized, or when there is no usable <c>Tf</c>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3: a zero size means the font shall be auto-sized.</remarks>
    public double FontSize { get; }

    /// <summary>Gets a value indicating whether the font is auto-sized: <see cref="FontSize"/> is 0.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public bool IsAutoSize => FontSize == 0;

    /// <summary>
    /// Gets the fill colour set by the last <c>g</c>, <c>rg</c> or <c>k</c> operator, in DeviceGray, DeviceRGB or DeviceCMYK; or
    /// <see langword="null"/> when there is none (the initial black then applies).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.3 and §8.6.8, Table 73. The components are as written, not clipped. A colour set in a named colour space
    /// (<c>cs</c>, <c>sc</c>, <c>scn</c>) is not read here and stays in <see cref="OtherOperators"/>, after which it applies.
    /// </remarks>
    public PdfColor? Color { get; }

    /// <summary>Gets the operands of the <c>Tm</c> operator, or <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.3 and §9.4.2, Table 106: the processor replaces its translation with positioning values of its own, and
    /// inserts a <c>Tm</c> when the string has none.
    /// </remarks>
    public Matrix? TextMatrix { get; }

    /// <summary>
    /// Gets every operator other than <c>Tf</c>, <c>Tm</c> and the <c>g</c>, <c>rg</c> and <c>k</c> fill colour operators, in order,
    /// each with its operands as written and separated by one space; empty when there are none. Operators that were ignored (see
    /// <see cref="Problems"/>) are left out.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.4.3: graphics state or text state operators that establish parameters for the variable text.</remarks>
    public ReadOnlyMemory<byte> OtherOperators { get; }

    /// <summary>Gets what departs from ISO 32000-2 §12.7.4.3 in the string; <see cref="PdfDefaultAppearanceProblems.None"/> when it is well formed.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public PdfDefaultAppearanceProblems Problems { get; }

    /// <summary>Gets a value indicating whether the string is well formed: <see cref="Problems"/> is <see cref="PdfDefaultAppearanceProblems.None"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public bool IsValid => Problems == PdfDefaultAppearanceProblems.None;

    /// <summary>
    /// Gets the font <see cref="FontName"/> names, found in the default resources; <see langword="null"/> when the appearance was not
    /// read through a field, or when no default resources hold the font.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.4.3 and §12.7.3, Table 224 (<c>DR</c>).</remarks>
    public PdfFont? Font { get; private init; }

    /// <summary>Gets the resource dictionary <see cref="Font"/> was found in, the one appearance resources are copied from; <see langword="null"/> when <see cref="Font"/> is.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3: an appearance stream's resources are created, or updated, from the default resources.</remarks>
    public CosDictionary? FontResources { get; private init; }

    /// <summary>Parses a default appearance string.</summary>
    /// <param name="value">The string's bytes, as stored in the <c>DA</c> entry.</param>
    /// <returns>What the string holds; never <see langword="null"/>, and never throws: see <see cref="Problems"/>.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public static PdfDefaultAppearance Parse(ReadOnlySpan<byte> value)
    {
        OperandArena arena = _arena ??= new OperandArena();
        arena.Clear();
        arena.Overflowed = false;

        // Kept operators are disjoint slices of the input, joined by one space each: at most twice its length. A decoded font name
        // is never longer than the input.
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(Array.MaxLength, Math.Max(16, value.Length * 2L)));
        byte[] name = ArrayPool<byte>.Shared.Rent(Math.Max(16, value.Length));
        try
        {
            var state = new State { FontNameLength = -1 };
            var reader = new ContentReader(value, arena);
            while (reader.Next(out ReadOperator op))
            {
                Read(op, arena, value, buffer, name, ref state);
                arena.Clear();
            }

            if (arena.Count > 0 || arena.OpenDepth > 0)
            {
                state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
            }

            if (reader.Issues != ReaderIssues.None || arena.Overflowed)
            {
                state.Problems |= PdfDefaultAppearanceProblems.SyntaxInvalid;
            }

            CosName? fontName = null;
            if (state.FontNameLength < 0)
            {
                state.Problems |= PdfDefaultAppearanceProblems.FontOperatorMissing;
            }
            else
            {
                fontName = CosName.FromOwnedBytes(name.AsSpan(0, state.FontNameLength).ToArray());
            }

            ReadOnlyMemory<byte> other = state.Written == 0 ? ReadOnlyMemory<byte>.Empty : buffer.AsSpan(0, state.Written).ToArray();
            return new PdfDefaultAppearance(fontName, state.FontSize, state.Color, state.TextMatrix, other, state.Problems);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(name);
            arena.Trim();
        }
    }

    /// <summary>Returns this appearance with its font resolved.</summary>
    internal PdfDefaultAppearance WithFont(PdfFont font, CosDictionary resources) =>
        new(FontName, FontSize, Color, TextMatrix, OtherOperators, Problems) { Font = font, FontResources = resources };

    private static void Read(ReadOperator op, OperandArena arena, ReadOnlySpan<byte> source, byte[] buffer, byte[] name, ref State state)
    {
        switch (op.Code)
        {
            case ContentOperatorCode.SetFont:
                ContentOperands font = Last(arena, 2, ref state);
                if (font.Count == 2 && font[0].Kind == ContentOperandKind.Name && !font[0].Bytes.Contains((byte)0) && font[1].IsNumber)
                {
                    font[0].Bytes.CopyTo(name);
                    state.FontNameLength = font[0].Bytes.Length;
                    state.FontSize = font[1].Number;
                }
                else
                {
                    state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
                }

                return;
            case ContentOperatorCode.SetFillGray:
                SetColor(arena, PdfDeviceGrayColorSpace.Instance, ref state);
                return;
            case ContentOperatorCode.SetFillRgb:
                SetColor(arena, PdfDeviceRgbColorSpace.Instance, ref state);
                return;
            case ContentOperatorCode.SetFillCmyk:
                SetColor(arena, PdfDeviceCmykColorSpace.Instance, ref state);
                return;
            case ContentOperatorCode.SetTextMatrix:
                ContentOperands matrix = Last(arena, 6, ref state);
                if (Numbers(matrix, 6))
                {
                    if (state.TextMatrix is not null)
                    {
                        state.Problems |= PdfDefaultAppearanceProblems.MultipleTextMatrices;
                    }

                    state.TextMatrix = new Matrix(matrix[0].Number, matrix[1].Number, matrix[2].Number, matrix[3].Number, matrix[4].Number, matrix[5].Number);
                }
                else
                {
                    state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
                }

                return;
        }

        switch (Check(op.Code, arena.Operands))
        {
            case Fit.Allowed:
                if (state.Written > 0)
                {
                    buffer[state.Written++] = (byte)' ';
                }

                ReadOnlySpan<byte> written = source[op.Start..op.End];
                written.CopyTo(buffer.AsSpan(state.Written));
                state.Written += written.Length;
                return;
            case Fit.OperandsInvalid:
                state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
                return;
            default:
                state.Problems |= PdfDefaultAppearanceProblems.OperatorNotAllowed;
                return;
        }
    }

    private static void SetColor(OperandArena arena, PdfColorSpace space, ref State state)
    {
        int count = space.ComponentCount;
        ContentOperands last = Last(arena, count, ref state);
        if (!Numbers(last, count))
        {
            state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
            return;
        }

        Span<float> components = stackalloc float[4];
        for (int index = 0; index < count; index++)
        {
            components[index] = (float)last[index].Number;
        }

        state.Color = new PdfColor(space, components[..count]);
    }

    /// <summary>The last <paramref name="count"/> operands; more than that is a problem, fewer gives them all.</summary>
    private static ContentOperands Last(OperandArena arena, int count, ref State state)
    {
        if (arena.Count <= count)
        {
            return arena.Operands;
        }

        state.Problems |= PdfDefaultAppearanceProblems.OperandsInvalid;
        return arena.Last(count);
    }

    /// <summary>Whether there are exactly <paramref name="count"/> operands, all numbers.</summary>
    private static bool Numbers(ContentOperands operands, int count) => operands.Count == count && LeadingNumbers(operands, count);

    /// <summary>Whether the first <paramref name="count"/> operands are numbers.</summary>
    private static bool LeadingNumbers(ContentOperands operands, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (!operands[index].IsNumber)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether an operator other than Tf, Tm, g, rg and k may stay in the string, and whether its operands fit (Tables 56, 73, 103, 106, 107, 351).</summary>
    private static Fit Check(ContentOperatorCode code, ContentOperands operands)
    {
        bool fits;
        switch (code)
        {
            case ContentOperatorCode.SetCharacterSpacing or ContentOperatorCode.SetWordSpacing or ContentOperatorCode.SetHorizontalScaling
                or ContentOperatorCode.SetLeading or ContentOperatorCode.SetTextRise or ContentOperatorCode.SetTextRenderingMode
                or ContentOperatorCode.SetLineWidth or ContentOperatorCode.SetLineCap or ContentOperatorCode.SetLineJoin
                or ContentOperatorCode.SetMiterLimit or ContentOperatorCode.SetFlatness or ContentOperatorCode.SetStrokeGray:
                fits = Numbers(operands, 1);
                break;
            case ContentOperatorCode.SetStrokeRgb:
                fits = Numbers(operands, 3);
                break;
            case ContentOperatorCode.SetStrokeCmyk:
                fits = Numbers(operands, 4);
                break;
            case ContentOperatorCode.MoveText or ContentOperatorCode.MoveTextSetLeading:
                fits = Numbers(operands, 2);
                break;
            case ContentOperatorCode.NextLine:
                fits = operands.Count == 0;
                break;
            case ContentOperatorCode.SetDash:
                fits = operands.Count == 2 && operands[0].Kind == ContentOperandKind.Array && operands[1].IsNumber;
                break;
            case ContentOperatorCode.SetRenderingIntent or ContentOperatorCode.SetGraphicsStateParameters
                or ContentOperatorCode.SetStrokeColorSpace or ContentOperatorCode.SetFillColorSpace or ContentOperatorCode.MarkPoint:
                fits = operands.Count == 1 && operands[0].Kind == ContentOperandKind.Name;
                break;
            case ContentOperatorCode.SetStrokeColor or ContentOperatorCode.SetFillColor:
                fits = operands.Count > 0 && LeadingNumbers(operands, operands.Count);
                break;
            case ContentOperatorCode.SetStrokeColorExtended or ContentOperatorCode.SetFillColorExtended:
                fits = operands.Count > 0 && LeadingNumbers(operands, operands.Count - 1)
                    && (operands[operands.Count - 1].IsNumber || operands[operands.Count - 1].Kind == ContentOperandKind.Name);
                break;
            case ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText:
                fits = operands.Count == 1 && operands[0].Kind == ContentOperandKind.String;
                break;
            case ContentOperatorCode.NextLineShowTextWithSpacing:
                fits = operands.Count == 3 && operands[0].IsNumber && operands[1].IsNumber && operands[2].Kind == ContentOperandKind.String;
                break;
            case ContentOperatorCode.ShowTextAdjusted:
                fits = operands.Count == 1 && operands[0].Kind == ContentOperandKind.Array;
                break;
            case ContentOperatorCode.MarkPointWithProperties:
                fits = operands.Count == 2 && operands[0].Kind == ContentOperandKind.Name
                    && operands[1].Kind is ContentOperandKind.Name or ContentOperandKind.Dictionary;
                break;
            default:
                return Fit.NotAllowed;
        }

        return fits ? Fit.Allowed : Fit.OperandsInvalid;
    }

    private enum Fit
    {
        Allowed,
        OperandsInvalid,
        NotAllowed,
    }

    private struct State
    {
        public int FontNameLength;
        public double FontSize;
        public PdfColor? Color;
        public Matrix? TextMatrix;
        public int Written;
        public PdfDefaultAppearanceProblems Problems;
    }
}
