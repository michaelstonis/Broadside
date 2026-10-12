using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// The graphics state: the parameters every painting operator consults, as the content interpreter holds them. A processor reads
/// it through <see cref="Content.ContentContext.State"/>, by reference and read-only; it may copy it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.4.1, Tables 51 and 52, with the text state parameters of §9.3 (Table 102). The initial values are those of
/// Tables 51, 52 and 102, except the CTM, which starts at the identity: a run reports geometry in default user space and the
/// consumer maps it to its device (§8.3.2.3). <c>q</c> pushes a copy of the whole state and <c>Q</c> pops it (§8.4.2); the current
/// path and the text matrices are not part of it (§8.5.2.1, §9.4.1).
/// </para>
/// <para>
/// The text state (§9.3) is set by its operators and the parameters of Table 57 through <c>gs</c> (§8.4.5). Parameters whose
/// values are document-model objects (font, soft mask, transfer, black generation, undercolour removal) are live views; a halftone
/// is kept as written. The colours (§8.6) are <see cref="PdfColor"/> values. Numeric parameters are always within their valid ranges:
/// the interpreter clips out-of-range operands and records a diagnostic (§8.4.1).
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "A snapshot of interpreter state, never compared; value equality over every parameter would mislead.")]
public struct GraphicsState
{
    private DashBuffer _dash;
    private double[]? _dashOverflow;
    private int _dashCount;
    private PdfColor _strokeColor;
    private PdfColor _fillColor;

    /// <summary>Gets the current transformation matrix: user space to the run's default user space.</summary>
    /// <remarks>ISO 32000-2 §8.3.2.3, Table 51. Changed by <c>cm</c> (Table 56).</remarks>
    public Matrix Ctm { readonly get; internal set; }

    /// <summary>
    /// Gets the current clipping path as a handle that <see cref="Content.ContentContext.GetClip"/> resolves; 0 is the run's initial
    /// clipping path (for a page, its media box). Meaningful only to a processor that asks for <see cref="Content.ContentEvents.Clips"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.5.4, Table 51. Restored by <c>Q</c> with the rest of the state.</remarks>
    public int ClipHandle { readonly get; internal set; }

    /// <summary>Gets the line width in user space units; 0 is the thinnest line the device can render. Initially 1.0.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.2, Table 51.</remarks>
    public double LineWidth { readonly get; internal set; }

    /// <summary>Gets the line cap style. Initially <see cref="LineCap.Butt"/>.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.3, Table 51.</remarks>
    public LineCap LineCap { readonly get; internal set; }

    /// <summary>Gets the line join style. Initially <see cref="LineJoin.Miter"/>.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.4, Table 51.</remarks>
    public LineJoin LineJoin { readonly get; internal set; }

    /// <summary>Gets the miter limit, at least 1. Initially 10.0.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.5, Table 51.</remarks>
    public double MiterLimit { readonly get; internal set; }

    /// <summary>Gets the lengths of the alternating dashes and gaps, in user space units; empty for a solid line. Initially empty.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.6, Table 51. Every element is non-negative and, when there are any, not all are zero.</remarks>
    [UnscopedRef]
    public readonly ReadOnlySpan<double> DashArray => _dashOverflow is { } overflow ? overflow : ((ReadOnlySpan<double>)_dash)[.._dashCount];

    /// <summary>Gets the distance into the dash pattern at which a stroke starts, never negative; 0 for a solid line. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.6: a negative phase is incremented by twice the sum of the dash array until it is not negative.</remarks>
    public double DashPhase { readonly get; internal set; }

    /// <summary>Gets the colour used for stroking: its colour space and values. Initially DeviceGray black (0).</summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.8, Tables 51 and 73. Set by <c>CS SC SCN G RG K</c>. The space is the one the operator selected; a default
    /// colour space replaces a device space when painting (§8.6.5.6, <see cref="Content.ContentContext.DefaultColorSpaces"/>).
    /// </remarks>
    [UnscopedRef]
    public readonly ref readonly PdfColor StrokeColor => ref _strokeColor;

    /// <summary>Gets the colour used for all other painting: its colour space and values. Initially DeviceGray black (0).</summary>
    /// <remarks>ISO 32000-2 §8.6.8, Tables 51 and 73. Set by <c>cs sc scn g rg k</c>; see <see cref="StrokeColor"/>.</remarks>
    [UnscopedRef]
    public readonly ref readonly PdfColor FillColor => ref _fillColor;

    /// <summary>Gets whether colour conversions compensate for black points. Initially <see cref="BlackPointCompensation.Default"/>.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.9, Table 52. Set through <c>gs</c> (UseBlackPtComp, PDF 2.0).</remarks>
    public BlackPointCompensation BlackPointCompensation { readonly get; internal set; }

