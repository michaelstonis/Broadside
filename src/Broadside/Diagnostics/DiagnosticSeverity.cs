namespace Broadside.Diagnostics;

/// <summary>How much the repair recorded by a <see cref="Diagnostic"/> may have changed the document.</summary>
/// <remarks>ADR 0005. There is no informational level: a well-formed file produces no diagnostics at all.</remarks>
public enum DiagnosticSeverity
{
    /// <summary>The file deviates from the specification, and the repair is believed to have kept all of its content.</summary>
    Warning = 0,

    /// <summary>The file deviates from the specification, and the repair probably lost or invented content.</summary>
    Error = 1,
}
