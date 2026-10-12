using System.Diagnostics.CodeAnalysis;

namespace Broadside.Graphics;

/// <summary>The two kinds of pattern, the <c>PatternType</c> entry of a pattern dictionary.</summary>
/// <remarks>ISO 32000-2 §8.7.1, Tables 74 and 75.</remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The values are the numbers the PDF entry holds; 0 is not one of them.")]
public enum PdfPatternType
{
    /// <summary>A tiling pattern: a small graphical figure replicated at fixed intervals (§8.7.3).</summary>
    Tiling = 1,

    /// <summary>A shading pattern: a smooth gradient fill (§8.7.4).</summary>
    Shading = 2,
}

/// <summary>How a tiling pattern's colour is specified, its <c>PaintType</c> entry.</summary>
/// <remarks>ISO 32000-2 §8.7.3.1, Table 74.</remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The values are the numbers the PDF entry holds; 0 is not one of them.")]
public enum PdfTilingPaintType
{
    /// <summary>Coloured (1): the cell's content stream sets its own colours (§8.7.3.2).</summary>
    Colored = 1,

    /// <summary>Uncoloured (2): the cell is a stencil painted in the colour given with the pattern (§8.7.3.3).</summary>
    Uncolored = 2,
}

/// <summary>How a tiling pattern's spacing may be adjusted to the device pixel grid, its <c>TilingType</c> entry.</summary>
/// <remarks>ISO 32000-2 §8.7.3.1, Table 74.</remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The values are the numbers the PDF entry holds; 0 is not one of them.")]
public enum PdfTilingType
{
    /// <summary>Constant spacing (1): the cell may be distorted by up to one device pixel.</summary>
    ConstantSpacing = 1,

    /// <summary>No distortion (2): the spacing may vary by up to one device pixel.</summary>
    NoDistortion = 2,

    /// <summary>Constant spacing and faster tiling (3): more distortion is allowed.</summary>
    ConstantSpacingFaster = 3,
}
