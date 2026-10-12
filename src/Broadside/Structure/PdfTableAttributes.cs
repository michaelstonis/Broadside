using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>Table</c>, on a table or its cells.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.5.7, Table 384. Each property is this object's own value, <see langword="null"/> when absent. None is inheritable;
/// <c>RowSpan</c> and <c>ColSpan</c> default to 1 through <see cref="PdfStructureElement.GetAttributeValue(CosName, CosName)"/>; the
/// default <c>Scope</c> is computed from the cell's position (§14.8.5.7), which this view does not do.
/// </remarks>
public sealed class PdfTableAttributes : PdfAttributeObject
{
    private static readonly CosName RowSpanName = new("RowSpan");
    private static readonly CosName ColSpanName = new("ColSpan");
    private static readonly CosName HeadersName = new("Headers");
    private static readonly CosName ScopeName = new("Scope");
    private static readonly CosName SummaryName = new("Summary");
    private static readonly CosName ShortName = new("Short");

    internal PdfTableAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>Gets <c>RowSpan</c>: the number of rows the cell spans.</summary>
    /// <remarks>ISO 32000-2 Table 384.</remarks>
    public int? RowSpan => IntegerValue(RowSpanName);

    /// <summary>Gets <c>ColSpan</c>: the number of columns the cell spans.</summary>
    /// <remarks>ISO 32000-2 Table 384.</remarks>
    public int? ColumnSpan => IntegerValue(ColSpanName);

    /// <summary>Gets <c>Headers</c>: the IDs of the header cells (<c>TH</c>) of this cell, as byte strings.</summary>
    /// <remarks>ISO 32000-2 Table 384; the IDs are those of <see cref="PdfStructureTreeRoot.FindElementById(ReadOnlySpan{byte})"/>.</remarks>
    public IReadOnlyList<CosString>? Headers
    {
        get
        {
            if (GetValue(HeadersName) is not CosArray array)
            {
                return null;
            }

            var headers = new List<CosString>(array.Count);
            foreach (CosObject item in array)
            {
                if (Document.Resolve(item) is CosString id)
                {
                    headers.Add(id);
                }
            }

            return headers;
        }
    }

    /// <summary>Gets <c>Scope</c> of a header cell: Row, Column or Both (PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 Table 384.</remarks>
    public string? Scope => NameValue(ScopeName);

    /// <summary>Gets <c>Summary</c> of a table (PDF 1.7).</summary>
    /// <remarks>ISO 32000-2 Table 384.</remarks>
    public string? Summary => TextValue(SummaryName);

    /// <summary>Gets <c>Short</c>, a short form of a header cell's content (PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 Table 384.</remarks>
    public string? ShortForm => TextValue(ShortName);
}
