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
/// <para>
/// A third-party filter, font program parser or security handler reports its own codes, made with <see cref="Create"/>: their
/// names are prefixed with the owner (<c>Contoso.LicenseExpired</c>), and Broadside's names never contain a dot, so the two can
/// never collide. The <c>Broadside</c> owner is reserved.
/// </para>
/// </remarks>
public sealed class DiagnosticCode : IEquatable<DiagnosticCode>
{
    /// <summary>The owner Broadside reserves for itself: a third-party code cannot use it.</summary>
    private const string ReservedOwner = "Broadside";

    internal DiagnosticCode(string name, DiagnosticArea area, DiagnosticSeverity defaultSeverity, string? clause)
        : this(name, area, defaultSeverity, clause, owner: null)
    {
    }

    private DiagnosticCode(string name, DiagnosticArea area, DiagnosticSeverity defaultSeverity, string? clause, string? owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Area = area;
        DefaultSeverity = defaultSeverity;
        Clause = clause;
        Owner = owner;
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

    /// <summary>
    /// Gets the owner of a third-party code made with <see cref="Create"/>, such as <c>Contoso</c>, or <see langword="null"/> for a
    /// code of Broadside's catalog or one recorded from a plain string.
    /// </summary>
    public string? Owner { get; }

    /// <summary>
    /// Creates a code for a third-party filter, font program parser or security handler to report through its context, named
    /// <c><paramref name="owner"/>.<paramref name="name"/></c>.
    /// </summary>
    /// <param name="owner">
    /// Who owns the code: one or more dot-separated identifiers (an ASCII letter, then ASCII letters and digits), such as
    /// <c>Contoso</c> or <c>Contoso.Imaging</c>. Its first identifier may not be the reserved <c>Broadside</c>, in any case.
    /// </param>
    /// <param name="name">The code's PascalCase name within the owner: an uppercase ASCII letter, then ASCII letters and digits.</param>
    /// <param name="defaultSeverity">The severity the code is usually reported with.</param>
    /// <param name="clause">The clause of the specification the code is about, such as <c>ISO 32000-2 §7.6.5</c>, or <see langword="null"/>.</param>
    /// <returns>A code with <see cref="DiagnosticArea.None"/>, outside the catalog, equal to any other code of the same full name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="owner"/> or <paramref name="name"/> is not in the form above, or <paramref name="owner"/> starts with the
    /// reserved <c>Broadside</c>.
    /// </exception>
    /// <remarks>
    /// Broadside's own codes have no dot in their names, so a prefixed code never collides with one of them however the catalog grows.
    /// A diagnostic keeps the code it was reported with, so its <see cref="Diagnostic.Kind"/> has this <see cref="Owner"/>. The
    /// contexts that report codes serve the extension points of ISO 32000-2 §7.4 (filters), §9.9 (font programs) and §7.6.2
    /// (security handlers).
    /// </remarks>
    public static DiagnosticCode Create(string owner, string name, DiagnosticSeverity defaultSeverity, string? clause = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(name);
        if (!IsOwner(owner))
        {
            throw new ArgumentException("The owner shall be one or more dot-separated identifiers, each an ASCII letter followed by ASCII letters and digits.", nameof(owner));
        }

        int firstDot = owner.IndexOf('.', StringComparison.Ordinal);
        if (string.Equals(firstDot < 0 ? owner : owner[..firstDot], ReservedOwner, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Broadside owner is reserved for the library's own catalog; choose a prefix of your own.", nameof(owner));
        }

        if (!IsIdentifier(name) || !char.IsAsciiLetterUpper(name[0]))
        {
            throw new ArgumentException("The name shall be PascalCase: an uppercase ASCII letter followed by ASCII letters and digits.", nameof(name));
        }

        return new DiagnosticCode(string.Concat(owner, ".", name), DiagnosticArea.None, defaultSeverity, clause, owner);
    }

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

    private static bool IsOwner(string owner)
    {
        ReadOnlySpan<char> text = owner;
        foreach (Range segment in text.Split('.'))
        {
            if (!IsIdentifier(text[segment]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifier(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        foreach (char c in text[1..])
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
