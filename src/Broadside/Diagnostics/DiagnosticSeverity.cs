namespace Broadside.Diagnostics;

/// <summary>How much the repair recorded by a <see cref="Diagnostic"/> may have changed the document.</summary>
/// <remarks>
/// ADR 0005. Ordered: a higher value is more severe. A well-formed file produces no <see cref="Warning"/> and no
/// <see cref="Error"/>; strict mode throws on those two and never on <see cref="Information"/>.
/// </remarks>
public enum DiagnosticSeverity
{
    /// <summary>
    /// The file is valid, but uses a feature the library reads and preserves without supporting it (parse-and-preserve: XFA,
    /// JavaScript, multimedia). Nothing was repaired; strict mode does not throw.
    /// </summary>
    Information = 0,

    /// <summary>The file deviates from the specification, and the repair is believed to have kept all of its content.</summary>
    Warning = 1,

    /// <summary>The file deviates from the specification, and the repair probably lost or invented content.</summary>
    Error = 2,
}