    /// <summary>Gets the colour rendering intent. Initially <see cref="RenderingIntent.RelativeColorimetric"/>.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.8, Table 52. Set by <c>ri</c> (Table 56).</remarks>
    public RenderingIntent RenderingIntent { readonly get; internal set; }

    /// <summary>Gets the flatness tolerance, 0 to 100; 0 selects the device's default. Initially 1.0.</summary>
    /// <remarks>ISO 32000-2 §10.7.2, Table 52. Set by <c>i</c> (Table 56).</remarks>
    public double Flatness { readonly get; internal set; }

    /// <summary>Gets a value indicating whether automatic stroke adjustment is on. Initially false.</summary>
    /// <remarks>ISO 32000-2 §10.7.5, Table 52. Set through <c>gs</c> (SA).</remarks>
    public bool StrokeAdjustment { readonly get; internal set; }

    /// <summary>Gets the blend mode. Initially <see cref="BlendMode.Normal"/>.</summary>
    /// <remarks>ISO 32000-2 §11.3.5, Table 52. Set through <c>gs</c> (BM).</remarks>
    public BlendMode BlendMode { readonly get; internal set; }

    /// <summary>Gets the constant opacity for stroking, 0 to 1. Initially 1.0.</summary>
    /// <remarks>ISO 32000-2 §11.6.4.4, Table 52. Set through <c>gs</c> (CA).</remarks>
    public double StrokeAlpha { readonly get; internal set; }

    /// <summary>Gets the constant opacity for everything else, 0 to 1. Initially 1.0.</summary>
    /// <remarks>ISO 32000-2 §11.6.4.4, Table 52. Set through <c>gs</c> (ca).</remarks>
    public double FillAlpha { readonly get; internal set; }

    /// <summary>Gets a value indicating whether the soft mask and alpha are shape values rather than opacity. Initially false.</summary>
    /// <remarks>ISO 32000-2 §11.6.4.3, Table 52. Set through <c>gs</c> (AIS).</remarks>
    public bool AlphaIsShape { readonly get; internal set; }

    /// <summary>Gets a value indicating whether text is a knockout group. Initially true.</summary>
    /// <remarks>ISO 32000-2 §9.3.8, Table 102. Set through <c>gs</c> (TK).</remarks>
    public bool TextKnockout { readonly get; internal set; }

    /// <summary>Gets a value indicating whether stroking overprints. Initially false.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52. Set through <c>gs</c> (OP).</remarks>
    public bool StrokeOverprint { readonly get; internal set; }

    /// <summary>Gets a value indicating whether other painting overprints. Initially false.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52. Set through <c>gs</c> (op, else OP).</remarks>
    public bool FillOverprint { readonly get; internal set; }

    /// <summary>Gets the overprint mode, 0 or 1. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §8.6.7, Table 52. Set through <c>gs</c> (OPM).</remarks>
    public int OverprintMode { readonly get; internal set; }

    /// <summary>
    /// Gets the soft mask: where it comes from and how it gives shape or opacity values; <see langword="null"/> for none. Initially none.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.5.1, Tables 52 and 142. Set through <c>gs</c> (SMask, PDF 1.4).</remarks>
    public PdfSoftMask? SoftMask { readonly get; internal set; }

    /// <summary>
    /// Gets the CTM at the <c>gs</c> that set <see cref="SoftMask"/>: the mask's group is painted in that coordinate system, not the
    /// one current when something is painted through it. The identity when there is no soft mask.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.5.1 (Table 142, <c>G</c>): the group's matrix is concatenated to the CTM in effect when the mask was set.</remarks>
    public Matrix SoftMaskMatrix { readonly get; internal set; }

    /// <summary>Gets the black-generation function, or <see langword="null"/> for the device's default. Initially the default.</summary>
    /// <remarks>ISO 32000-2 §10.4.2.4, Table 52. Set through <c>gs</c> (BG, BG2, PDF 1.3); pass it to <see cref="ColorConversion"/>.</remarks>
    public PdfFunction? BlackGeneration { readonly get; internal set; }

    /// <summary>Gets the undercolour-removal function, or <see langword="null"/> for the device's default. Initially the default.</summary>
    /// <remarks>ISO 32000-2 §10.4.2.4, Table 52. Set through <c>gs</c> (UCR, UCR2, PDF 1.3); pass it to <see cref="ColorConversion"/>.</remarks>
    public PdfFunction? UndercolorRemoval { readonly get; internal set; }

    /// <summary>Gets the transfer function, or <see langword="null"/> for the device's default. Initially the default.</summary>
    /// <remarks>ISO 32000-2 §10.5, Table 52. Set through <c>gs</c> (TR, TR2; deprecated in PDF 2.0).</remarks>
    public PdfTransferFunction? TransferFunction { readonly get; internal set; }

    /// <summary>
    /// Gets the halftone as written (a halftone dictionary or stream), or <see langword="null"/> for the device's default. Initially
    /// the default. Halftones are a device concern and are not interpreted.
    /// </summary>
    /// <remarks>ISO 32000-2 §10.6, Table 52. Set through <c>gs</c> (HT).</remarks>
    public CosObject? Halftone { readonly get; internal set; }

