namespace Broadside;

/// <summary>The namespace URIs of the XMP schemas a PDF document's metadata commonly uses.</summary>
/// <remarks>
/// Properties are identified by namespace URI and local name, never by prefix: a packet may bind any prefix to these URIs
/// (ISO 16684-1 §6.2). URIs from ISO 16684-1 §8.3-8.6 (dc, xmp, xmpMM), ISO 32000-2 §14.3.2 (pdf), ISO 19005 (pdfaid) and
/// ISO 14289-1 §5 (pdfuaid).
/// </remarks>
public static class XmpNamespaces
{
    /// <summary>RDF, <c>http://www.w3.org/1999/02/22-rdf-syntax-ns#</c>: the syntax XMP is serialized in (ISO 16684-1 §7.4).</summary>
    public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>The <c>x:xmpmeta</c> wrapper element's namespace, <c>adobe:ns:meta/</c> (ISO 16684-1 §7.3.3).</summary>
    public const string XmpMeta = "adobe:ns:meta/";

    /// <summary>Dublin Core, <c>http://purl.org/dc/elements/1.1/</c>, usually prefixed <c>dc</c> (ISO 16684-1 §8.3).</summary>
    public const string DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>XMP basic, <c>http://ns.adobe.com/xap/1.0/</c>, usually prefixed <c>xmp</c> (ISO 16684-1 §8.4).</summary>
    public const string Xmp = "http://ns.adobe.com/xap/1.0/";

    /// <summary>XMP media management, <c>http://ns.adobe.com/xap/1.0/mm/</c>, usually prefixed <c>xmpMM</c> (ISO 16684-1 §8.6).</summary>
    public const string XmpMediaManagement = "http://ns.adobe.com/xap/1.0/mm/";

    /// <summary>Adobe PDF, <c>http://ns.adobe.com/pdf/1.3/</c>, usually prefixed <c>pdf</c> (ISO 32000-2 §14.3.3, Table 349).</summary>
    public const string Pdf = "http://ns.adobe.com/pdf/1.3/";

    /// <summary>PDF/A identification, <c>http://www.aiim.org/pdfa/ns/id/</c>, usually prefixed <c>pdfaid</c> (ISO 19005).</summary>
    public const string PdfAIdentification = "http://www.aiim.org/pdfa/ns/id/";

    /// <summary>PDF/UA identification, <c>http://www.aiim.org/pdfua/ns/id/</c>, usually prefixed <c>pdfuaid</c> (ISO 14289-1 §5).</summary>
    public const string PdfUAIdentification = "http://www.aiim.org/pdfua/ns/id/";
}
