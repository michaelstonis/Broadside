namespace Broadside;

/// <summary>The relationship of an associated file to the part of the document that refers to it (<c>AFRelationship</c>).</summary>
/// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §14.13. Informational only: it gives no processing instructions.</remarks>
public enum PdfFileRelationship
{
    /// <summary>The relationship is not known or cannot be described by another value. The default.</summary>
    Unspecified,

    /// <summary>The original source material for the associated content.</summary>
    Source,

    /// <summary>Information used to derive a visual presentation, such as the data behind a table or graph.</summary>
    Data,

    /// <summary>An alternative representation of the content, such as audio.</summary>
    Alternative,

    /// <summary>A supplemental representation that may be easier to consume, such as MathML for an equation.</summary>
    Supplement,

    /// <summary>An encrypted payload document (§7.6.7).</summary>
    EncryptedPayload,

    /// <summary>The data of the document's interactive form (§12.7.3).</summary>
    FormData,

    /// <summary>A schema definition for the associated object, such as an XML schema for a metadata stream.</summary>
    Schema,

    /// <summary>A second-class name (Annex E) or another name; see <see cref="PdfFileSpecification.RelationshipName"/>.</summary>
    Other,
}
