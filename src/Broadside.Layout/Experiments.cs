namespace Broadside.Layout.Flow;

/// <summary>
/// The diagnostic identifiers of the experimental surfaces of this package. Using an experimental type or member reports the identifier as
/// an error until the caller suppresses it (<c>&lt;NoWarn&gt;BROADSIDE0001&lt;/NoWarn&gt;</c> or <c>#pragma warning disable BROADSIDE0001</c>),
/// so a caller opts in knowingly: the surface may change in a minor release before it is stabilized.
/// </summary>
internal static class Experiments
{
    /// <summary>The document builder (<see cref="DocumentBuilder"/> and its scopes, elements and values): experimental in 1.0.</summary>
    public const string DocumentBuilder = "BROADSIDE0001";

    /// <summary>Where the experimental document builder is described.</summary>
    public const string DocumentBuilderUrl = "https://github.com/michaelstonis/Broadside/issues/397";
}
