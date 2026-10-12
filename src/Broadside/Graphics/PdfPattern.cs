using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>
/// A pattern: a tiling pattern (<see cref="PdfTilingPattern"/>) or a shading pattern (<see cref="PdfShadingPattern"/>), used as a
/// colour in the Pattern colour space, resolved from its dictionary or stream into a device-independent model.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.1 and §8.7.2. Get one with <see cref="PdfDocument.GetPattern"/> or, while content runs, from a colour with
/// <see cref="Content.ContentContext.GetPattern"/>. Like shadings, a pattern is an immutable snapshot cached per document and read
/// again only after the object changes through the public API.
/// </para>
/// <para>
/// Pattern space: <see cref="Matrix"/> maps it to the default coordinate space of the content stream in which the pattern was
/// selected (the page's default space, the form's space at <c>Do</c> for a form, the outer pattern's space for a pattern used in a
/// pattern), not to the user space in effect when something is painted with it. A colour captures that space when <c>scn</c> runs
/// (<see cref="PdfColor.PatternMatrix"/>), and <see cref="GetPatternSpace"/> combines the two.
/// </para>
/// </remarks>
public abstract class PdfPattern
{
    private protected PdfPattern(ShadingReader reader, PdfPatternType type)
    {
        PatternType = type;
        CosObject = reader.CosObject;
        Dictionary = reader.Dictionary;
        Reference = reader.Reference;
        Matrix = reader.Matrix(ShadingNames.Matrix, DiagnosticCodes.PatternEntryInvalid);
    }

    /// <summary>Gets the pattern's type, its <c>PatternType</c> entry.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1 (Table 74) and §8.7.4.1 (Table 75).</remarks>
    public PdfPatternType PatternType { get; }

    /// <summary>Gets the pattern object: a <see cref="CosStream"/> for a tiling pattern, a <see cref="CosDictionary"/> for a shading pattern.</summary>
    public CosObject CosObject { get; }

    /// <summary>Gets the pattern dictionary: the dictionary itself, or the stream's dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference the pattern was first reached through, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the pattern matrix, which maps pattern space to the default space of the stream that selected the pattern. Default the identity.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, Tables 74 and 75 (<c>Matrix</c>).</remarks>
    public Matrix Matrix { get; }

    /// <summary>Gets the earliest PDF version that has this kind of pattern: 1.2 for tiling patterns, 1.3 for shading patterns.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.2 and §8.7.4.1 (ADR 0003).</remarks>
    public PdfVersion MinimumVersion => PatternType == PdfPatternType.Tiling ? new PdfVersion(1, 2) : new PdfVersion(1, 3);

    /// <summary>Gets a value indicating whether the pattern can be painted; when <see langword="false"/> it paints nothing.</summary>
    public bool IsValid { get; private protected set; }

    /// <summary>Returns the matrix from pattern space to the run's default user space, for a colour that selected this pattern.</summary>
    /// <param name="color">The pattern colour, as <c>scn</c> or <c>SCN</c> set it.</param>
    /// <returns><see cref="Matrix"/> followed by the colour's <see cref="PdfColor.PatternMatrix"/>.</returns>
    /// <remarks>ISO 32000-2 §8.7.2: never the CTM at the time of painting.</remarks>
    public Matrix GetPatternSpace(in PdfColor color) => Matrix * color.PatternMatrix;
}
