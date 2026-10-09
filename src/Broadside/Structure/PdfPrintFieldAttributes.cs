using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>PrintField</c>: the role and state of a non-interactive form field, on a <c>Form</c> element.</summary>
/// <remarks>ISO 32000-2 §14.8.5.6, Table 383 (PDF 1.7). None is inheritable; <c>Checked</c> defaults to <c>off</c>.</remarks>
public sealed class PdfPrintFieldAttributes : PdfAttributeObject
{
    private static readonly CosName RoleName = new("Role");
    private static readonly CosName CheckedName = new("Checked");
    private static readonly CosName LegacyCheckedName = new("checked");
    private static readonly CosName DescName = new("Desc");

    internal PdfPrintFieldAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>Gets <c>Role</c>: rb (radio button), cb (check box), pb (push button), tv (text value) or lb (list box).</summary>
    /// <remarks>ISO 32000-2 Table 383.</remarks>
    public string? Role => NameValue(RoleName);

    /// <summary>Gets <c>Checked</c>: on, off or neutral. The lower-case key <c>checked</c> of PDF 1.7, deprecated in PDF 2.0, is read when <c>Checked</c> is absent.</summary>
    /// <remarks>ISO 32000-2 Table 383.</remarks>
    public string? Checked => NameValue(CheckedName) ?? NameValue(LegacyCheckedName);

    /// <summary>Gets <c>Desc</c>: the field's text description.</summary>
    /// <remarks>ISO 32000-2 Table 383.</remarks>
    public string? Description => TextValue(DescName);
}
