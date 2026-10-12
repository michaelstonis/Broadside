namespace Broadside.Fonts;

/// <summary>The character collection a CIDFont or CMap uses: registry, ordering and supplement.</summary>
/// <param name="Registry">The issuer of the ordering, such as <c>Adobe</c>.</param>
/// <param name="Ordering">The name of the character collection within the registry, such as <c>Japan1</c> or <c>Identity</c>.</param>
/// <param name="Supplement">The supplement number of the character collection.</param>
/// <remarks>ISO 32000-2 §9.7.3, Table 114.</remarks>
public sealed record CidSystemInfo(string Registry, string Ordering, int Supplement)
{
    /// <summary>
    /// Returns whether a CMap of this character collection may be used with a CIDFont of <paramref name="other"/>, or the other way
    /// round: the registries and orderings are equal. The supplement is not compared.
    /// </summary>
    /// <param name="other">The other character collection.</param>
    /// <returns>Whether the two are compatible.</returns>
    /// <remarks>ISO 32000-2 §9.7.3. The Identity CMaps are compatible with every CIDFont whatever their system info (§9.7.5.2).</remarks>
    public bool IsCompatibleWith(CidSystemInfo other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Registry, other.Registry, StringComparison.Ordinal) && string.Equals(Ordering, other.Ordering, StringComparison.Ordinal);
    }
}
