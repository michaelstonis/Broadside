namespace Broadside.Objects;

/// <summary>A numeric object: either a <see cref="CosInteger"/> or a <see cref="CosReal"/>.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.3. "Number" in the specification means an object that is either an integer or a real; wherever a real is
/// expected an integer may be used instead, which is what <see cref="ToDouble"/> is for.
/// </remarks>
public abstract class CosNumber : CosObject
{
    private protected CosNumber()
    {
    }

    /// <summary>Returns the value as a double-precision real, whichever kind of number this is.</summary>
    /// <returns>The value. Integers beyond 2^53 in magnitude round to the nearest representable double.</returns>
    public abstract double ToDouble();
}
