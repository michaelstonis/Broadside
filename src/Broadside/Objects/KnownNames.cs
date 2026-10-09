namespace Broadside.Objects;

/// <summary>Names the library itself looks up, created once. Add a name here when code needs it, not before.</summary>
internal static class KnownNames
{
    /// <summary><c>/Length</c>, the stream dictionary entry that gives the extent of the data (ISO 32000-2 §7.3.8.2, Table 5).</summary>
    public static readonly CosName Length = new("Length");
}
