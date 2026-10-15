using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// The device-dependent graphics state parameters a display list carries for a backend whose device can honour them: overprint
/// control and the halftone. A backend receives them in <see cref="IRenderingBackend.SetDeviceParameters"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.7 (overprint control, <c>OP</c>, <c>op</c>, <c>OPM</c>) and §10.6 (halftones, <c>HT</c>, <c>HTO</c>), Table 52.
/// They are recorded, not applied: whether they have any effect depends on the device. A composite display that does not overprint
/// ignores the overprint parameters, as §8.6.7 requires of a device that does not support overprinting, and a continuous-tone device
/// needs no halftone (§10.6.1). A separation, CMYK or overprint-preview backend can use them.
/// </para>
/// <para>
/// The other device-dependent parameters travel elsewhere: rendering intent, black point compensation, black generation,
/// undercolour removal and the transfer function with each colour, in its <see cref="ColorConversion"/>; flatness and smoothness are
/// the backend's tolerances; stroke adjustment with each stroke (<see cref="StrokeStyle"/>).
/// </para>
/// </remarks>
public readonly record struct DisplayDeviceParameters
{
    internal DisplayDeviceParameters(bool strokeOverprint, bool fillOverprint, int overprintMode, CosObject? halftone, PathPoint? halftoneOrigin)
    {
        StrokeOverprint = strokeOverprint;
        FillOverprint = fillOverprint;
        OverprintMode = overprintMode;
        Halftone = halftone;
        HalftoneOrigin = halftoneOrigin;
    }

    /// <summary>Gets a value indicating whether strokes overprint: whether painting leaves colourants the stroke does not specify unchanged.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52 (<c>OP</c>). Applies to the paints that use the stroking alpha constant.</remarks>
    public bool StrokeOverprint { get; }

    /// <summary>Gets a value indicating whether every other paint overprints.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52 (<c>op</c>, else <c>OP</c>).</remarks>
    public bool FillOverprint { get; }

    /// <summary>Gets the overprint mode, 0 or 1: with 1, a DeviceCMYK component of 0 leaves that colourant unchanged when overprinting.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52 (<c>OPM</c>, PDF 1.3).</remarks>
    public int OverprintMode { get; }

    /// <summary>
    /// Gets the halftone as written, a halftone dictionary or stream (Types 1, 5, 6, 10 and 16), or <see langword="null"/> for the
    /// device's default.
    /// </summary>
    /// <remarks>ISO 32000-2 §10.6.5, Tables 127 to 132 (<c>HT</c>). Halftones are defined in device space (§10.6.1).</remarks>
    public CosObject? Halftone { get; }

    /// <summary>Gets the halftone origin in device space, or <see langword="null"/> when not set.</summary>
    /// <remarks>ISO 32000-2 §10.6.5, Table 57 (<c>HTO</c>, PDF 2.0).</remarks>
    public PathPoint? HalftoneOrigin { get; }

    /// <summary>Gets the parameters a replay starts with: no overprint, mode 0, the device's halftone (ISO 32000-2 §8.4.1, Table 52).</summary>
    internal static DisplayDeviceParameters Initial => default;

    /// <summary>Returns the parameters of a graphics state.</summary>
    internal static DisplayDeviceParameters FromState(in GraphicsState state) =>
        new(state.StrokeOverprint, state.FillOverprint, state.OverprintMode, state.Halftone, state.HalftoneOrigin);
}
