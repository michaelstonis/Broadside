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

    /// <summary>Returns a leaf that is true when <paramref name="group"/> is ON.</summary>
    /// <param name="group">The group: one of the document the expression is written to, as an indirect object.</param>
    /// <returns>The leaf.</returns>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (<c>VE</c>, PDF 1.6): a group operand is an indirect reference to the group.</remarks>
    public static PdfVisibilityExpression Of(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new PdfVisibilityExpression(PdfVisibilityOperator.Group, group, []);
    }

    /// <summary>Returns an expression that is true when every operand is.</summary>
    /// <param name="operands">One or more operands.</param>
    /// <returns>The <c>/And</c> node.</returns>
    /// <exception cref="ArgumentException">There is no operand.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (<c>VE</c>, PDF 1.6).</remarks>
    public static PdfVisibilityExpression And(params PdfVisibilityExpression[] operands) => Node(PdfVisibilityOperator.And, operands);

    /// <summary>Returns an expression that is true when any operand is.</summary>
    /// <param name="operands">One or more operands.</param>
    /// <returns>The <c>/Or</c> node.</returns>
    /// <exception cref="ArgumentException">There is no operand.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (<c>VE</c>, PDF 1.6).</remarks>
    public static PdfVisibilityExpression Or(params PdfVisibilityExpression[] operands) => Node(PdfVisibilityOperator.Or, operands);

    /// <summary>Returns an expression that is true when <paramref name="operand"/> is false.</summary>
    /// <param name="operand">The single operand.</param>
    /// <returns>The <c>/Not</c> node.</returns>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (<c>VE</c>, PDF 1.6): <c>Not</c> takes exactly one operand.</remarks>
    public static PdfVisibilityExpression Not(PdfVisibilityExpression operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new PdfVisibilityExpression(PdfVisibilityOperator.Not, null, [operand]);
    }

    /// <summary>Gets the kind of node.</summary>
    public PdfVisibilityOperator Operator { get; }

    /// <summary>Gets the group of a <see cref="PdfVisibilityOperator.Group"/> leaf, else <see langword="null"/>.</summary>
    public PdfOptionalContentGroup? Group { get; }

    /// <summary>Gets the operands of an operator node; empty for a leaf.</summary>
    public IReadOnlyList<PdfVisibilityExpression> Operands { get; }

    private static PdfVisibilityExpression Node(PdfVisibilityOperator @operator, PdfVisibilityExpression[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        if (operands.Length == 0 || Array.Exists(operands, static operand => operand is null))
        {
            throw new ArgumentException("An And or Or expression takes one or more operands, none null.", nameof(operands));
        }

        return new PdfVisibilityExpression(@operator, null, [.. operands]);
    }

    /// <inheritdoc/>
    public override string ToString() => Operator == PdfVisibilityOperator.Group
        ? Group!.Name
        : $"{Operator}({string.Join(", ", Operands)})";
}
