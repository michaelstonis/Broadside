namespace Broadside.Graphics;

/// <summary>How <see cref="ManagedColorManagement"/> converts DeviceCMYK to RGB and gray.</summary>
/// <remarks>ISO 32000-2 §10.4.2.4 and §10.4.2.5.</remarks>
public enum CmykConversion
{
    /// <summary>
    /// The default: a polynomial fitted to the CGATS TR 001 (SWOP) characterisation data, close to what viewers using a SWOP
    /// profile show (100 % cyan is about (0, 0.69, 0.94), not (0, 1, 1)); gray is then 0.3 r + 0.59 g + 0.11 b of that colour.
    /// </summary>
    Characterized,

    /// <summary>
    /// The formulas of §10.4.2: red = 1 − min(1, c + k), green = 1 − min(1, m + k), blue = 1 − min(1, y + k), and gray =
    /// 1 − min(1, 0.3 c + 0.59 m + 0.11 y + k), which §10.4.2.1 calls crude approximations.
    /// </summary>
    Classic,
}
