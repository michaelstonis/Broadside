using Broadside.Forms;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>default-appearance</c> target (issue #166): the input is a default appearance string's bytes, parsed by
/// <see cref="PdfDefaultAppearance.Parse"/>. Invariants: parsing never throws; there is a font name exactly when no
/// <c>FontOperatorMissing</c> problem is reported; the other operators are no longer than twice the input; and when the input had
/// no syntax problem, the other operators parse again on their own to themselves, with no problem but the missing <c>Tf</c> and no
/// colour or text matrix (every operator kept is allowed, has fitting operands and is written whole).
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

        PdfDefaultAppearance again = PdfDefaultAppearance.Parse(appearance.OtherOperators.Span);
        if (again.Problems != PdfDefaultAppearanceProblems.FontOperatorMissing
            || again.Color is not null
            || again.TextMatrix is not null
            || !again.OtherOperators.Span.SequenceEqual(appearance.OtherOperators.Span))
        {
            throw new InvalidOperationException($"The other operators do not parse to themselves: problems {again.Problems}, colour {again.Color is not null}, matrix {again.TextMatrix is not null}.");
        }
    }
}
