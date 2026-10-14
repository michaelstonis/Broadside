using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Converting image rows through an ICC profile is a hot path and allocates nothing per row once the converter exists (CLAUDE.md
/// "Code conventions"; ISO 32000-2 §8.9.5.2 through §8.6.5.5). The build-breaking half; <c>IccConversionBenchmarks</c> measures.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class IccAllocationTests
{
    [Fact]
    public void Image_rows_convert_through_matrix_TRC_profiles_without_allocating()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("icc-rgb-matrix.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace rgb = IccTesting.Space(document, "CS0");
        PdfColorSpace gray = IccTesting.Space(document, "CS2");
        PdfColorConverter[] converters =
        [
            document.GetColorConverter(rgb),
            document.GetColorConverter(gray),
            document.GetColorConverter(rgb, new ColorConversion { Target = DeviceColorModel.Cmyk }),
            document.GetColorConverter(rgb, new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric }),
        ];
        byte[] row = new byte[4096 * 3];
        byte[] pixels = new byte[4096 * 4];
        float[] components = new float[256 * 3];
        float[] colors = new float[256 * 4];
        for (int i = 0; i < row.Length; i++)
        {
            row[i] = (byte)(i * 7);
        }

        long allocated = Allocations.Measure(Run);

        Assert.Equal(0, allocated);

        void Run()
        {
            foreach (PdfColorConverter converter in converters)
            {
                converter.Convert(row, pixels, 4096 / converter.InputCount);
                converter.Convert(components, colors, 256 / converter.InputCount);
            }
        }
    }

    [Fact]
    public void Image_rows_convert_through_lookup_tables_without_allocating()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("icc-lut.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorConverter[] converters =
        [
            .. new[] { "CS0", "CS1", "CS2", "CS3", "CS4" }.Select(resource => document.GetColorConverter(IccTesting.Space(document, resource))),
            document.GetColorConverter(IccTesting.Space(document, "CS1"), new ColorConversion { Target = DeviceColorModel.Cmyk }),
            document.GetColorConverter(IccTesting.Space(document, "CS1"), new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric }),
        ];
        byte[] row = new byte[4096 * 4];
        byte[] pixels = new byte[4096 * 4];
        float[] components = new float[256 * 4];
        float[] colors = new float[256 * 4];
        for (int i = 0; i < row.Length; i++)
        {
            row[i] = (byte)(i * 7);
            components[i % components.Length] = (i % 13) / 12f;
        }

        long allocated = Allocations.Measure(Run);

        Assert.Equal(0, allocated);

        void Run()
        {
            foreach (PdfColorConverter converter in converters)
            {
                converter.Convert(row, pixels, 4096);
                converter.Convert(components, colors, 256);
            }
        }
    }

    [Fact]
    public void Image_rows_convert_through_an_output_intent_with_black_point_compensation_without_allocating()
    {
        // Issue #119: DeviceCMYK through the output intent's profile to sRGB, every intent, compensated and not; and ICCBased RGB to
        // the output intent's CMYK device through its BToA tables.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"), new PdfOptions().UseIccColorManagement());
        PdfOutputIntent intent = document.OutputIntents[0];
        PdfColorSpace rgb = document.GetColorSpace(IccProfiles.Space(IccProfiles.Rgb(), 3));
        PdfColorConverter[] converters =
        [
            .. Enum.GetValues<RenderingIntent>().Select(r => document.GetColorConverter(
                PdfDeviceCmykColorSpace.Instance, new ColorConversion { Intent = r, OutputIntent = intent })),
            document.GetColorConverter(PdfDeviceCmykColorSpace.Instance, new ColorConversion { BlackPointCompensation = BlackPointCompensation.Off, OutputIntent = intent }),
            document.GetColorConverter(rgb, new ColorConversion { Target = DeviceColorModel.Cmyk, OutputIntent = intent }),
        ];
        byte[] row = new byte[4096 * 4];
        byte[] pixels = new byte[4096 * 4];
        for (int i = 0; i < row.Length; i++)
        {
            row[i] = (byte)(i * 7);
        }

        long allocated = Allocations.Measure(Run);

        Assert.Equal(0, allocated);

        void Run()
        {
            foreach (PdfColorConverter converter in converters)
            {
                converter.Convert(row, pixels, 4096 / converter.InputCount);
            }
        }
    }
}
