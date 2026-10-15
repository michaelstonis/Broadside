namespace Broadside.Structure;

/// <summary>The type of an artifact: the <c>Type</c> entry of its property list.</summary>
/// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
public enum PdfArtifactType
{
    /// <summary>Ancillary page features such as running heads, folios (page numbers) or Bates numbering.</summary>
    Pagination,

    /// <summary>Purely cosmetic typographical or design elements such as footnote rules or decorative ornaments.</summary>
    Layout,

    /// <summary>Production aids extraneous to the document itself, such as cut marks and colour bars.</summary>
    Page,

    /// <summary>Images, patterns or coloured blocks from a document template, repeated across pages.</summary>
    Background,
}
