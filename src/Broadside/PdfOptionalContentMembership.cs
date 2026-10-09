using Broadside.Objects;

namespace Broadside;

/// <summary>
/// An optional content membership dictionary: content whose visibility depends on several groups through a policy or a visibility
/// expression. A live view over its dictionary.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.11.2.2 (PDF 1.5), Table 97. A visibility expression (<c>VE</c>, PDF 1.6) takes precedence over <c>OCGs</c> and
/// <c>P</c>; an invalid expression falls back to them. Without usable groups the membership has no effect: content is visible.
/// </remarks>
public sealed class PdfOptionalContentMembership
{
    private readonly PdfOptionalContentProperties _properties;

    internal PdfOptionalContentMembership(PdfOptionalContentProperties properties, CosDictionary dictionary, CosReference? reference)
    {
        _properties = properties;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the membership dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the dictionary, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the groups whose states the policy combines (<c>OCGs</c>, a dictionary or an array); null and deleted entries are ignored.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> Groups =>
        _properties.ReadGroupList(ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.OCGs), Reference);

    /// <summary>Gets the visibility policy (<c>P</c>); <see cref="PdfVisibilityPolicy.AnyOn"/> when absent or unknown.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97. An unknown name is reported as <c>VisibilityPolicyInvalid</c> when visibility is computed.</remarks>
    public PdfVisibilityPolicy Policy => PdfOptionalContentProperties.ReadPolicy(ViewReading.Name(_properties.Document, Dictionary, FileAndLayerNames.P)) ?? PdfVisibilityPolicy.AnyOn;

    /// <summary>Gets the parsed visibility expression (<c>VE</c>), or <see langword="null"/> when absent or invalid.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97 (PDF 1.6).</remarks>
    public PdfVisibilityExpression? VisibilityExpression =>
        ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.VE) is CosArray array ? _properties.ParseExpression(array, Reference) : null;
}
