namespace Broadside.Objects;

/// <summary>Resolves the value of a stream dictionary's <c>Length</c> entry when it is an indirect reference.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.8.2 (Table 5): <c>Length</c> may be an indirect reference to an integer object, which only the file reader can
/// resolve. The parser calls the resolver for an indirect <c>Length</c>; when the result is <see langword="null"/>, or the data does
/// not end at <c>endstream</c>, the parser finds the data by scanning for <c>endstream</c> and reports a repair.
/// </remarks>
internal interface IStreamLengthResolver
{
    /// <summary>Returns the byte count <paramref name="lengthEntry"/> stands for, or <see langword="null"/> when it cannot be resolved.</summary>
    /// <param name="lengthEntry">The <c>Length</c> entry as stored in the stream dictionary.</param>
    /// <returns>The length, or <see langword="null"/>.</returns>
    long? ResolveLength(CosObject lengthEntry);
}
