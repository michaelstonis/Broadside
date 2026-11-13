using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// Pages for the undecodable soft mask tests of every backend (issue #515): a 256 x 256 JPEG (kept encoded, so it reduces when drawn
/// small) painted into a 512-point square, with a soft-mask image (ISO 32000-2 §11.6.5.2) whose header reads, so the display list
/// keeps it encoded too, but whose data cannot be decoded when it is drawn. Every backend paints such an image unmasked, as when the
/// soft mask failed when the list was built, and the codec's reason is a diagnostic of the document.
/// </summary>
/// <remarks>
/// <c>jpx-*</c> soft masks are JPEG 2000 codestreams with a valid SIZ (one 8-bit component) and COD (five decomposition levels) and
/// zeros where the QCD marker segment and the tile-parts belong: the header the list reads is whole, and the decode finds no QCD.
/// <c>dct-*</c> soft masks are gray baseline JPEGs whose one quantization table is stored under destination 2 while the frame uses
/// destination 0: the header (up to the first scan) reads, and the decode finds no table. <c>*-same-size</c> soft masks have the
/// image's size, which the folding backends fold into its pixels; <c>*-own-size</c> ones (200 x 150) are drawn on their own grid.
/// </remarks>
public static class UndecodableSoftMaskPages
{
    /// <summary>The content stream: the image over a 512-point square, so that a quarter scale draws it 128 pixels square.</summary>
    public const string Paint = "q 512 0 0 512 50 100 cm /Im0 Do Q";

    /// <summary>The image's width and height in samples.</summary>
    public const int ImageSize = 256;

    /// <summary>Gets the names <see cref="Page"/> knows.</summary>
    public static IReadOnlyList<string> Names { get; } = ["jpx-same-size", "jpx-own-size", "dct-same-size", "dct-own-size"];

    /// <summary>Returns the page called <paramref name="name"/> (one of <see cref="Names"/>), with its soft mask or without.</summary>
    /// <param name="name">The page.</param>
    /// <param name="masked">
    /// Whether the image's dictionary names the soft mask (<c>/SMask 6 0 R</c>); without it the same image is painted unmasked, which
    /// is what a render of the masked page must give.
    /// </param>
    /// <returns>The file.</returns>
    public static byte[] Page(string name, bool masked = true)
    {
        (int width, int height) = name.EndsWith("-own-size", StringComparison.Ordinal) ? (200, 150) : (ImageSize, ImageSize);
        string soft = name.StartsWith("jpx-", StringComparison.Ordinal)
            ? MaskStream(Jpx(width, height), "/JPXDecode", width, height)
            : name.StartsWith("dct-", StringComparison.Ordinal)
                ? MaskStream(Dct(width, height), "/DCTDecode", width, height)
                : throw new ArgumentOutOfRangeException(nameof(name), name, null);
        string image = ContentSamples.Stream(
            Encoding.Latin1.GetString(CatalogueSamples.Jpeg(ImageSize, ImageSize)),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Filter /DCTDecode /Width {ImageSize} /Height {ImageSize} /ColorSpace /DeviceRGB /BitsPerComponent 8{(masked ? " /SMask 6 0 R" : string.Empty)}"));
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes(Paint), "/XObject << /Im0 5 0 R >>", image, soft);
    }

    /// <summary>
    /// Returns a page whose 256 x 256 JPEG image (no soft mask) has the corrupt quantization table of the <c>dct-*</c> soft masks: its
    /// header reads and its data cannot be decoded, so every backend paints nothing (ISO 32000-2 §8.9.5).
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] UndecodableImagePage()
    {
        byte[] jpeg = CatalogueSamples.Jpeg(ImageSize, ImageSize).ToArray();
        MoveQuantizationTable(jpeg);
        string image = ContentSamples.Stream(
            Encoding.Latin1.GetString(jpeg),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Filter /DCTDecode /Width {ImageSize} /Height {ImageSize} /ColorSpace /DeviceRGB /BitsPerComponent 8"));
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes(Paint), "/XObject << /Im0 5 0 R >>", image);
    }

    /// <summary>Returns the code of the diagnostic the soft mask of page <paramref name="name"/> records when it is first drawn.</summary>
    /// <param name="name">The page.</param>
    /// <returns><c>JpxMarkerSegmentInvalid</c> (no QCD) or <c>DctTableMissing</c> (no quantization table for the frame).</returns>
    public static string DiagnosticCode(string name) =>
        name.StartsWith("jpx-", StringComparison.Ordinal) ? "JpxMarkerSegmentInvalid" : "DctTableMissing";

    /// <summary>
    /// A JPEG 2000 codestream (ITU-T T.800 Annex A) of one 8-bit gray component, <paramref name="width"/> × <paramref name="height"/>,
    /// one tile, five decomposition levels, whose QCD marker segment and tile-parts are zeros.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The codestream.</returns>
    public static byte[] Jpx(int width, int height)
    {
        var data = new List<byte> { 0xFF, 0x4F }; // SOC
        data.AddRange([0xFF, 0x51, 0x00, 0x29, 0x00, 0x00]); // SIZ, Lsiz 41, Rsiz 0
        foreach (int value in new[] { width, height, 0, 0, width, height, 0, 0 })
        {
            // Xsiz, Ysiz, XOsiz, YOsiz, XTsiz, YTsiz, XTOsiz, YTOsiz.
            data.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        data.AddRange([0x00, 0x01, 0x07, 0x01, 0x01]); // Csiz 1; Ssiz 8-bit unsigned, XRsiz 1, YRsiz 1
        data.AddRange([0xFF, 0x52, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x01, 0x00, 0x05, 0x04, 0x04, 0x00, 0x01]); // COD: LRCP, 1 layer, NL 5, 64 x 64 blocks, 5/3
        data.AddRange(new byte[64]); // Where QCD and the first tile-part belong.
        return [.. data];
    }

    /// <summary>A gray baseline JPEG, <paramref name="width"/> × <paramref name="height"/>, whose one quantization table is stored under destination 2.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The JPEG.</returns>
    public static byte[] Dct(int width, int height)
    {
        byte[] gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                gray[(y * width) + x] = (byte)((x * 255 / Math.Max(1, width - 1) + (y * 255 / Math.Max(1, height - 1))) / 2);
            }
        }

        var output = new ArrayBufferWriter<byte>();
        var context = new StreamEncoderContext
        {
            StreamDictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) },
        };
        new DctEncoder(90).Encode(gray, output, context);
        byte[] jpeg = output.WrittenSpan.ToArray();
        MoveQuantizationTable(jpeg);
        return jpeg;
    }

    /// <summary>
    /// Stores each quantization table of a JPEG two destinations higher (0 and 1 become 2 and 3; ITU-T T.81 B.2.4.1: Tq is the low
    /// nibble of the byte before each table), so that the frame's components name tables that were never defined.
    /// </summary>
    private static void MoveQuantizationTable(byte[] jpeg)
    {
        int position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF && jpeg[position + 1] != 0xDA)
        {
            int length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            if (jpeg[position + 1] == 0xDB)
            {
                // Each table: Pq/Tq, then 64 values of 1 or 2 bytes.
                for (int table = position + 4; table < position + 2 + length; table += 1 + (64 << (jpeg[table] >> 4)))
                {
                    jpeg[table] = (byte)(jpeg[table] + 2);
                }
            }

            position += 2 + length;
        }
    }

    private static string MaskStream(byte[] data, string filter, int width, int height) =>
        ContentSamples.Stream(
            Encoding.Latin1.GetString(data),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Filter {filter} /Width {width} /Height {height} /ColorSpace /DeviceGray /BitsPerComponent 8"));
}
