using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// A marked-content point (<c>MP</c>, <c>DP</c>) or the beginning or end of a marked-content sequence (<c>BMC</c>, <c>BDC</c>,
/// <c>EMC</c>). Valid only during the callback that received it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.6, Table 352. The property list is either inline (<see cref="InlineProperties"/>) or named in the resources'
/// <c>Properties</c> subdictionary (<see cref="Properties"/>). Declared with the content interpreter's processor surface
/// (issue #55); reported from issue #56. Members are added, never changed.
/// </remarks>
public readonly ref struct MarkedContentEvent
{
    /// <summary>Gets the tag, a name's bytes without the solidus.</summary>
    public ReadOnlySpan<byte> Tag { get; internal init; }

    /// <summary>Gets an inline property list (§14.6.2) as written; default when there is none or it is named.</summary>
    public ContentOperand InlineProperties { get; internal init; }

    /// <summary>Gets the property list named in the resources, resolved, or <see langword="null"/>.</summary>
    public CosDictionary? Properties { get; internal init; }

    /// <summary>
    /// Gets the value a named property list resolves to: the same dictionary as <see cref="Properties"/>, or, for an <c>AF</c>
    /// sequence, a bare array of file specifications (§14.13.5, Example 2); <see langword="null"/> for an inline or missing one.
    /// </summary>
    public CosObject? PropertiesObject { get; internal init; }

    /// <summary>Gets the marked-content identifier <c>MCID</c> (§14.7.5.2), or <see langword="null"/>.</summary>
    public int? Mcid { get; internal init; }

    /// <summary>Gets the nesting depth of the sequence, 1 for the outermost.</summary>
    public int Depth { get; internal init; }

    /// <summary>Gets a value indicating whether optional content hides the sequence (§8.11.3.1).</summary>
    public bool IsHidden { get; internal init; }

    /// <summary>Gets a value indicating whether the sequence is tagged <c>OC</c>: optional content whose visibility its property list decides (§8.11.3.2).</summary>
    public bool IsOptionalContent { get; internal init; }

    /// <summary>
    /// Gets the content stream that holds the sequence when it is a form XObject or other nested stream; <see langword="null"/> for
    /// a page's content. Marked-content identifiers are scoped to it (§14.7.5.2).
    /// </summary>
    public CosStream? ContentStream { get; internal init; }

    /// <summary>
    /// Gets the <c>StructParents</c> key of the stream that holds the sequence (the page's or the form's), through which
    /// <see cref="Mcid"/> resolves in the structural parent tree (§14.7.5.4); see <see cref="ContentContext.FindStructureElement"/>.
    /// </summary>
    public int? StructParents { get; internal init; }
}
