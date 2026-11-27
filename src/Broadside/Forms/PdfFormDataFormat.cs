namespace Broadside.Forms;

/// <summary>The file formats form data and annotations are exported to and imported from.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.8 (forms data format) and §12.7.6.2 (the submit-form action's <c>XFDF</c> flag). XFDF is specified by
/// ISO 19444-1:2019; Broadside follows Adobe's public XFDF 3.0 specification as its working reference.
/// </remarks>
public enum PdfFormDataFormat
{
    /// <summary>Forms Data Format: a PDF-syntax file with a <c>%FDF-1.2</c> header (ISO 32000-2 §12.7.8).</summary>
    Fdf,

    /// <summary>XML Forms Data Format: an XML file whose root is <c>&lt;xfdf xmlns="http://ns.adobe.com/xfdf/"&gt;</c> (Adobe XFDF 3.0, ISO 19444-1).</summary>
    Xfdf,
}
