namespace Broadside.Structure;

/// <summary>Which namespace a structure namespace is, by its namespace name.</summary>
/// <remarks>ISO 32000-2 §14.8.6: the two standard structure namespaces and MathML 3.0, the one other namespace §14.8.6.3 identifies.</remarks>
public enum PdfStructureNamespaceKind
{
    /// <summary>Any other namespace: its types are custom and reach a standard type only through its <c>RoleMapNS</c>.</summary>
    Custom = 0,

    /// <summary>The standard structure namespace for PDF 1.7, <c>http://iso.org/pdf/ssn</c>: the default namespace (§14.8.6.1).</summary>
    Pdf17 = 1,

    /// <summary>The standard structure namespace for PDF 2.0, <c>http://iso.org/pdf2/ssn</c> (§14.8.6.1).</summary>
    Pdf20 = 2,

    /// <summary>MathML 3.0, <c>http://www.w3.org/1998/Math/MathML</c> (§14.8.6.3).</summary>
    MathML = 3,
}
