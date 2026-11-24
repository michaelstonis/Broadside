using Broadside.Graphics;

#pragma warning disable CA5394 // pseudo-random test input, not security

namespace Broadside.Rendering.Tests;

/// <summary>
/// The mesh layer's twin (issue #604): a DeviceCMYK group's first pass draws its patch meshes' black layer in the same walk as its
/// C, M and Y (ISO 32000-2 §11.4.7, §8.7.4.5.5), so the twin must hold, bit for bit, what a layer of its own paints from the same
/// triangles with the grey level as each vertex's three colours.
/// </summary>
public sealed class MeshLayerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_twin_holds_the_pixels_a_layer_of_its_own_paints_with_the_grey_level_in_every_channel(bool box)
    {
        var random = new Random(604);
        var layer = new MeshLayer();
        var plain = new MeshLayer();
        var grey = new MeshLayer();
        foreach (MeshLayer each in (MeshLayer[])[layer, plain, grey])
        {
            Assert.True(each.Begin(-3, -2, 61, 47));
            if (box)
            {
                each.UseBox(Matrix.Identity, new PdfRectangle(4.5, 3.25, 52, 40.75));
            }
        }

        layer.BeginTwin();
        for (int i = 0; i < 3000; i++)
        {
            MeshVertex a = Vertex(random);
            MeshVertex b = Vertex(random);
            MeshVertex c = Vertex(random);
            (float ka, float kb, float kc) = i % 97 == 0
                ? (float.MaxValue, -float.MaxValue, 0f) // a level plane that is not finite: the twin is left alone, the colours painted
                : (Level(random), Level(random), Level(random));
            layer.FillTriangle(a, b, c, ka, kb, kc);
            plain.FillTriangle(a, b, c);
            grey.FillTriangle(a with { C0 = ka, C1 = ka, C2 = ka }, b with { C0 = kb, C1 = kb, C2 = kb }, c with { C0 = kc, C1 = kc, C2 = kc });
        }

        Assert.True(layer.Pixels.SequenceEqual(plain.Pixels));
        Assert.True(layer.TwinPixels.SequenceEqual(grey.Pixels));
        Assert.Equal(plain.HasPaint, layer.HasPaint);
        Assert.Equal(grey.HasPaint, layer.TwinHasPaint);
        Assert.True(layer.TwinHasPaint);
    }

    [Fact]
    public void An_adopted_twin_is_the_layer_its_triangles_would_have_painted()
    {
        var layer = new MeshLayer();
        var grey = new MeshLayer();
        Assert.True(layer.Begin(0, 0, 20, 10));
        Assert.True(grey.Begin(0, 0, 20, 10));
        layer.BeginTwin();
        var a = new MeshVertex(1.25, 0.5, 0.1f, 0.2f, 0.3f);
        var b = new MeshVertex(18.5, 2, 0.9f, 0.8f, 0.7f);
        var c = new MeshVertex(6, 9.75, 0.4f, 0.6f, 0.5f);
        layer.FillTriangle(a, b, c, 0.25f, 0.75f, 1f);
        grey.FillTriangle(a with { C0 = 0.25f, C1 = 0.25f, C2 = 0.25f }, b with { C0 = 0.75f, C1 = 0.75f, C2 = 0.75f }, c with { C0 = 1f, C1 = 1f, C2 = 1f });
        bool hasPaint = layer.TwinHasPaint;
        uint[] twin = layer.TakeTwin();

        Assert.True(layer.Begin(0, 0, 3, 3));
        uint[] previous = layer.Adopt(twin, 0, 0, 20, 10, hasPaint);

        Assert.True(layer.Pixels.SequenceEqual(grey.Pixels));
        Assert.True(layer.HasPaint);
        Assert.NotSame(twin, previous);
    }

    private static MeshVertex Vertex(Random random) =>
        new(-10 + (random.NextDouble() * 80), -8 + (random.NextDouble() * 62), Level(random), Level(random), Level(random));

    private static float Level(Random random) => (float)(-0.1 + (random.NextDouble() * 1.2));
}
