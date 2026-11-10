using Broadside.Images;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// The platform-neutral half of the WIC image decoder: which reduction a size Windows Imaging Component offers delivers, the
/// YCbCr planes put back together as the managed decoder does (chroma replicated, the managed integer transform), and WIC's BGR rows
/// repacked as RGB. A plane or a row WIC delivers one sample short (a size rounded down) is completed by repeating its last sample.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8 (DCTDecode, Table 13) and §8.9.3 (sample layout); ITU-T T.81 A.1.1 (sampling factors). The managed decoder
/// replicates subsampled chroma (libjpeg's <c>-nosmooth</c>), so a chroma sample covers 2^shift luma samples in each direction.
/// </remarks>
internal static class WicSamples
{
    /// <summary>The largest chroma subsampling shift a JPEG has: 4:1:1 subsamples by four.</summary>
    private const int MaxChromaShift = 2;

    /// <summary>
    /// Returns the reduction, from 0 to <paramref name="requested"/>, whose size WIC offers as (<paramref name="offeredWidth"/>,
    /// <paramref name="offeredHeight"/>): ⌈W / 2^r⌉ as the managed decoder delivers, or ⌊W / 2^r⌋ (one sample short at most); -1
    /// when it is neither for any reduction allowed.
    /// </summary>
    /// <param name="width">The image's width.</param>
    /// <param name="height">The image's height.</param>
    /// <param name="requested">The coarsest reduction allowed.</param>
    /// <param name="offeredWidth">The width WIC offers.</param>
    /// <param name="offeredHeight">The height WIC offers.</param>
    /// <returns>The reduction delivered, or -1.</returns>
    public static int DeliveredReduction(int width, int height, int requested, long offeredWidth, long offeredHeight)
    {
        for (int r = requested; r >= 0; r--)
        {
            if (Matches(width, r, offeredWidth) && Matches(height, r, offeredHeight))
            {
                return r;
            }
        }

        return -1;

        static bool Matches(int size, int r, long offered) => offered == Reduce(size, r) || (offered == size >> r && offered > 0);
    }

    /// <summary>
    /// Returns the subsampling shift of a chroma plane <paramref name="chroma"/> samples long against a luma plane
    /// <paramref name="luma"/> samples long: s from 0 to 2 with ⌈luma / 2^s⌉ or ⌊luma / 2^s⌋ equal to <paramref name="chroma"/>;
    /// -1 for any other ratio.
    /// </summary>
    /// <param name="luma">The luma plane's width or height.</param>
    /// <param name="chroma">The chroma plane's width or height.</param>
    /// <returns>The shift, or -1.</returns>
    public static int ChromaShift(int luma, int chroma)
    {
        for (int s = 0; s <= MaxChromaShift; s++)
        {
            if (chroma > 0 && (chroma == Reduce(luma, s) || chroma == luma >> s))
            {
                return s;
            }
        }

        return -1;
    }

