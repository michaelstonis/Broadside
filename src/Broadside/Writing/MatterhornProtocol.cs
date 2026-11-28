using System.Collections.Frozen;

namespace Broadside.Writing;

/// <summary>
/// The failure conditions of the Matterhorn Protocol 1.1 (PDF Association, 2021-04, CC BY 4.0), the PDF/UA-1 conformance testing
/// model: each condition's index (<c>01-003</c>), the ISO 14289-1 clause it tests, and whether software decides it (Machine) or it
/// usually needs a person (Human). <see cref="PdfDocument.ValidatePdfUA"/> uses the table to cite the checkpoint a violation fails,
/// to list the machine conditions <see cref="PdfUAChecker"/> does not check yet (issue #527), and to list the human ones for review.
/// </summary>
/// <remarks>
/// Transcribed from the failure-condition tables of the Matterhorn Protocol 1.1 (<c>Specs/References/Matterhorn-Protocol-1-1.pdf</c>):
/// 87 machine conditions, 48 human ones (the 47 the introduction counts plus 13-008, added in 1.1) and two with no test of their own
/// (23-001, 27-001).
/// </remarks>
internal static class MatterhornProtocol
{
    /// <summary>
    /// Every failure condition, in protocol order: index, the ISO 14289-1 clause of its "Section" column, and its "How" column
    /// (<see cref="How.None"/> for the two conditions with no specific test).
    /// </summary>
    public static readonly IReadOnlyList<(string Index, string Clause, How How)> Conditions =
    [
        ("01-001", "7.1", How.Human), ("01-002", "7.1", How.Human), ("01-003", "7.1", How.Machine), ("01-004", "7.1", How.Machine),
        ("01-005", "7.1", How.Machine), ("01-006", "7.1", How.Human), ("01-007", "7.1", How.Machine),
        ("02-001", "7.1", How.Machine), ("02-002", "7.1", How.Human), ("02-003", "7.1", How.Machine), ("02-004", "7.1", How.Machine),
        ("03-001", "7.1", How.Human), ("03-002", "7.1", How.Human), ("03-003", "7.1", How.Human),
        ("04-001", "7.1", How.Human),
        ("05-001", "7.1", How.Human), ("05-002", "7.1", How.Human), ("05-003", "7.1", How.Human),
        ("06-001", "7.1", How.Machine), ("06-002", "5", How.Machine), ("06-003", "7.1", How.Machine), ("06-004", "7.1", How.Human),
        ("07-001", "7.1", How.Machine), ("07-002", "7.1", How.Machine),
        ("08-001", "7.1", How.Human), ("08-002", "7.1", How.Human),
        ("09-001", "7.2", How.Human), ("09-002", "7.2", How.Human), ("09-003", "7.2", How.Human), ("09-004", "7.2", How.Machine),
        ("09-005", "7.2", How.Machine), ("09-006", "7.2", How.Machine), ("09-007", "7.2", How.Machine), ("09-008", "7.2", How.Machine),
        ("10-001", "7.2", How.Machine),
        ("11-001", "7.2", How.Machine), ("11-002", "7.2", How.Machine), ("11-003", "7.2", How.Machine), ("11-004", "7.2", How.Machine),
        ("11-005", "7.2", How.Machine), ("11-006", "7.2", How.Machine), ("11-007", "7.2", How.Human),
        ("12-001", "7.2", How.Human),
        ("13-001", "7.3", How.Human), ("13-002", "7.3", How.Human), ("13-003", "7.3", How.Human), ("13-004", "7.3", How.Machine),
        ("13-005", "7.3", How.Human), ("13-006", "7.3", How.Human), ("13-007", "7.3", How.Human), ("13-008", "7.3", How.Human),
        ("14-001", "7.4", How.Human), ("14-002", "7.4.2", How.Machine), ("14-003", "7.4", How.Machine), ("14-004", "7.4.3", How.Human),
        ("14-005", "7.4.3", How.Human), ("14-006", "7.4.4", How.Machine), ("14-007", "7.4.4", How.Machine),
        ("15-001", "7.5", How.Human), ("15-002", "7.5", How.Human), ("15-003", "7.5", How.Machine), ("15-004", "7.5", How.Human),
        ("15-005", "7.5", How.Human),
        ("16-001", "7.6", How.Human), ("16-002", "7.6", How.Human), ("16-003", "7.6", How.Human),
        ("17-001", "7.7", How.Human), ("17-002", "7.7", How.Machine), ("17-003", "7.7", How.Machine),
        ("18-001", "7.8", How.Human), ("18-002", "7.8", How.Human),
        ("19-001", "7.9", How.Human), ("19-002", "7.9", How.Human), ("19-003", "7.9", How.Machine), ("19-004", "7.9", How.Machine),
        ("20-001", "7.10", How.Machine), ("20-002", "7.10", How.Machine), ("20-003", "7.10", How.Machine),
        ("21-001", "7.11", How.Machine),
        ("22-001", "7.12", How.Human),
        ("23-001", "7.13", How.None),
        ("24-001", "7.14", How.Human),
        ("25-001", "7.15", How.Machine),
        ("26-001", "7.16", How.Machine), ("26-002", "7.16", How.Machine),
        ("27-001", "7.17", How.None),
        ("28-001", "7.18.1", How.Human), ("28-002", "7.18.1", How.Machine), ("28-003", "7.18.1", How.Human), ("28-004", "7.18.1", How.Machine),
        ("28-005", "7.18.1", How.Machine), ("28-006", "7.18.2", How.Machine), ("28-007", "7.18.2", How.Machine), ("28-008", "7.18.3", How.Machine),
        ("28-009", "7.18.3", How.Machine), ("28-010", "7.18.4", How.Machine), ("28-011", "7.18.5", How.Machine), ("28-012", "7.18.5", How.Machine),
        ("28-013", "7.18.5", How.Human), ("28-014", "7.18.6.2", How.Machine), ("28-015", "7.18.6.2", How.Machine), ("28-016", "7.18.7", How.Machine),
        ("28-017", "7.18.8", How.Machine), ("28-018", "7.18.8", How.Machine),
        ("29-001", "7.19", How.Human),
        ("30-001", "7.20", How.Machine), ("30-002", "7.20", How.Machine),
        ("31-001", "7.21.3", How.Machine), ("31-002", "7.21.3.1", How.Machine), ("31-003", "7.21.3.1", How.Machine), ("31-004", "7.21.3.2", How.Machine),
        ("31-005", "7.21.3.2", How.Machine), ("31-006", "7.21.3.3", How.Machine), ("31-007", "7.21.3.3", How.Machine), ("31-008", "7.21.3.3", How.Machine),
        ("31-009", "7.21.4.1", How.Machine), ("31-010", "7.21.4.1", How.Human), ("31-011", "7.21.4.1", How.Machine), ("31-012", "7.21.4.2", How.Machine),
        ("31-013", "7.21.4.2", How.Machine), ("31-014", "7.21.4.2", How.Machine), ("31-015", "7.21.4.2", How.Machine), ("31-016", "7.21.5", How.Machine),
        ("31-017", "7.21.6", How.Machine), ("31-018", "7.21.6", How.Machine), ("31-019", "7.21.6", How.Machine), ("31-020", "7.21.6", How.Machine),
        ("31-021", "7.21.6", How.Machine), ("31-022", "7.21.6", How.Machine), ("31-023", "7.21.6", How.Machine), ("31-024", "7.21.6", How.Machine),
        ("31-025", "7.21.6", How.Machine), ("31-026", "7.21.6", How.Machine), ("31-027", "7.21.7", How.Machine), ("31-028", "7.21.7", How.Machine),
        ("31-029", "7.21.7", How.Machine), ("31-030", "7.21.8", How.Machine),
    ];

