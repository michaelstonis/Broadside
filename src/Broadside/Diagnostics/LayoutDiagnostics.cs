namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics flow layout records while it lays content out on pages.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (page objects), §8.4.2 (graphics state stack) and §14.6 (marked content). Each code is reported under
/// <see cref="DiagnosticArea.Layout"/>; see <see cref="DiagnosticCatalog"/> for every area. Layout records them on the document it lays
/// out into (<see cref="PdfDocument.ReportDiagnostic"/>), so they appear in <see cref="PdfDocument.Diagnostics"/>, and a document opened
/// or created in strict mode throws the first one.
/// </para>
/// </remarks>
public static class LayoutDiagnostics
{
    /// <summary>Gets the <c>DrawingStateUnbalanced</c> code: a drawing callback left graphics states saved, marked-content sequences or a
    /// text object open (or closed more than it opened); the layout closed or reopened them to keep the content stream balanced.</summary>
    /// <remarks>ISO 32000-2 §8.4.2</remarks>
    public static DiagnosticCode DrawingStateUnbalanced { get; } = new(nameof(DrawingStateUnbalanced), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.2");

    /// <summary>Gets the <c>KeepTogetherTooTall</c> code: content asked to stay on one page is taller than a page, so it breaks across
    /// pages anyway.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3</remarks>
    public static DiagnosticCode KeepTogetherTooTall { get; } = new(nameof(KeepTogetherTooTall), DiagnosticArea.Layout, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        DrawingStateUnbalanced,
        KeepTogetherTooTall,
    ];
}