    /// <summary>
    /// Fills the RGB rows of <paramref name="image"/> from Y, Cb and Cr planes: each chroma sample replicated over 2^shift luma
    /// samples in each direction, then the managed decoder's YCbCr transform (<see cref="YCbCrConversion.ToRgb"/>).
    /// </summary>
    /// <param name="image">The three-component image to fill; its size may exceed the luma plane's by one sample in each direction.</param>
    /// <param name="y">The luma plane.</param>
    /// <param name="luma">The luma plane's size and stride.</param>
    /// <param name="cb">The blue-difference plane.</param>
    /// <param name="cr">The red-difference plane, with the same size and stride as <paramref name="cb"/>.</param>
    /// <param name="chroma">The chroma planes' size and stride.</param>
    /// <param name="scratch">At least 3 × the image's width bytes, for one row of each component.</param>
    /// <returns><see langword="false"/> when the planes do not fit the image (another subsampling than 1, 2 or 4).</returns>
    public static bool ConvertPlanes(DecodedImageBuilder image, ReadOnlySpan<byte> y, Plane luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Plane chroma, Span<byte> scratch)
    {
        int width = image.Width;
        int shiftX = ChromaShift(luma.Width, chroma.Width);
        int shiftY = ChromaShift(luma.Height, chroma.Height);
        if (image.Components != 3 || shiftX < 0 || shiftY < 0 || !luma.Covers(width, image.Height) || scratch.Length < 3 * width)
        {
            return false;
        }

        Span<byte> cbRow = scratch[..width];
        Span<byte> crRow = scratch.Slice(width, width);
        Span<byte> lumaRow = width == luma.Width ? default : scratch.Slice(2 * width, width);
        for (int row = 0; row < image.Height; row++)
        {
            int ly = Math.Min(row, luma.Height - 1);
            int cy = Math.Min(ly >> shiftY, chroma.Height - 1);
            ReadOnlySpan<byte> cbSource = cb.Slice(cy * chroma.Stride, chroma.Width);
            ReadOnlySpan<byte> crSource = cr.Slice(cy * chroma.Stride, chroma.Width);
            for (int x = 0; x < width; x++)
            {
                int cx = Math.Min(Math.Min(x, luma.Width - 1) >> shiftX, chroma.Width - 1);
                cbRow[x] = cbSource[cx];
                crRow[x] = crSource[cx];
            }

            ReadOnlySpan<byte> lumaSource = y.Slice(ly * luma.Stride, luma.Width);
            if (!lumaRow.IsEmpty)
            {
                lumaSource.CopyTo(lumaRow);
                lumaRow[luma.Width..].Fill(lumaSource[^1]);
                lumaSource = lumaRow;
            }

            YCbCrConversion.ToRgb(lumaSource, cbRow, crRow, image.GetRow(row)[..(width * 3)]);
        }

        return true;
    }

    /// <summary>
    /// Fills the rows of <paramref name="image"/> from WIC's rows: 24bppBGR repacked as RGB for three components, 8bppGray copied for
    /// one; a source one sample short in either direction is completed by repeating its last sample.
    /// </summary>
    /// <param name="image">The image to fill, of 1 or 3 components.</param>
    /// <param name="pixels">WIC's pixels, top row first.</param>
    /// <param name="source">Their size and stride.</param>
    /// <returns><see langword="false"/> when the pixels do not fit the image.</returns>
    public static bool CopyPixels(DecodedImageBuilder image, ReadOnlySpan<byte> pixels, Plane source)
    {
        int components = image.Components;
        int width = image.Width;
        if (components is not (1 or 3) || !source.Covers(width, image.Height))
        {
            return false;
        }

        for (int row = 0; row < image.Height; row++)
        {
            ReadOnlySpan<byte> from = pixels.Slice(Math.Min(row, source.Height - 1) * source.Stride, source.Width * components);
            Span<byte> to = image.GetRow(row)[..(width * components)];
            if (components == 1)
            {
                from.CopyTo(to);
            }
            else
            {
                for (int i = 0; i < from.Length; i += 3)
                {
                    to[i] = from[i + 2];
                    to[i + 1] = from[i + 1];
                    to[i + 2] = from[i];
                }
            }

            ReadOnlySpan<byte> last = to.Slice((source.Width - 1) * components, components);
            for (int x = source.Width; x < width; x++)
            {
                last.CopyTo(to.Slice(x * components, components));
            }
        }

        return true;
    }

    /// <summary>Returns ⌈<paramref name="size"/> / 2^<paramref name="reduction"/>⌉, the size of a reduction as the managed decoder delivers it.</summary>
    public static int Reduce(int size, int reduction) => (int)(((long)size + (1L << reduction) - 1) >> reduction);

    /// <summary>The size and stride of a plane of 8-bit samples (or of interleaved pixels).</summary>
    /// <param name="Width">The samples (pixels) per row.</param>
    /// <param name="Height">The rows.</param>
    /// <param name="Stride">The bytes from one row to the next.</param>
    public readonly record struct Plane(int Width, int Height, int Stride)
    {
        /// <summary>Returns whether the plane is the image's size, or one sample short of it in either direction.</summary>
        public bool Covers(int width, int height) =>
            Width > 0 && Height > 0 && Width <= width && Height <= height && width - Width <= 1 && height - Height <= 1;
    }
}
