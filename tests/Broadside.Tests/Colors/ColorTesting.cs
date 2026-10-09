using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;

namespace Broadside.Tests.Colors;

/// <summary>Builds documents for colour tests and reads colours back through the public processor seam.</summary>
internal static class ColorTesting
{
    /// <summary>
    /// A one-page document whose page has <paramref name="resources"/> and the content <paramref name="content"/>; further objects
    /// are numbered from 5.
    /// </summary>
    public static byte[] Page(string resources, string content, params string[] objects) => new TestPdf().Build(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources {resources} /Contents 4 0 R >>",
        $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream",
        .. objects,
    ]);

    /// <summary>Opens <paramref name="file"/> and records the colours of every paint of its first page.</summary>
    public static (List<Paint> Paints, string[] Codes) Run(byte[] file, PdfOptions? options = null)
    {
        using PdfDocument document = PdfDocument.Open(file, options ?? new PdfOptions());
        var recorder = new ColorRecorder();
        document.Pages[0].ProcessContent(recorder);
        return (recorder.Paints, [.. document.Diagnostics.Select(static d => d.Code)]);
    }

    /// <summary>Parses one COS object written in PDF syntax.</summary>
    public static CosObject Cos(string syntax) => CosObject.Parse(Encoding.Latin1.GetBytes(syntax));

    /// <summary>Converts one colour to RGB through <paramref name="document"/>'s colour management.</summary>
    public static float[] Rgb(this PdfDocument document, PdfColorSpace space, params float[] components)
    {
        float[] rgb = new float[3];
        document.GetColorConverter(space).Convert(components, rgb);
        return rgb;
    }

    /// <summary>Converts one colour with <paramref name="converter"/>.</summary>
    public static float[] ConvertOne(this PdfColorConverter converter, params float[] components)
    {
        float[] output = new float[converter.OutputCount];
        converter.Convert(components, output);
        return output;
    }

    /// <summary>Asserts that two colours agree to within <paramref name="tolerance"/>.</summary>
    public static void Near(float[] expected, ReadOnlySpan<float> actual, double tolerance = 1e-4)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"Component {i}: expected {expected[i]}, got {actual[i]} (all: [{string.Join(", ", actual.ToArray())}]).");
        }
    }

    /// <summary>Writes a colour as "Family c0 c1 ...".</summary>
    public static string Describe(in PdfColor color)
    {
        var text = new StringBuilder(color.ColorSpace.Family.ToString());
        foreach (float component in color.Components)
        {
            text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
        }

        if (color.PatternName is { } name)
        {
            text.Append(" /").Append(name.Value);
        }

        return text.ToString();
    }
}

/// <summary>What a paint saw: the fill and stroke colours as set, the RGB they convert to with the defaults in effect.</summary>
internal sealed record Paint(PdfColor Fill, PdfColor Stroke, PdfDefaultColorSpaces Defaults, float[] FillRgb, bool PaintsNothing)
{
    public string FillText => ColorTesting.Describe(Fill);

    public string StrokeText => ColorTesting.Describe(Stroke);
}

/// <summary>Records the graphics state's colours on every path paint, converted to RGB with the current default colour spaces.</summary>
internal sealed class ColorRecorder : ContentProcessor
{
    public List<Paint> Paints { get; } = [];

    public override ContentEvents Events => ContentEvents.Paths;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        PdfColor fill = context.State.FillColor;
        PdfDefaultColorSpaces defaults = context.DefaultColorSpaces;
        PdfColorConverter converter = context.Document.GetColorConverter(fill.ColorSpace, ColorConversion.FromState(context.State, DeviceColorModel.Rgb), defaults);
        float[] rgb = new float[3];
        converter.Convert(fill, rgb);
        Paints.Add(new Paint(fill, context.State.StrokeColor, defaults, rgb, converter.PaintsNothing));
    }
}
