using Broadside.Forms;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>default-appearance</c> target (issue #166): the input is a default appearance string's bytes, parsed by
/// <see cref="PdfDefaultAppearance.Parse"/>. Invariants: parsing never throws; there is a font name exactly when no
/// <c>FontOperatorMissing</c> problem is reported; the other operators are no longer than twice the input; and when the input had
/// no syntax problem, the other operators parse again on their own to themselves, with no problem but the missing <c>Tf</c> and no
/// colour or text matrix (every operator kept is allowed, has fitting operands and is written whole); and renaming its font
/// (issue #308) changes nothing but the font name.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.4.3, §7.8.2.</remarks>
internal static class DefaultAppearanceTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        // A whole file (the smoke seeds) is read from its first "/DA (" up to the next ")", so acroform-fields.pdf seeds the target.
        if (data.StartsWith("%PDF-"u8) && data.IndexOf("/DA ("u8) is >= 0 and int start)
        {
            data = data[(start + 5)..];
            int end = data.IndexOf((byte)')');
            data = end < 0 ? data : data[..end];
        }

        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse(data);
        bool missing = (appearance.Problems & PdfDefaultAppearanceProblems.FontOperatorMissing) != 0;
        if (missing != appearance.FontName is null)
        {
            throw new InvalidOperationException($"Font name {(appearance.FontName is null ? "absent" : "present")} with problems {appearance.Problems}.");
        }

        if (appearance.OtherOperators.Length > 2L * data.Length || appearance.Font is not null || appearance.FontResources is not null)
        {
            throw new InvalidOperationException($"{appearance.OtherOperators.Length} bytes of other operators from {data.Length} bytes of input, or a font resolved without resources.");
        }

        if ((appearance.Problems & PdfDefaultAppearanceProblems.SyntaxInvalid) != 0)
        {
            return;
        }

        CheckRename(data, appearance);

        PdfDefaultAppearance again = PdfDefaultAppearance.Parse(appearance.OtherOperators.Span);
        if (again.Problems != PdfDefaultAppearanceProblems.FontOperatorMissing
            || again.Color is not null
            || again.TextMatrix is not null
            || !again.OtherOperators.Span.SequenceEqual(appearance.OtherOperators.Span))
        {
            throw new InvalidOperationException($"The other operators do not parse to themselves: problems {again.Problems}, colour {again.Color is not null}, matrix {again.TextMatrix is not null}.");
        }
    }

    /// <summary>
    /// Renaming the font (issue #308, merged forms) changes nothing else: the result parses to the new name with the same size,
    /// colour, text matrix, other operators and problems; with no font there is nothing to rename.
    /// </summary>
    private static void CheckRename(ReadOnlySpan<byte> data, PdfDefaultAppearance appearance)
    {
        var renamedName = new Objects.CosName("Brs Renamed#1"u8);
        byte[]? renamed = PdfDefaultAppearance.RenameFonts(data, name => name.Equals(appearance.FontName) ? renamedName : null);
        if (appearance.FontName is null)
        {
            if (renamed is not null)
            {
                throw new InvalidOperationException("A string without a usable Tf was renamed.");
            }

            return;
        }

        PdfDefaultAppearance after = renamed is null ? appearance : PdfDefaultAppearance.Parse(renamed);
        if (!renamedName.Equals(after.FontName)
            || after.FontSize.CompareTo(appearance.FontSize) != 0
            || after.Problems != appearance.Problems
            || !Equals(after.TextMatrix, appearance.TextMatrix)
            || !Equals(after.Color, appearance.Color)
            || !after.OtherOperators.Span.SequenceEqual(appearance.OtherOperators.Span))
        {
            throw new InvalidOperationException($"Renaming /{appearance.FontName.Value} changed more than the name: font {after.FontName?.Value}, problems {after.Problems}.");
        }
    }
}
