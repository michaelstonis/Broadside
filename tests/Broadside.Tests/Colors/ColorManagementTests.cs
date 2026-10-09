using System.Collections.Concurrent;
using Broadside.Graphics;
using Broadside.TestSupport;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// The colour-management extension point (ADR 0001; ISO 32000-2 §10.3, §10.4): replaced through the options, used for every
/// conversion of a base space, never handed an Indexed, Separation, DeviceN or Pattern space.
/// </summary>
public class ColorManagementTests
{
    [Fact]
    public void A_decorator_sees_only_device_CIE_based_and_ICCBased_spaces_while_a_page_of_every_family_is_painted()
    {
        var counting = new CountingColorManagement(ManagedColorManagement.Default);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("colorspace-families.pdf"), new PdfOptions().UseColorManagement(counting));
        var recorder = new ColorRecorder();
        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(12, recorder.Paints.Count);
        Assert.Equal(
            [PdfColorSpaceFamily.DeviceGray, PdfColorSpaceFamily.DeviceRgb, PdfColorSpaceFamily.DeviceCmyk, PdfColorSpaceFamily.CalGray,
             PdfColorSpaceFamily.CalRgb, PdfColorSpaceFamily.Lab, PdfColorSpaceFamily.IccBased],
            counting.Requested.Keys.Order());
        Assert.True(counting.Conversions > 0);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_replacement_decides_the_device_colour()
    {
        var magenta = new ConstantColorManagement();
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "0.2 0.4 0.6 rg 0 0 1 1 re f"), new PdfOptions().UseColorManagement(magenta));
        var recorder = new ColorRecorder();
        document.Pages[0].ProcessContent(recorder);

        Assert.Equal([1f, 0f, 1f], Assert.Single(recorder.Paints).FillRgb);
    }

    [Fact]
    public void Two_engines_with_different_colour_management_do_not_interfere()
    {
        byte[] file = Page("<< >>", string.Empty);
        using PdfDocument classic = new PdfEngine(new PdfOptions().UseColorManagement(ManagedColorManagement.Classic)).Open(file);
        using PdfDocument managed = PdfDocument.Open(file);

        Near([0f, 1f, 1f], classic.Rgb(PdfDeviceCmykColorSpace.Instance, 1, 0, 0, 0));
        Assert.True(managed.Rgb(PdfDeviceCmykColorSpace.Instance, 1, 0, 0, 0)[1] < 0.8f);
    }

    [Fact]
    public void The_managed_default_refuses_special_spaces()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => ManagedColorManagement.Default.CreateConverter(document.GetColorSpace(Cos("[/Indexed /DeviceRGB 0 <000000>]")), default));
    }

    /// <summary>Counts the converters requested per source family and the colours converted, then defers to the inner management.</summary>
    private sealed class CountingColorManagement(IColorManagement inner) : IColorManagement
    {
        private int _conversions;

        public ConcurrentDictionary<PdfColorSpaceFamily, int> Requested { get; } = new();

        public int Conversions => _conversions;

        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion)
        {
            Requested.AddOrUpdate(source.Family, 1, static (_, count) => count + 1);
            return new Counting(inner.CreateConverter(source, conversion), this);
        }

        private sealed class Counting(IColorConverter inner, CountingColorManagement owner) : IColorConverter
        {
            public int InputCount => inner.InputCount;

            public int OutputCount => inner.OutputCount;

            public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
            {
                Interlocked.Add(ref owner._conversions, count);
                inner.Convert(source, destination, count);
            }
        }
    }

    /// <summary>Paints everything magenta.</summary>
    private sealed class ConstantColorManagement : IColorManagement
    {
        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion) => new Magenta(source.ComponentCount);

        private sealed class Magenta(int inputs) : IColorConverter
        {
            public int InputCount => inputs;

            public int OutputCount => 3;

            public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    destination[3 * i] = 1;
                    destination[(3 * i) + 1] = 0;
                    destination[(3 * i) + 2] = 1;
                }
            }
        }
    }
}
