namespace Broadside.Filters.Dct;

/// <summary>What a scan codes (ITU-T T.81 §G.1.1.1: spectral selection Ss..Se, successive approximation Ah, Al).</summary>
internal enum JpegScanKind
{
    /// <summary>A sequential scan: every coefficient of each block, at full precision.</summary>
    Sequential,

    /// <summary>A progressive DC scan with Ah = 0: the DC coefficients, shifted left by Al.</summary>
    DcFirst,

    /// <summary>A progressive DC scan with Ah != 0: one more bit of each DC coefficient.</summary>
    DcRefine,

    /// <summary>A progressive AC scan with Ah = 0: the band Ss..Se of one component, shifted left by Al.</summary>
    AcFirst,

    /// <summary>A progressive AC scan with Ah != 0: one more bit of every coefficient in the band.</summary>
    AcRefine,
}

/// <summary>
/// The scan being decoded: its components and parameters from the scan header, and the entropy decoders' state that every restart
/// interval starts afresh (EOBRUN, the arithmetic statistics).
/// </summary>
/// <remarks>
/// ITU-T T.81 §B.2.3, Table B.3 (Ns, Csj, Ss, Se, Ah, Al); §G.1.2.2 (EOBRUN); §F.1.4.4 and §G.1.3 (statistics areas: Table F.4,
/// 64 DC bins, and Table F.5, 256 AC bins, per conditioning table).
/// </remarks>
internal sealed class JpegScan
{
    /// <summary>The most components in one scan (ITU-T T.81 §B.2.3, Table B.3: Ns 1 to 4).</summary>
    public const int MaxComponents = 4;

    /// <summary>The statistics bins of one arithmetic DC conditioning table.</summary>
    public const int DcStatisticsBins = 64;

    /// <summary>The statistics bins of one arithmetic AC conditioning table.</summary>
    public const int AcStatisticsBins = 256;

    /// <summary>The fixed-probability bin of sign and refinement decisions (Qe X'5A1D', never adapted); passed by reference.</summary>
    public byte FixedBin = ArithmeticDecoder.FixedState;

    /// <summary>Gets, for each scan component, the index of its frame component.</summary>
    public int[] Components { get; } = new int[MaxComponents];

    /// <summary>Gets the arithmetic DC statistics, <see cref="DcStatisticsBins"/> per conditioning table.</summary>
    public byte[] DcStatistics { get; } = new byte[4 * DcStatisticsBins];

    /// <summary>Gets the arithmetic AC statistics, <see cref="AcStatisticsBins"/> per conditioning table.</summary>
    public byte[] AcStatistics { get; } = new byte[4 * AcStatisticsBins];

    /// <summary>Gets or sets the number of components in the scan (Ns).</summary>
    public int Length { get; set; }

    /// <summary>Gets or sets what the scan codes.</summary>
    public JpegScanKind Kind { get; set; }

    /// <summary>Gets or sets the start of spectral selection Ss.</summary>
    public int SpectralStart { get; set; }

    /// <summary>Gets or sets the end of spectral selection Se.</summary>
    public int SpectralEnd { get; set; } = 63;

    /// <summary>Gets or sets the successive approximation bit position low Al.</summary>
    public int ApproximationLow { get; set; }

    /// <summary>Gets or sets the number of blocks left in the current end-of-band run.</summary>
    public int EobRun { get; set; }

    /// <summary>Gets a value indicating whether the scan codes DC coefficients from scratch.</summary>
    public bool CodesDc => Kind is JpegScanKind.Sequential or JpegScanKind.DcFirst;

    /// <summary>Gets a value indicating whether the scan codes AC coefficients.</summary>
    public bool CodesAc => Kind is JpegScanKind.Sequential or JpegScanKind.AcFirst or JpegScanKind.AcRefine;

    /// <summary>Resets the state every scan and restart interval starts with: DC predictors, EOBRUN, arithmetic statistics.</summary>
    /// <param name="components">The frame's component records.</param>
    /// <param name="arithmetic">Whether the frame is arithmetic-coded.</param>
    /// <remarks>ITU-T T.81 §E.2.4 (Reset_decoder), §G.1.2.2 (EOBRUN), §F.1.4.4 and §G.1.3.1 (statistics areas and DC context set to zero).</remarks>
    public void ResetEntropyState(JpegComponent[] components, bool arithmetic)
    {
        EobRun = 0;
        bool codesDc = CodesDc;
        bool codesAc = CodesAc;
        for (int j = 0; j < Length; j++)
        {
            JpegComponent component = components[Components[j]];
            component.Predictor = 0;
            component.DcContext = 0;
            if (!arithmetic)
            {
                continue;
            }

            if (codesDc)
            {
                DcStatistics.AsSpan(component.DcConditioning * DcStatisticsBins, DcStatisticsBins).Clear();
            }

            if (codesAc)
            {
                AcStatistics.AsSpan(component.AcConditioning * AcStatisticsBins, AcStatisticsBins).Clear();
            }
        }
    }

    /// <summary>Forgets the scan for the next image.</summary>
    public void Reset()
    {
        Length = 0;
        Kind = JpegScanKind.Sequential;
        SpectralStart = 0;
        SpectralEnd = 63;
        ApproximationLow = 0;
        EobRun = 0;
        FixedBin = ArithmeticDecoder.FixedState;
    }
}
