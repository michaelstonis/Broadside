namespace Broadside;

/// <summary>
/// The profile (conformance level) of a Factur-X or ZUGFeRD 2.x e-invoice: how much of the EN 16931 data model the embedded CII XML
/// carries. Written to the XMP packet as <c>fx:ConformanceLevel</c>.
/// </summary>
/// <remarks>
/// Factur-X 1.0 and ZUGFeRD 2.x (FNFE-MPE and FeRD), the PDF/A-3 hybrid invoice: an ISO 19005-3 file whose associated file
/// (ISO 32000-2 §14.13) is the UN/CEFACT Cross Industry Invoice. Each profile has a default file name and <c>AFRelationship</c>
/// (see <see cref="PdfInvoiceOptions"/>). ZUGFeRD 1.0 is not supported.
/// </remarks>
public enum PdfInvoiceProfile
{
    /// <summary><c>MINIMUM</c>: the header data only; not an invoice under EN 16931, so its XML is attached as <c>Data</c>.</summary>
    Minimum,

    /// <summary><c>BASIC WL</c>: the header and totals without lines; attached as <c>Data</c>.</summary>
    BasicWl,

    /// <summary><c>BASIC</c>: a subset of EN 16931 with lines; attached as <c>Alternative</c>.</summary>
    Basic,

    /// <summary><c>EN 16931</c> (ZUGFeRD's <c>COMFORT</c>): the European core invoice; attached as <c>Alternative</c>.</summary>
    EN16931,

    /// <summary><c>EXTENDED</c>: EN 16931 extended for cross-industry needs; attached as <c>Alternative</c>.</summary>
    Extended,

    /// <summary>
    /// <c>XRECHNUNG</c>: the German CIUS of EN 16931 (ZUGFeRD 2.1.1 and later), file name <c>xrechnung.xml</c>; attached as
    /// <c>Alternative</c>.
    /// </summary>
    XRechnung,
}
