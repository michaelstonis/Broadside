using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;

namespace Broadside.Tests.Graphics;

/// <summary>Opens shading and pattern documents and collects what the content interpreter reports about them.</summary>
internal static class ShadingTesting
{
    /// <summary>A one-page document: <paramref name="content"/>, the page's resources, and further objects numbered from 5.</summary>
    public static byte[] Page(string content, string resources, params string[] objects) => new TestPdf().Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources {resources} /Contents 4 0 R >>",
        $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        .. objects,
    ]);

    /// <summary>A stream object's text for <see cref="Page"/>.</summary>
    public static string StreamObject(string dictionary, string data) =>
        $"<< {dictionary} /Length {data.Length} >>\nstream\n{data}\nendstream";

    /// <summary>Runs page 0 and returns the <c>sh</c> events.</summary>
    public static List<SeenShading> Shadings(PdfDocument document)
    {
        var processor = new ShadingCollector();
        document.Pages[0].ProcessContent(processor);
        return processor.Shadings;
    }

    /// <summary>The single shading the page paints with <c>sh</c>.</summary>
    public static T OnlyShading<T>(PdfDocument document)
        where T : PdfShading
    {
        SeenShading seen = Assert.Single(Shadings(document));
        return Assert.IsType<T>(seen.Model);
    }

    /// <summary>Evaluates a shading's function at one input.</summary>
    public static float[] Color(this PdfShading shading, params float[] input)
    {
        float[] color = new float[shading.ColorComponentCount];
        shading.EvaluateFunction(input, color);
        return color;
    }

    /// <summary>Asserts two vectors agree to within <paramref name="tolerance"/>.</summary>
    public static void NearAll(IReadOnlyList<float> expected, IReadOnlyList<float> actual, double tolerance = 1e-5)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"Component {i}: expected {expected[i]}, got {actual[i]}.");
        }
    }

    /// <summary>What a <c>sh</c> event carried.</summary>
    internal sealed record SeenShading(PdfShading? Model, CosObject? Shading, string Name, Matrix Ctm);

    private sealed class ShadingCollector : ContentProcessor
    {
        public List<SeenShading> Shadings { get; } = [];

        public override ContentEvents Events => ContentEvents.Shadings;

        public override void PaintShading(in ShadingEvent shading, ContentContext context) =>
            Shadings.Add(new SeenShading(shading.Model, shading.Shading, System.Text.Encoding.Latin1.GetString(shading.ResourceName), shading.Ctm));
    }
}