    /// <summary>Gets the halftone origin in device space, or <see langword="null"/> when not set. Initially not set.</summary>
    /// <remarks>ISO 32000-2 §10.6.5, Table 57. Set through <c>gs</c> (HTO, PDF 2.0).</remarks>
    public PathPoint? HalftoneOrigin { readonly get; internal set; }

    /// <summary>Gets the smoothness tolerance for shadings, 0 to 1. Initially 0 (the device's default).</summary>
    /// <remarks>ISO 32000-2 §10.7.3, Table 52. Set through <c>gs</c> (SM, PDF 1.3).</remarks>
    public double Smoothness { readonly get; internal set; }

    /// <summary>Gets the text font: the font <c>Tf</c> selected, or the <c>Font</c> entry of a graphics state parameter dictionary; <see langword="null"/> until one is selected.</summary>
    /// <remarks>ISO 32000-2 §9.3.1, Table 102 (T<sub>f</sub>, no initial value).</remarks>
    public PdfFont? Font { readonly get; internal set; }

    /// <summary>Gets the character spacing T<sub>c</sub>, in unscaled text space units. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §9.3.2, Table 102. Set by <c>Tc</c>.</remarks>
    public double CharacterSpacing { readonly get; internal set; }

    /// <summary>Gets the word spacing T<sub>w</sub>, in unscaled text space units. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §9.3.3, Table 102. Set by <c>Tw</c>.</remarks>
    public double WordSpacing { readonly get; internal set; }

    /// <summary>Gets the horizontal scaling T<sub>h</sub> as a factor (the <c>Tz</c> operand divided by 100). Initially 1.0.</summary>
    /// <remarks>ISO 32000-2 §9.3.4, Table 102.</remarks>
    public double HorizontalScaling { readonly get; internal set; }

    /// <summary>Gets the leading T<sub>l</sub>, in unscaled text space units. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §9.3.5, Table 102. Set by <c>TL</c> and <c>TD</c>.</remarks>
    public double Leading { readonly get; internal set; }

    /// <summary>Gets the text font size T<sub>fs</sub>; 0 until <c>Tf</c> sets a font.</summary>
    /// <remarks>ISO 32000-2 §9.3.1, Table 102.</remarks>
    public double FontSize { readonly get; internal set; }

    /// <summary>Gets the text rendering mode T<sub>mode</sub>. Initially <see cref="TextRenderingMode.Fill"/>.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 102. Set by <c>Tr</c>.</remarks>
    public TextRenderingMode TextRenderingMode { readonly get; internal set; }

    /// <summary>Gets the text rise T<sub>rise</sub>, in unscaled text space units. Initially 0.</summary>
    /// <remarks>ISO 32000-2 §9.3.7, Table 102. Set by <c>Ts</c>.</remarks>
    public double TextRise { readonly get; internal set; }

    /// <summary>Returns the state a content stream starts with (Tables 51, 52 and 102), with the CTM at the identity.</summary>
    internal static GraphicsState CreateInitial() => new()
    {
        Ctm = Matrix.Identity,
        SoftMaskMatrix = Matrix.Identity,
        LineWidth = 1.0,
        MiterLimit = 10.0,
        Flatness = 1.0,
        StrokeAlpha = 1.0,
        FillAlpha = 1.0,
        TextKnockout = true,
        HorizontalScaling = 1.0,
        _strokeColor = PdfDeviceGrayColorSpace.Instance.GetInitialColor(),
        _fillColor = PdfDeviceGrayColorSpace.Instance.GetInitialColor(),
    };

    /// <summary>Sets the stroking or the non-stroking colour.</summary>
    /// <param name="stroke">Whether the stroking colour is set.</param>
    /// <param name="color">The colour.</param>
    internal void SetColor(bool stroke, in PdfColor color)
    {
        if (stroke)
        {
            _strokeColor = color;
        }
        else
        {
            _fillColor = color;
        }
    }

    /// <summary>Sets the dash pattern; the elements are copied. A pattern longer than the inline storage gets its own array.</summary>
    internal void SetDash(ReadOnlySpan<double> array, double phase)
    {
        if (array.Length <= DashBuffer.Length)
        {
            array.CopyTo(_dash);
            _dashOverflow = null;
        }
        else if (_dashOverflow is null || !array.SequenceEqual(_dashOverflow))
        {
            _dashOverflow = array.ToArray();
        }

        _dashCount = array.Length;
        DashPhase = phase;
    }

    /// <summary>Storage for dash arrays of up to eight elements, the common case, so that <c>q</c> copies no heap object.</summary>
    [InlineArray(Length)]
    private struct DashBuffer
    {
        public const int Length = 8;

        private double _element;
    }
}
