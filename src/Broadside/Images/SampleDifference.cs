namespace Broadside.Images;

/// <summary>How far apart two decodes of one image are: the largest difference of any component, the PSNR over all components, and the share of pixels whose largest component difference exceeds 2 levels.</summary>
/// <param name="Max">The largest absolute difference of any component, 0 to 255.</param>
/// <param name="Psnr">The peak signal-to-noise ratio in dB over all components; positive infinity when the decodes are equal.</param>
/// <param name="OverTwoLevels">The share of pixels, 0 to 1, with a component more than 2 levels apart.</param>
/// <remarks>The fidelity measures of the image decode research report, over 8-bit samples of equal size and component count.</remarks>
internal readonly record struct SampleDifference(int Max, double Psnr, double OverTwoLevels)
{
    /// <summary>Measures <paramref name="a"/> against <paramref name="b"/>, which have the same size, components and 8-bit storage.</summary>
    public static SampleDifference Measure(DecodedImage a, DecodedImage b)
    {
        int components = a.Components;
        int width = a.Width;
        int max = 0;
        long over = 0;
        double squares = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<byte> rowA = a.GetRow(y)[..(width * components)];
            ReadOnlySpan<byte> rowB = b.GetRow(y)[..(width * components)];
            for (int x = 0; x < rowA.Length; x += components)
            {
                int pixel = 0;
                for (int c = 0; c < components; c++)
                {
                    int d = Math.Abs(rowA[x + c] - rowB[x + c]);
                    squares += d * d;
                    pixel = Math.Max(pixel, d);
                }

                max = Math.Max(max, pixel);
                if (pixel > 2)
                {
                    over++;
                }
            }
        }

        long pixels = (long)width * a.Height;
        double mse = pixels == 0 ? 0 : squares / (pixels * components);
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
        return new SampleDifference(max, psnr, pixels == 0 ? 0 : over / (double)pixels);
    }
}
