namespace Broadside;

/// <summary>A parsed visibility expression of a membership dictionary: a boolean expression over optional content group states.</summary>
/// <remarks>
/// ISO 32000-2 §8.11.2.2 (PDF 1.6), Table 97 <c>VE</c>. The array <c>[/Or 1 0 R [/Not 2 0 R]]</c> parses to an <see cref="PdfVisibilityOperator.Or"/>
/// node with a group leaf and a <see cref="PdfVisibilityOperator.Not"/> node. Operands that are null, deleted, or not listed groups
/// are dropped (they have no effect); an operator left without operands has no effect on visibility.
/// </remarks>
public sealed class PdfVisibilityExpression
{
    internal PdfVisibilityExpression(PdfVisibilityOperator @operator, PdfOptionalContentGroup? group, IReadOnlyList<PdfVisibilityExpression> operands)
    {
        Operator = @operator;
        Group = group;
        Operands = operands;
    }

    /// <summary>Gets the kind of node.</summary>
    public PdfVisibilityOperator Operator { get; }

    /// <summary>Gets the group of a <see cref="PdfVisibilityOperator.Group"/> leaf, else <see langword="null"/>.</summary>
    public PdfOptionalContentGroup? Group { get; }

    /// <summary>Gets the operands of an operator node; empty for a leaf.</summary>
    public IReadOnlyList<PdfVisibilityExpression> Operands { get; }

    /// <inheritdoc/>
    public override string ToString() => Operator == PdfVisibilityOperator.Group
        ? Group!.Name
        : $"{Operator}({string.Join(", ", Operands)})";
}
