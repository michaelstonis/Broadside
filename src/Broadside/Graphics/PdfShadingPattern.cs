using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A shading pattern (PatternType 2): an area filled with a shading, in pattern space.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.1, Table 75. Painting with it fills the painted area with <see cref="Shading"/> (its coordinates in pattern
/// space, see <see cref="PdfPattern.GetPatternSpace"/>), and with the shading's <see cref="PdfShading.Background"/> outside its
/// bounds. <see cref="ExtGState"/> holds graphics state parameters in effect while painting; per §11.6.7 only those that affect
/// <c>sh</c> apply, and the others come from the state at the beginning of the stream that selected the pattern.
/// </remarks>
public sealed class PdfShadingPattern : PdfPattern
{
    internal PdfShadingPattern(ShadingReader reader)
        : base(reader, PdfPatternType.Shading)
    {
        CosObject? shading = reader.Get(ShadingNames.Shading);
        Shading = shading is null ? null : reader.Document.Shadings.GetShading(shading, reader.DiagnosticReference);
        if (Shading is not { IsValid: true })
        {
            reader.Invalid(DiagnosticCodes.PatternEntryInvalid, "A shading pattern's Shading entry is missing or not a usable shading.");
        }

        ExtGState = reader.Resolve(reader.Get(ShadingNames.ExtGState)) as CosDictionary;
        IsValid = reader.IsValid;
    }

    /// <summary>Gets the shading, or <see langword="null"/> when the entry is missing or not a shading.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.1, Table 75 (<c>Shading</c>, required).</remarks>
    public PdfShading? Shading { get; }

    /// <summary>Gets the graphics state parameter dictionary in effect while painting the shading, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.1, Table 75 (<c>ExtGState</c>), and §11.6.7.</remarks>
    public CosDictionary? ExtGState { get; }
}
