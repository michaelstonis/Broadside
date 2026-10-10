using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The row helpers, the Decode mapping and the inline-image end finder are hot paths and allocate nothing per row or per image
/// (CLAUDE.md, "Code conventions"; ISO 32000-2 §8.9.3, §8.9.7). This is the build-breaking half; <c>ImageUnpackBenchmarks</c> is
/// the measuring half. The finder is driven through the internal content reader, as <c>ContentAllocationTests</c> drives the lexer,
/// because no public seam exposes reading alone.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageAllocationTests
{
    private const int Width = 1021;
    private const int Rows = 200;

    [Fact]
    public void Unpacking_mapping_keying_and_unpremultiplying_rows_allocates_nothing()
    {
        byte[] row = [.. Enumerable.Range(0, Width * 3 * 2).Select(i => (byte)(i * 7))];
        byte[] bytes = new byte[Width * 3];
        ushort[] values = new ushort[Width * 3];
        float[] colours = new float[Width * 3];
        float[] alpha = new float[Width];
        byte[] coverage = new byte[Width * 3];
        double[] ranges = [10, 200, 0, 255, 30, 40];
        float[] matte = [1, 1, 1];
        ComponentRange[] componentRanges = [new(0, 1), new(0, 1), new(0, 1)];
        ImageDecodeMap map8 = ImageDecodeMap.Create([1, 0, 0, 1, 0.2, 0.8], 3, 8);
        ImageDecodeMap map16 = ImageDecodeMap.Create([1, 0, 0, 1, 0.2, 0.8], 3, 16);
        Array.Fill(alpha, 0.5f);

        void Pass()
        {
            for (int y = 0; y < Rows; y++)
            {
                for (int bits = 1; bits <= 8; bits *= 2)
                {
                    ImageRows.Unpack(row, bits, Width * 3, bytes);
                    ImageRows.UnpackScaled(row, bits, bits, Width * 3, bytes);
                }

                ImageRows.Unpack(row, 16, Width * 3, values);
                ImageRows.UnpackScaled(row, 16, 16, Width * 3, bytes);
                map8.Map(bytes, colours);
                map16.Map(values, colours);
                map16.Map(values, colours, componentRanges);
                map8.MapToIndices(values, bytes, 255);
                ImageRows.ColorKey(values, 3, ranges, coverage);
                ImageRows.Stencil(row, Width, inverted: true, coverage);
                ImageRows.Unpremultiply(colours, 3, alpha, matte, componentRanges);
                _ = ImageRows.MaskIndex(y, Rows, 77);
            }
        }

        Assert.Equal(0, Allocations.Measure(Pass));
    }

    [Fact]
    public void Finding_the_end_of_inline_images_allocates_nothing()
    {
        var content = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            content.Append("q BI /W 4 /H 1 /CS /G /BPC 8 ID EI Q EI Q\n");
            content.Append("q BI /W 4 /H 1 /CS /G /BPC 8 /F /RL /L 3 ID \u0001EI EI 1 0 0 1 0 0 cm Q\n");
            content.Append("q BI /W 2 /H 1 /CS /G /BPC 8 /F /A85 ID 87cUR~> EI Q\n");
            content.Append("q BI /W 1 /H 1 /CS /G /BPC 8 /F /DCT ID \u00FF\u00D8\u00FF\u00DA\u0000\u0002EI Q\u00FF\u00D9 EI Q\n");
        }

        byte[] source = Encoding.Latin1.GetBytes(content.ToString());
        var arena = new OperandArena();
        int images = default;
        long allocated = Allocations.Measure(() => images = ReadAll(source, arena), 50);

        Assert.Equal(800, images);
        Assert.Equal(0, allocated);
    }

    private static int ReadAll(ReadOnlySpan<byte> content, OperandArena arena)
    {
        var reader = new ContentReader(content, arena);
        int images = 0;
        while (reader.Next(out ReadOperator op))
        {
            images += op.Code == ContentOperatorCode.BeginInlineImage && op.DataLength > 0 ? 1 : 0;
            arena.Clear();
        }

        return images;
    }
}
