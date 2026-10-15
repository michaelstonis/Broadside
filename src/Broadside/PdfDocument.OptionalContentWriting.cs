using Broadside.Objects;

namespace Broadside;

/// <content>Writing optional content groups and membership dictionaries (issue #157): ISO 32000-2 §8.11.</content>
public sealed partial class PdfDocument
{
    /// <summary>Creates an optional content group (a layer) named <paramref name="name"/>.</summary>
    /// <param name="name">The name a viewer shows for the group.</param>
    /// <param name="visible">Whether the group is ON in the default configuration.</param>
    /// <returns>The group, as <see cref="OptionalContent"/> lists it.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.11.2.1, Table 96, and §8.11.4.2, Table 98 (PDF 1.5). The group is a new indirect object listed in <c>OCGs</c>; the
    /// catalog gets <c>OCProperties</c> with a default configuration <c>D</c> when it has none. The group is appended to <c>D</c>'s
    /// <c>Order</c> (when <c>D</c> has one or was created) and to its <c>OFF</c> array when not <paramref name="visible"/> (to <c>ON</c>
    /// when visible and <c>D</c>'s <c>BaseState</c> is OFF). Apply it with <see cref="Graphics.PdfCanvas.BeginOptionalContent(PdfOptionalContentGroup)"/>
    /// or the <c>SetOptionalContent</c> methods of forms, images and annotations. Records PDF 1.5. A mutation.
    /// </remarks>
    public PdfOptionalContentGroup CreateOptionalContentGroup(string name, bool visible = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        CosReference reference = OptionalContentWriter.AddGroup(this, name, visible);
        return OptionalContent!.FindGroup(reference)!;
    }

    /// <summary>Creates an optional content membership dictionary: content visible according to <paramref name="policy"/> over <paramref name="groups"/>.</summary>
    /// <param name="groups">Groups of this document.</param>
    /// <param name="policy">How the groups' states combine (<c>P</c>).</param>
    /// <returns>The membership dictionary.</returns>
    /// <exception cref="ArgumentException">A group belongs to another document or is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (PDF 1.5): a new indirect object. Records PDF 1.5. A mutation.</remarks>
    public PdfOptionalContentMembership CreateOptionalContentMembership(IEnumerable<PdfOptionalContentGroup> groups, PdfVisibilityPolicy policy) =>
        MembershipView(OptionalContentWriter.AddMembership(this, groups, policy));

    /// <summary>Creates an optional content membership dictionary whose visibility <paramref name="expression"/> decides.</summary>
    /// <param name="expression">The expression over groups of this document, built with <see cref="PdfVisibilityExpression.Of"/>, <see cref="PdfVisibilityExpression.And"/>, <see cref="PdfVisibilityExpression.Or"/> and <see cref="PdfVisibilityExpression.Not"/>.</param>
    /// <returns>The membership dictionary.</returns>
    /// <exception cref="ArgumentException">A group belongs to another document or is not an indirect object.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.11.2.2, Table 97 (<c>VE</c>, PDF 1.6). The expression's groups are also written as <c>OCGs</c> (with the default
    /// policy), which PDF 1.5 readers use. A single group leaf is written as <c>[/Or group]</c>. Records PDF 1.6. A mutation.
    /// </remarks>
    public PdfOptionalContentMembership CreateOptionalContentMembership(PdfVisibilityExpression expression) =>
        MembershipView(OptionalContentWriter.AddMembership(this, expression));

    private PdfOptionalContentMembership MembershipView(CosReference reference) =>
        OptionalContent?.FindMembership(reference)
        ?? throw new InvalidOperationException("The document has no optional content properties (OCProperties): create a group first.");
}
