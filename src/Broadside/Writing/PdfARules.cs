using System.Globalization;

namespace Broadside.Writing;

/// <summary>
/// The scope of a PDF/A validation (issue #651): the level a file claims in its XMP identification, and the veraPDF rules the
/// checker leaves to veraPDF (tracked by issue #533). As #533 adds a rule to <see cref="PdfAChecker"/>, it takes the rule out of
/// <see cref="NotChecked"/>.
/// </summary>
/// <remarks>
/// ISO 19005-2 and ISO 19005-3, 6.6.4; ISO 19005-4, 6.7.3 (veraPDF rules 6.6.4-2, 6.6.4-3, 6.7.3-2, 6.7.3-3 and 6.7.3-5:
/// <c>pdfaid:part</c>, <c>pdfaid:conformance</c>, <c>pdfaid:rev</c>).
/// </remarks>
internal static class PdfARules
{
    /// <summary>
    /// The veraPDF rules of PDF/A-2 and PDF/A-3 the checker does not check, or checks only in part: a rule identifier
    /// (<c>6.2.5-5</c>) or a clause, for every rule under it (<c>6.2.11.2</c>).
    /// </summary>
    private static readonly string[] NotCheckedPart2 =
    [
        // Issue #533.
        "6.1.7.1-3", "6.1.7.2-1", "6.2.2-2", "6.2.5-5", "6.2.5-6", "6.2.8-4", "6.2.8-5", "6.2.8.3", "6.2.11.2", "6.2.11.6-1", "6.2.11.6-2",
        "6.2.11.6-4", "6.4.3", "6.8-5",

        // File syntax the parser does not record (issue #651 checks only what it records or repairs).
        "6.1.4", "6.1.6-1", "6.1.9", "6.1.13",

        // Colour, fonts, annotations and metadata the checker leaves to veraPDF.
        "6.2.4.2", "6.2.11.3.1", "6.2.11.3.3", "6.2.11.7.1", "6.3.3-4", "6.6.2.1-5", "6.6.2.3", "6.6.4-4", "6.6.4-5",
        "6.6.4-6", "6.6.4-7",
    ];

    /// <summary>The veraPDF rules of PDF/A-4 the checker does not check, or checks only in part, as for <see cref="NotCheckedPart2"/>.</summary>
    private static readonly string[] NotCheckedPart4 =
    [
        // Issue #533.
        "6.1.6", "6.1.9", "6.2.5-5", "6.2.5-6", "6.2.7.1-4", "6.2.7.1-5", "6.2.7.3", "6.2.8", "6.2.10.2", "6.2.10.6-1", "6.2.10.6-2",
        "6.2.10.6-4", "6.4", "6.10", "6.9-3",

        // File syntax the parser does not record.
        "6.1.4", "6.1.5-1", "6.1.13",

        // Colour, fonts and metadata the checker leaves to veraPDF.
        "6.2.4.2", "6.2.10.3.1", "6.2.10.3.3", "6.2.10.7", "6.7.2-5", "6.7.3-4", "6.7.3-6",
    ];

    /// <summary>The veraPDF rules of <paramref name="conformance"/>'s profile the checker leaves to veraPDF (issue #533).</summary>
    public static IReadOnlyList<string> NotChecked(PdfAConformance conformance) => conformance.Part() == 4 ? NotCheckedPart4 : NotCheckedPart2;

    /// <summary>
    /// The level the file's XMP identification claims: <c>pdfaid:part</c> and <c>pdfaid:conformance</c> (none for PDF/A-4, F for
    /// PDF/A-4f). Each level <see cref="PdfAConformance"/> defines is found by its part and conformance, so a level added to the
    /// enumeration is found without a change here.
    /// </summary>
    /// <param name="packet">The catalog's XMP packet, or <see langword="null"/>.</param>
    /// <param name="claimed">The level, or <see langword="null"/> when the file claims none.</param>
    /// <param name="description">The claim as text (<c>PDF/A-2a</c>), when the file claims a level; <see langword="null"/> otherwise.</param>
    /// <returns>Whether the claim is a level <see cref="PdfAConformance"/> defines; <see langword="false"/> for no claim too.</returns>
    public static bool TryGetClaimed(XmpPacket? packet, out PdfAConformance? claimed, out string? description)
    {
        claimed = null;
        description = null;
        if (packet?.PdfAPart is not { } part)
        {
            return false;
        }

        string? level = packet.PdfAConformance;
        description = string.Create(CultureInfo.InvariantCulture, $"PDF/A-{part}{level?.ToLowerInvariant()}");
        foreach (PdfAConformance candidate in Enum.GetValues<PdfAConformance>())
        {
            if (candidate != PdfAConformance.None && candidate.Part() == part && string.Equals(candidate.Level(), level, StringComparison.OrdinalIgnoreCase))
            {
                claimed = candidate;
                return true;
            }
        }

        return false;
    }
}
