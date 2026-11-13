using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for name trees, number trees, destinations, outlines and actions (issue #70).</summary>
internal static partial class DiagnosticCodes
{
    // Actions (§12.6.2, Table 196).
    public static string ActionInvalid => ActionDiagnostics.ActionInvalid.Name;
}
