namespace Broadside;

/// <summary>
/// The Bates numbers one <see cref="PdfDocument.AddBatesNumbers(PdfBatesNumbering, Range?)"/> call stamped: the first and last number
/// and their spelled forms, for a production log.
/// </summary>
/// <param name="FirstNumber">The number of the first numbered page.</param>
/// <param name="LastNumber">The number of the last numbered page; <paramref name="FirstNumber"/> minus 1 when no page was numbered.</param>
/// <param name="First">The first page's Bates number as stamped, such as <c>ACME000001</c>; empty when no page was numbered.</param>
/// <param name="Last">The last page's Bates number as stamped; empty when no page was numbered.</param>
/// <remarks>ISO 32000-2 §14.8.2.2.2 (Table 363, <c>Bates</c>).</remarks>
public readonly record struct PdfBatesRange(long FirstNumber, long LastNumber, string First, string Last);
