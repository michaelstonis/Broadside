namespace Broadside.Objects;

/// <summary>The null object. There is exactly one, <see cref="Instance"/>.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.9. A dictionary entry whose value is null is the same as an absent entry, so <see cref="CosDictionary"/> never
/// stores one; an indirect reference to an object that does not exist resolves to null.
/// </remarks>
public sealed class CosNull : CosObject
{
    private CosNull()
    {
    }

    /// <summary>Gets the null object.</summary>
    public static CosNull Instance { get; } = new();
}
