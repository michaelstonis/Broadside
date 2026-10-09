using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Tests.Document;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// Setting colours and converting runs of colours are hot paths and allocate nothing once warm (CLAUDE.md "Code conventions";
/// ISO 32000-2 §8.6.8). This is the build-breaking half; <c>ColorConversionBenchmarks</c> is the measuring half. In the heavy
/// collection, like the other allocation tests.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class ColorAllocationTests
{
    private const int WarmUp = 50;

    [Fact]
    public void Setting_colours_in_every_kind_of_space_allocates_nothing_once_warm()
    {
        var content = new StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            content.Append("/Sep cs 0.5 scn /Ref CS 0.25 SCN /Ix cs 1 sc /Lab cs 50 10 -10 sc /DN cs 0.1 0.2 scn ")
                .Append("/P cs 1 0 0 /T scn 0.2 g 0.1 0.2 0.3 RG 0 0 0 1 k 0 0 1 1 re B\n");
        }

        using PdfDocument document = PdfDocument.Open(Page(
            "<< /ColorSpace << /Sep [/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.5 1 0] /N 1 >>] "
            + "/Ref 5 0 R /Ix [/Indexed /DeviceRGB 1 <FF0000 0000FF>] /Lab [/Lab << /WhitePoint [0.9642 1 0.8249] >>] "
            + "/DN [/DeviceN [/A /B] /DeviceRGB << /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] /Length 9 >>] /P [/Pattern /DeviceRGB] >> "
            + "/Pattern << /T 6 0 R >> >>",
            content.ToString(),
            "[/Separation /Other /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]",
            "<< /PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 1 1] /XStep 1 /YStep 1 /Resources << >> /Length 0 >>\nstream\n\nendstream"));
        PdfPage page = document.Pages[0];
        var processor = new ColorCounter();
        for (int pass = 0; pass < WarmUp; pass++)
        {
            page.ProcessContent(processor);
        }

        processor.Paints = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        page.ProcessContent(processor);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(500, processor.Paints);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Converting_runs_of_colours_allocates_nothing_once_warm()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorConverter[] converters =
        [
            document.GetColorConverter(PdfDeviceCmykColorSpace.Instance),
            document.GetColorConverter(document.GetColorSpace(Cos("[/Lab << /WhitePoint [0.9642 1 0.8249] >>]"))),
            document.GetColorConverter(document.GetColorSpace(Cos("[/Separation /S /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.5 1 0] /N 1 >>]"))),
            document.GetColorConverter(document.GetColorSpace(Cos("[/Indexed /DeviceRGB 1 <FF0000 0000FF>]"))),
            document.GetColorConverter(document.GetColorSpace(Cos("[/DeviceN [/A /B] /DeviceCMYK << /FunctionType 2 /Domain [0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 0 0] /N 1 >>]"))),
        ];
        byte[] samples = new byte[4096 * 4];
        byte[] output = new byte[4096 * 3];
        float[] components = new float[256 * 4];
        float[] colors = new float[256 * 3];
        for (int pass = 0; pass < WarmUp; pass++)
        {
            Run();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);

        void Run()
        {
            foreach (PdfColorConverter converter in converters)
            {
                converter.Convert(samples, output, 4096);
                converter.Convert(components, colors, 256);
            }
        }
    }

    /// <summary>Counts paints and reads both colours, as a renderer would.</summary>
    private sealed class ColorCounter : ContentProcessor
    {
        public int Paints { get; set; }

        public float Sum { get; private set; }

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            Paints++;
            Sum += context.State.FillColor[0] + context.State.StrokeColor.ComponentCount;
            _ = context.DefaultColorSpaces;
        }
    }
}
