using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>The files of <c>Images/Import</c> (made by <c>make_import_images.py</c>) and the checks the import tests share.</summary>
internal static class ImportTesting
{
    /// <summary>The directory of the files.</summary>
    public static string Directory { get; } = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Images", "Import");

    /// <summary>The bytes of a file.</summary>
    public static byte[] File(string name) => System.IO.File.ReadAllBytes(Path.Combine(Directory, name));

    /// <summary>The image's raw samples, one value per component, with a codec's inversion undone (0 is black for gray, as the color space reads them).</summary>
    public static ushort[] Samples(PdfImage image)
    {
        using DecodedImage decoded = image.Decode() ?? throw new InvalidOperationException("The image does not decode.");
        Assert.Equal(image.Width, decoded.Geometry.Width);
        Assert.Equal(image.Height, decoded.DecodedRows);
        ushort[] values = ImageTesting.Raw(decoded);
        if (decoded.SamplesInverted)
        {
            int max = (1 << decoded.Geometry.BitsPerComponent) - 1;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)(max - values[i]);
            }
        }

        return values;
    }

    /// <summary>The expected samples of a <paramref name="width"/> x <paramref name="height"/> image of <paramref name="components"/> components.</summary>
    public static ushort[] Expected(int width, int height, int components, Func<int, int, int, int> value)
    {
        ushort[] values = new ushort[width * height * components];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    values[(((y * width) + x) * components) + c] = (ushort)value(x, y, c);
                }
            }
        }

        return values;
    }

    /// <summary>The bilevel pattern as DeviceGray 1-bit samples: 0 black, 1 white.</summary>
    public static ushort[] Bilevel(int width, int height) => Expected(width, height, 1, (x, y, _) => ImportSamples.Black(x, y) ? 0 : 1);

    /// <summary>The 8-bit RGB pattern.</summary>
    public static ushort[] Rgb8(int width, int height) => Expected(width, height, 3, (x, y, c) => Component(ImportSamples.Rgb8(x, y), c));

    /// <summary>The smooth RGB pattern of the JPEG files.</summary>
    public static ushort[] Smooth(int width, int height) => Expected(width, height, 3, (x, y, c) => Component(ImportSamples.Smooth(x, y), c));

    /// <summary>Component <paramref name="c"/> of a triple.</summary>
    public static int Component((int, int, int) value, int c) => c switch
    {
        0 => value.Item1,
        1 => value.Item2,
        _ => value.Item3,
    };

    /// <summary>The image's filter name.</summary>
    public static string Filter(PdfImage image) => ((CosName)image.Stream!.Dictionary[new CosName("Filter")]).Value;

    /// <summary>An entry of the image's DecodeParms, or null.</summary>
    public static CosObject? Parameter(PdfImage image, string key) =>
        image.Stream!.Dictionary.TryGetValue(new CosName("DecodeParms"), out CosObject? value) && value is CosDictionary parameters
        && parameters.TryGetValue(new CosName(key), out CosObject? entry) ? entry : null;

    /// <summary>Asserts that two sample arrays differ by at most <paramref name="tolerance"/> anywhere (lossy JPEG).</summary>
    public static void Near(ushort[] expected, ushort[] actual, int tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        int worst = 0;
        long total = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int difference = Math.Abs(expected[i] - actual[i]);
            worst = Math.Max(worst, difference);
            total += difference;
        }

        Assert.True(total / (double)expected.Length < tolerance / 4.0 && worst <= tolerance, $"mean difference {total / (double)expected.Length:F2}, worst {worst}");
    }

    /// <summary>The palette of an Indexed image as RGB bytes.</summary>
    public static byte[] Lookup(PdfImage image) => ((PdfIndexedColorSpace)image.ColorSpace!).GetLookup().ToArray();
}
