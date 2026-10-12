using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// The display list is on the hot path (ADR 0006, CLAUDE.md "Code conventions"): building allocates per page, not per operation,
/// and replaying allocates nothing. <c>DisplayListBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DisplayListAllocationTests
{
    [Fact]
    public void Replaying_a_display_list_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var backend = new CountingBackend();
        Action replay = () => list.Replay(backend);

        long allocated = Allocations.Measure(replay);

        Assert.Equal(0, allocated);
        Assert.True(backend.Fills > 1000);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(2000)]
    public void Building_a_display_list_allocates_per_page_not_per_operation(int fills)
    {
        // The same rectangle in the same colour: one interned path, one colour, one record per fill. Anything allocated per fill
        // (a path copy, a colour object, a boxed operation) would cost far more than the 16 bytes per operation allowed here.
        var content = new StringBuilder("0.5 0 0.25 0.1 k ");
        for (int i = 0; i < fills; i++)
        {
            content.Append("10 10 100 50 re f\n");
        }

        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content.ToString())));
        PdfPage page = document.Pages[0];
        Action build = () => page.CreateDisplayList();

        long allocated = Allocations.Measure(build);

        Assert.InRange(allocated, 1, 2048 + (16 * fills));
    }

    /// <summary>Counts fills and touches what each call hands over, without allocating.</summary>
    private sealed class CountingBackend : IRenderingBackend
    {
        public int Fills { get; private set; }

        public double Checksum { get; private set; }

        public void SaveState() => Checksum++;

        public void RestoreState() => Checksum--;

        public void SetTransform(in Matrix transform) => Checksum += transform.E;

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            Fills++;
            Checksum += path.Points.Length + color.Components.Length + (int)color.ColorSpace.Family;
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) =>
            Checksum += image.Width + image.Height;
    }
}
