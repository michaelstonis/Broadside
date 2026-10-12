namespace Broadside;

/// <summary>How a full save numbers the indirect objects it writes.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.10 (object and generation numbers) and §7.5.4 (a cross-reference table lists every number from 0 to the
/// highest). Numbers are the file's business, not the document's: whichever numbering is chosen, every reference is written to the
/// object it named, so the saved document reads the same.
/// </para>
/// </remarks>
public enum PdfObjectNumbering
{
    /// <summary>
    /// Objects keep their numbers and generations, so an object that has not changed keeps its exact bytes. A numbering that cannot be
    /// written compactly is renumbered as <see cref="Compact"/> does instead: a number above 8,388,607 (the limit of ISO 32000-1
    /// Annex C), or numbers above 1,048,576 that are more than 16 times as many as the objects, which would make the cross-reference
    /// information list millions of unused numbers.
    /// </summary>
    Automatic,

    /// <summary>
    /// Objects are numbered 1, 2, 3 and so on in the order of their old numbers, all with generation 0, leaving no unused number.
    /// Every object is serialized with its references renumbered; stream data is written as stored, still encoded.
    /// </summary>
    Compact,
}
