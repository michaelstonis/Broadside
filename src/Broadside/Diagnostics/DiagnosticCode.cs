namespace Broadside.Diagnostics;

/// <summary>A kind of deviation a <see cref="Diagnostic"/> records: its stable name, its area, its default severity and the clause it is about.</summary>
/// <remarks>
/// <para>
/// Broadside's codes are listed by area in the catalog classes, such as <see cref="FileStructureDiagnostics"/> and
/// <see cref="FontDiagnostics"/>; <see cref="DiagnosticCatalog"/> finds one by name. A code is equal to another code with the same
/// <see cref="Name"/>, so <c>diagnostic.Kind == FileStructureDiagnostics.StartxrefMissing</c> and
/// <c>diagnostic.Code == "StartxrefMissing"</c> select the same diagnostics.
/// </para>
/// <para>
/// The <see cref="DefaultSeverity"/> is the severity the code is reported with unless the repair says otherwise; the severity of
/// one occurrence is <see cref="Diagnostic.Severity"/>. Recording repairs instead of throwing, with severities that tell how much a
/// repair may have changed the document, is how Broadside reads damaged files (ISO 32000-2 §7.5 describes the structure a reader
/// repairs, not the repair).
/// </para>
/// </remarks>
public sealed class DiagnosticCode : IEquatable<DiagnosticCode>
{
    internal DiagnosticCode(string name, DiagnosticArea area, DiagnosticSeverity defaultSeverity, string? clause)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Area = area;
        DefaultSeverity = defaultSeverity;
        Clause = clause;
    }

    /// <summary>Gets the stable PascalCase name of the code, such as <c>StartxrefMissing</c>; it is <see cref="Diagnostic.Code"/>.</summary>
    public string Name { get; }

    /// <summary>Gets the area of the library that reports the code, or <see cref="DiagnosticArea.None"/> for a code outside the catalog.</summary>
    public DiagnosticArea Area { get; }

    /// <summary>Gets the severity the code is reported with unless the repair says otherwise.</summary>
    public DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>
    /// Gets the clause of the specification (or the technical note or recommendation) the code is about, such as
    /// <c>ISO 32000-2 §7.5.5</c>, or <see langword="null"/> for a code outside the catalog.
    /// </summary>
    public string? Clause { get; }

    /// <summary>Returns whether two codes have the same name.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when both are <see langword="null"/> or have the same <see cref="Name"/>.</returns>
    public static bool operator ==(DiagnosticCode? left, DiagnosticCode? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Returns whether two codes have different names.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when exactly one is <see langword="null"/> or the names differ.</returns>
    public static bool operator !=(DiagnosticCode? left, DiagnosticCode? right) => !(left == right);

    /// <inheritdoc/>
    public bool Equals(DiagnosticCode? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DiagnosticCode);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <summary>Returns the <see cref="Name"/>.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
