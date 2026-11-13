using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters.Codecs;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>How the decoded components become output samples (ISO 32000-2 §7.4.8, Table 13).</summary>
internal enum JpegOutputKind
{
    /// <summary>One component, copied.</summary>
    Gray,

    /// <summary>Two, three or four components interleaved without a colour transform.</summary>
    Interleaved,

    /// <summary>Three components, YCbCr to RGB.</summary>
    YccToRgb,

    /// <summary>Four components, YCCK to CMYK.</summary>
    YcckToCmyk,
}

/// <summary>
/// The state of the one image a <see cref="JpegDecoder"/> is decoding: the frame header's parameters and geometry, the tables and
/// the current scan, the decode's progress (storage, truncation) and the reporter its deviations go through.
/// </summary>
/// <remarks>
/// ITU-T T.81 §B.2.2, Table B.2 (frame header: P, Y, X, Nf), §A.1.1 and §A.2 (component dimensions and MCUs), §B.2.4.4 (DRI);
/// Adobe Technical Note #5116 §18 (APP14). The frame parser (<see cref="JpegMarkerReader"/>), the entropy decoders
/// (<see cref="JpegScanDecoder"/>) and the output stage (<see cref="JpegSampleWriter"/>) share it; <see cref="Reset"/> readies it
/// for the next image, keeping the tables and component records for the thread.
/// <para>ISO 32000-2 §7.4.8.</para>
/// </remarks>
internal sealed class JpegFrame
{
    /// <summary>
    /// The most components in a frame: T.81 §B.2.2 allows 255, a PDF colour space has at most 32 (ISO 32000-2 Annex C, the image
    /// buffer's <see cref="ImageGeometry.MaxComponents"/>). More than four are coded in several scans.
    /// </summary>
    public const int MaxComponents = ImageGeometry.MaxComponents;

    /// <summary>Gets the reporter of this image's deviations, each code once.</summary>
    public CodecReporter Reporter { get; } = new();

    /// <summary>Gets the Huffman, quantization and arithmetic conditioning tables defined so far.</summary>
    public JpegTables Tables { get; } = new();

    /// <summary>Gets the current scan.</summary>
    public JpegScan Scan { get; } = new();

    /// <summary>Gets the component records; the first <see cref="ComponentCount"/> are the frame's.</summary>
    public JpegComponent[] Components { get; private set; } = [new(), new(), new(), new()];

    /// <summary>Gets or sets where the first scan header's length starts, after <see cref="JpegMarkerReader.ReadHeaders"/>.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets a value indicating whether a frame header was read.</summary>
    public bool FrameFound { get; set; }

    /// <summary>Gets or sets the number of components (Nf).</summary>
    public int ComponentCount { get; set; }

    /// <summary>Gets or sets the number of samples per row (X).</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the number of rows (Y, or the DNL value when Y is 0).</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the sample precision P: 8 or 12.</summary>
    public int Precision { get; set; } = 8;

    /// <summary>Gets or sets the largest horizontal sampling factor Hmax.</summary>
    public int HMax { get; set; }

    /// <summary>Gets or sets the largest vertical sampling factor Vmax.</summary>
    public int VMax { get; set; }

    /// <summary>Gets or sets the number of MCUs per row of an interleaved scan.</summary>
    public int McusX { get; set; }

    /// <summary>Gets or sets the number of MCU rows of an interleaved scan.</summary>
    public int McusY { get; set; }

    /// <summary>Gets or sets the restart interval Ri (0: none).</summary>
    public int RestartInterval { get; set; }

    /// <summary>Gets or sets a value indicating whether an Adobe APP14 segment was read.</summary>
    public bool Adobe { get; set; }