    /// <summary>
    /// The checkpoint each PDF/UA-1 rule the checker reports fails by default (veraPDF PDF/UA-1 rule identifier to Matterhorn index);
    /// <see langword="null"/> for a rule with no Matterhorn condition (6.2-1, <c>MarkInfo</c> <c>Marked</c>). Where one rule covers
    /// several conditions, the checker names the precise one when it reports (7.1-10, 7.2-29, 7.4.2-1, 7.18.3-1, 7.21.6-2, 7.21.7-2).
    /// </summary>
    public static readonly FrozenDictionary<string, string?> CheckpointOfRule = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["5-1"] = "06-002",
        ["5-2"] = "06-002",
        ["6.2-1"] = null,
        ["7.1-1"] = "01-003",
        ["7.1-2"] = "01-004",
        ["7.1-3"] = "01-005",
        ["7.1-4"] = "01-007",
        ["7.1-5"] = "02-001",
        ["7.1-7"] = "02-004",
        ["7.1-8"] = "06-001",
        ["7.1-9"] = "06-003",
        ["7.1-10"] = "07-001",
        ["7.1-11"] = "01-005",
        ["7.2-3"] = "09-004",
        ["7.2-4"] = "09-004",
        ["7.2-5"] = "09-004",
        ["7.2-6"] = "09-004",
        ["7.2-7"] = "09-004",
        ["7.2-8"] = "09-004",
        ["7.2-9"] = "09-004",
        ["7.2-10"] = "09-004",
        ["7.2-11"] = "09-004",
        ["7.2-12"] = "09-004",
        ["7.2-13"] = "09-004",
        ["7.2-14"] = "09-004",
        ["7.2-16"] = "09-004",
        ["7.2-17"] = "09-005",
        ["7.2-18"] = "09-005",
        ["7.2-19"] = "09-005",
        ["7.2-20"] = "09-005",
        ["7.2-26"] = "09-006",
        ["7.2-27"] = "09-006",
        ["7.2-29"] = "11-001",
        ["7.2-33"] = "11-006",
        ["7.2-36"] = "09-004",
        ["7.2-37"] = "09-004",
        ["7.2-38"] = "09-004",
        ["7.2-39"] = "09-004",
        ["7.2-40"] = "09-005",
        ["7.3-1"] = "13-004",
        ["7.4.2-1"] = "14-003",
        ["7.4.4-1"] = "14-006",
        ["7.4.4-2"] = "14-007",
        ["7.5-1"] = "15-003",
        ["7.9-1"] = "19-003",
        ["7.9-2"] = "19-004",
        ["7.15-1"] = "25-001",
        ["7.18.1-1"] = "28-002",
        ["7.18.1-2"] = "28-004",
        ["7.18.1-3"] = "28-005",
        ["7.18.2-1"] = "28-007",
        ["7.18.3-1"] = "28-008",
        ["7.18.4-1"] = "28-010",
        ["7.18.5-1"] = "28-011",
        ["7.18.5-2"] = "28-012",
        ["7.21.4.1-1"] = "31-009",
        ["7.21.6-2"] = "31-021",
        ["7.21.7-1"] = "31-027",
        ["7.21.7-2"] = "31-028",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The machine conditions a validation checks in full. 10-001 and 17-003 (character codes mapped to Unicode) are the check of
    /// 31-027; 31-019 and 31-020 are reported under rule 7.21.6-2 with 31-021.
    /// </summary>
    public static readonly FrozenSet<string> Checked = new[]
    {
        "01-003", "01-004", "01-005", "01-007", "02-001", "02-004", "06-001", "06-002", "06-003", "07-001", "07-002", "09-004", "09-005",
        "09-006", "10-001", "11-006", "13-004", "14-002", "14-003", "14-006", "14-007", "15-003", "17-003", "19-003", "19-004", "25-001",
        "28-002", "28-004", "28-005", "28-007", "28-008", "28-009", "28-010", "28-011", "28-012", "31-009", "31-019", "31-020", "31-021",
        "31-027", "31-028", "31-029",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The machine conditions not checked yet (issue #527), in protocol order.</summary>
    public static readonly IReadOnlyList<string> NotChecked =
        [.. Conditions.Where(condition => condition.How == How.Machine && !Checked.Contains(condition.Index)).Select(condition => condition.Index)];

    /// <summary>The conditions that need human judgment, in protocol order: no software decides them.</summary>
    public static readonly IReadOnlyList<string> Human =
        [.. Conditions.Where(condition => condition.How == How.Human).Select(condition => condition.Index)];

    /// <summary>The "How" column of a failure condition.</summary>
    public enum How
    {
        /// <summary>No specific test: other provisions apply (23-001, 27-001).</summary>
        None,

        /// <summary>M: software decides it.</summary>
        Machine,

        /// <summary>H: it usually needs human judgment.</summary>
        Human,
    }
}
