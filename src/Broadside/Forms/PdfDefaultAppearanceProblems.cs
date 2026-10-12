namespace Broadside.Forms;

/// <summary>The ways a default appearance string (<c>DA</c>) departs from what ISO 32000-2 §12.7.4.3 requires; what was readable is still read.</summary>
/// <remarks>ISO 32000-2 §12.7.4.3 and Table 228; operators and their operands per §7.8.2 and Table 50.</remarks>
[Flags]
public enum PdfDefaultAppearanceProblems
{
    /// <summary>The string is well formed.</summary>
    None = 0,

    /// <summary>A token that cannot appear in content (a lone <c>)</c>, an unbalanced <c>]</c>, a malformed number) was skipped or repaired (§7.8.2).</summary>
    SyntaxInvalid = 1,

    /// <summary>No usable <c>Tf</c> operator: §12.7.4.3 requires one, with its font and size operands.</summary>
    FontOperatorMissing = 2,

    /// <summary>
    /// An operator had too few operands or operands of the wrong type, and was ignored; or too many, and only its last ones were used;
    /// or operands were left with no operator after them.
    /// </summary>
    OperandsInvalid = 4,

    /// <summary>
    /// An operator not allowed within a text object (Table 50, Figure 9), one that would unbalance the appearance stream around it
    /// (<c>q</c>, <c>Q</c>, marked content), or an unknown keyword; it was ignored.
    /// </summary>
    OperatorNotAllowed = 8,

    /// <summary>More than one <c>Tm</c> operator; §12.7.4.3 allows at most one. The last one is used.</summary>
    MultipleTextMatrices = 16,
}
