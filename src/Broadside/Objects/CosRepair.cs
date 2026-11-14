using System.Globalization;

using Broadside.Diagnostics;

namespace Broadside.Objects;

/// <summary>
/// One deviation from ISO 32000-2 §7.2 or §7.3 the parser found, and repaired, while reading objects.
/// </summary>
/// <remarks>
/// The object loader turns each repair into a public <see cref="Diagnostics.Diagnostic"/> with the same <see cref="Code"/>, the
/// absolute offset and the object (issue #41). Codes are stable PascalCase identifiers, listed in <see cref="Diagnostics.ObjectSyntaxDiagnostics"/>.
/// </remarks>
/// <param name="Code">The stable identifier of the kind of deviation.</param>
/// <param name="Offset">The byte offset in the parsed source where the deviation was found.</param>
/// <param name="Message">A human-readable description of the deviation and the repair.</param>
internal readonly record struct CosRepair(DiagnosticCode Code, int Offset, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Code} at offset {Offset}: {Message}");
}

/// <summary>Collects the <see cref="CosRepair"/> records a parse produces.</summary>
/// <param name="keepAll"><see langword="true"/> to keep every repair in <see cref="All"/>; otherwise only the first and the count are kept.</param>
internal sealed class CosRepairLog(bool keepAll = false)
{
    private List<CosRepair>? _all;

    /// <summary>Gets the first repair, or <see langword="null"/> when there was none.</summary>
    public CosRepair? First { get; private set; }

    /// <summary>Gets the number of repairs.</summary>
    public int Count { get; private set; }

    /// <summary>Gets every repair, in order, when the log keeps them all; otherwise only the first.</summary>
    public IReadOnlyList<CosRepair> All => _all ?? (First is { } first ? [first] : []);

    /// <summary>Records a repair.</summary>
    public void Report(DiagnosticCode code, int offset, string message)
    {
        var repair = new CosRepair(code, offset, message);
        First ??= repair;
        Count++;
        if (keepAll)
        {
            (_all ??= []).Add(repair);
        }
    }
}
