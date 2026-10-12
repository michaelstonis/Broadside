using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// One stream of a run's content: where its bytes start in the decoded content and the stream it came from, so diagnostics and
/// <see cref="ContentContext.CurrentStream"/> name the right part.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2 and Table 31 (<c>Contents</c>): a page's content may be an array of streams read as their concatenation;
/// a nested stream (form, pattern cell, glyph description, appearance) is one part.
/// </remarks>
/// <param name="Start">The offset of the part's first byte in the run's decoded content.</param>
/// <param name="Reference">The part's stream reference; <see langword="null"/> for a direct stream or synthetic content.</param>
internal readonly record struct ContentPart(int Start, CosReference? Reference);