    /// <summary>Gets or sets the APP14 transform code.</summary>
    public AdobeColorTransform AdobeTransform { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame is progressive (SOF2, SOF10).</summary>
    public bool Progressive { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame is arithmetic-coded (SOF9, SOF10).</summary>
    public bool Arithmetic { get; set; }

    /// <summary>Gets or sets the reduction the image is decoded at, as a power of two: 0 to 3.</summary>
    public int Reduction { get; set; }

    /// <summary>Gets or sets the samples per side of a full-size component's IDCT: 8 &gt;&gt; <see cref="Reduction"/>.</summary>
    public int MinScaled { get; set; } = 8;

    /// <summary>Gets or sets how the components become output samples.</summary>
    public JpegOutputKind Output { get; set; }

    /// <summary>Gets or sets a value indicating whether one MCU row of coefficients is decoded and output at a time.</summary>
    public bool Streaming { get; set; }

    /// <summary>Gets or sets a value indicating whether the per-image buffers are rented.</summary>
    public bool StorageReady { get; set; }

    /// <summary>Gets or sets the number of scans decoded.</summary>
    public int ScansDecoded { get; set; }

    /// <summary>Gets or sets the first MCU row a missing block was in.</summary>
    public int FirstMissingRow { get; set; }

    /// <summary>Gets or sets a value indicating whether the truncation was reported.</summary>
    public bool Truncated { get; set; }

    /// <summary>Gets the width of the output rows: ⌈<see cref="Width"/> / 2^<see cref="Reduction"/>⌉.</summary>
    public int OutputWidth => ImageGeometry.Reduce(Width, Reduction);

    /// <summary>Gets the number of output rows: ⌈<see cref="Height"/> / 2^<see cref="Reduction"/>⌉.</summary>
    public int OutputHeight => ImageGeometry.Reduce(Height, Reduction);

    /// <summary>Gets the number of output rows decoded from data; the rows below are mid-grey.</summary>
    public int DecodedRows => (int)Math.Min(OutputHeight, (long)FirstMissingRow * MinScaled * VMax);

    /// <summary>Gets a value indicating whether every component decodes with the 1 x 1 IDCT, which reads the DC coefficient alone.</summary>
    public bool DcOnly
    {
        get
        {
            for (int c = 0; c < ComponentCount; c++)
            {
                if (Components[c].ScaledSize != 1)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Formats a message with the invariant culture.</summary>
    /// <param name="text">The message.</param>
    /// <returns>The formatted message.</returns>
    public static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns whether <paramref name="code"/> would be reported now.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <returns>Whether the code was not yet reported for this image and the reporter is not silent.</returns>
    public bool ShouldReport(DiagnosticCode code) => Reporter.ShouldReport(code);

    /// <summary>Reports a deviation, once per code per image.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="message">The message.</param>
    public void Report(DiagnosticCode code, DiagnosticSeverity severity, string message) => Reporter.Report(code, severity, message);

    /// <summary>Records that MCU row <paramref name="frameRow"/> lacks data and reports the truncation once.</summary>
    /// <param name="frameRow">The MCU row, or <see cref="int.MaxValue"/> when no row is lost.</param>
    /// <param name="message">The message of the first report, or <see langword="null"/> to report nothing.</param>
    public void MarkTruncated(int frameRow, string? message)
    {
        if (frameRow < FirstMissingRow)
        {
            FirstMissingRow = frameRow;
        }

        if (message is not null && !Truncated)
        {
            Truncated = true;
            Report(DctDiagnostics.DctTruncated, DiagnosticSeverity.Warning, message);
        }
    }

    /// <summary>Grows the component records to <paramref name="count"/> (a frame of more than four components; kept for the thread).</summary>
    /// <param name="count">The number of components.</param>
    public void EnsureComponents(int count)
    {
        if (count <= Components.Length)
        {
            return;
        }

        int previous = Components.Length;
        JpegComponent[] components = Components;
        Array.Resize(ref components, count);
        for (int c = previous; c < count; c++)
        {
            components[c] = new JpegComponent();
        }

        Components = components;
    }

    /// <summary>Releases the per-image buffers and forgets the image, keeping the tables and component records.</summary>
    public void Reset()
    {
        Tables.Reset();
        foreach (JpegComponent component in Components)
        {
            component.Release();
        }

        Reporter.Reset(null);
        Position = 0;
        FrameFound = false;
        ComponentCount = 0;
        RestartInterval = 0;
        Adobe = false;
        AdobeTransform = AdobeColorTransform.Unknown;
        Streaming = false;
        StorageReady = false;
        ScansDecoded = 0;
        FirstMissingRow = int.MaxValue;
        Truncated = false;
        Output = JpegOutputKind.Gray;
        Progressive = false;
        Arithmetic = false;
        Precision = 8;
        Reduction = 0;
        MinScaled = 8;
        Scan.Reset();
        Width = 0;
        Height = 0;
    }
}
