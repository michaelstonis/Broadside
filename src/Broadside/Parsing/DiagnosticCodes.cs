using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>
/// The codes of the diagnostics the file reader and the document model record, grouped by area. Codes are stable: tests, logs and users match on them.
/// </summary>
/// <remarks>
/// Every ticket adds its own group here. The catalogue is internal while parallel tickets add codes; the codes reach users as
/// <see cref="Diagnostics.Diagnostic.Code"/> strings.
/// </remarks>
internal static partial class DiagnosticCodes
{
}
