using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A printer's color reference card for an invented ink maker: CIE L*a*b* swatches, CalRGB and CalGray ramps next to their device
/// counterparts, an Indexed image and Indexed fills, a duotone in a two-ink DeviceN space, a five-ink DeviceN (process plus a spot)
/// tint chart, and an overprint demonstration (an ExtGState with overprint mode 1). The calibrated and Lab spaces are written as
/// COS arrays and read back through <see cref="PdfDocument.GetColorSpace"/>; the overprint patch is a hand-written content stream.
/// </summary>
internal static class ColorGuide
{
    private static readonly string[] Inks = ["Cyan", "Magenta", "Yellow", "Black", "Lumen Gold"];

    public static void Write(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Lumen Inks color reference", "Lab, calibrated, Indexed and DeviceN color, and overprint, on a printer's reference card.");
        PdfCanvasFont regular = context.Font(document, "ibmplexmono/IBMPlexMono-Regular.ttf");
        PdfCanvasFont title = context.Font(document, "poppins/Poppins-SemiBold.ttf");
        PdfCanvasFont sans = context.Font(document, "poppins/Poppins-Regular.ttf");

        LabAndCalibrated(document, title, sans, regular);
        IndexedAndDeviceN(document, title, sans, regular);
        document.Save(context.OutputPath("color-guide.pdf"));
    }

    private static void LabAndCalibrated(PdfDocument document, PdfCanvasFont title, PdfCanvasFont sans, PdfCanvasFont mono)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        PdfColorSpace lab = document.GetColorSpace(new CosArray([new CosName("Lab"), new CosDictionary
        {
            [new CosName("WhitePoint")] = Numbers(0.9642, 1, 0.8249),
            [new CosName("Range")] = Numbers(-128, 127, -128, 127),
        }]));
        PdfColorSpace calRgb = document.GetColorSpace(new CosArray([new CosName("CalRGB"), new CosDictionary
        {
            [new CosName("WhitePoint")] = Numbers(0.9505, 1, 1.089),
            [new CosName("Gamma")] = Numbers(2.2, 2.2, 2.2),
            [new CosName("Matrix")] = Numbers(0.4124, 0.2126, 0.0193, 0.3576, 0.7152, 0.1192, 0.1805, 0.0722, 0.9505),
        }]));
        PdfColorSpace calGray = document.GetColorSpace(new CosArray([new CosName("CalGray"), new CosDictionary
        {
            [new CosName("WhitePoint")] = Numbers(0.9505, 1, 1.089),
            [new CosName("Gamma")] = new CosReal(2.2),
        }]));

        using PdfCanvas canvas = page.CreateCanvas();
        Header(canvas, title, sans, "Color reference · CIE L*a*b* and calibrated color", 1);

        // Lab: four lightness rows by eight hues at chroma 45, and a neutral column.
        double x0 = Mm(20);
        double y0 = Mm(232);
        double cell = Mm(19);
        canvas.SetFillColor(Gray(0.15));
        Text(canvas, sans, 9, x0, y0 + Mm(8), "CIE L*a*b* (D50 white point), chroma 45, hue every 45°, and the neutral axis");
        for (int row = 0; row < 4; row++)
        {
            double l = 85 - (row * 18);
            for (int column = 0; column < 9; column++)
            {
                double a = 0, b = 0;
                if (column < 8)
                {
                    double hue = column * Math.PI / 4;
                    a = 45 * Math.Cos(hue);
                    b = 45 * Math.Sin(hue);
                }

                double x = x0 + (column * cell);
                double y = y0 - ((row + 1) * cell);
                canvas.SetFillColor(new PdfColor(lab, [(float)l, (float)a, (float)b]));
                canvas.Rectangle(x, y + Mm(4), cell - Mm(1.5), cell - Mm(5.5));
                canvas.Fill();
                canvas.SetFillColor(Gray(0.3));
                Text(canvas, mono, 4.6, x, y + Mm(1.5), string.Create(CultureInfo.InvariantCulture, $"{l:0} {a:0} {b:0}"));
            }
        }

        // CalRGB and CalGray ramps above their device equivalents: the same numbers, a different meaning.
        double ry = Mm(118);
        canvas.SetFillColor(Gray(0.15));
        Text(canvas, sans, 9, x0, ry + Mm(16), "CalRGB (sRGB primaries, gamma 2.2) over DeviceRGB, and CalGray (gamma 2.2) over DeviceGray");
        for (int i = 0; i < 10; i++)
        {
            float t = (i + 1) / 10f;
            double x = x0 + (i * Mm(17));
            (PdfColorSpace Space, float[] Components, string Label)[] rows =
            [
                (calRgb, [t, 0.25f * t, 0.1f], "CalRGB"),
                (PdfDeviceRgbColorSpace.Instance, [t, 0.25f * t, 0.1f], "DeviceRGB"),
                (calGray, [t], "CalGray"),
                (PdfDeviceGrayColorSpace.Instance, [t], "DeviceGray"),
            ];
            for (int r = 0; r < rows.Length; r++)
            {
                canvas.SetFillColor(new PdfColor(rows[r].Space, rows[r].Components));
                canvas.Rectangle(x, ry - (r * Mm(13)), Mm(15.5), Mm(11));
                canvas.Fill();
                if (i == 0)
                {
                    canvas.SetFillColor(Gray(0.3));
                    Text(canvas, mono, 5.5, x0 - Mm(15.5), ry - (r * Mm(13)) + Mm(4), rows[r].Label);
                }
            }
        }

        canvas.SetFillColor(Gray(0.4));
        Text(canvas, sans, 7.5, x0, Mm(40), "Lumen Inks is invented for the Broadside sample documents. Values are for a viewer's color management, not for proofing.");
    }

    private static void IndexedAndDeviceN(PdfDocument document, PdfCanvasFont title, PdfCanvasFont sans, PdfCanvasFont mono)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));

        // An Indexed image: a 64-color palette (a cool-to-warm ramp) over a field of contour bands.
        const int Palette = 64;
        byte[] lookup = new byte[Palette * 3];
        for (int i = 0; i < Palette; i++)
        {
            double t = i / (double)(Palette - 1);
            lookup[(i * 3) + 0] = (byte)Math.Round(255 * Math.Clamp((1.6 * t) - 0.3, 0, 1));
            lookup[(i * 3) + 1] = (byte)Math.Round(255 * Math.Clamp(1 - (2.2 * Math.Abs(t - 0.5)), 0.1, 1));
            lookup[(i * 3) + 2] = (byte)Math.Round(255 * Math.Clamp(1.3 - (1.6 * t), 0, 1));
        }

        PdfIndexedColorSpace indexed = document.CreateIndexedColorSpace(PdfDeviceRgbColorSpace.Instance, lookup);
        const int W = 320, H = 200;
        byte[] samples = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                double v = (Math.Sin(x / 23.0) * Math.Cos(y / 17.0)) + (0.6 * Math.Sin((x + (2 * y)) / 41.0)) + (0.3 * Math.Cos((x - y) / 11.0));
                samples[(y * W) + x] = (byte)Math.Clamp((int)((v + 1.9) / 3.8 * Palette), 0, Palette - 1);
            }
        }

        PdfImage field = document.CreateImage(samples, new PdfImageLayout { Width = W, Height = H, BitsPerComponent = 8, ColorSpace = indexed }, PdfImageCompression.Flate);

        // A duotone: black and a spot blue, in one DeviceN space whose alternate is CMYK.
        PdfFunction duoTint = document.CreatePostScriptFunction([0, 1, 0, 1], [0, 1, 0, 1, 0, 1, 0, 1], "{ dup 0.85 mul exch dup 0.35 mul exch 0.1 mul 4 -1 roll add dup 1 gt { pop 1 } if 0 exch }");
        PdfDeviceNColorSpace duotone = document.CreateDeviceNColorSpace(["Black", "Lumen Blue"], PdfDeviceCmykColorSpace.Instance, duoTint);
        const int S = 260;
        byte[] moon = new byte[S * S * 2];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                double nx = ((x + 0.5) / S * 2) - 1, ny = 1 - ((y + 0.5) / S * 2);
                double r2 = (nx * nx) + (ny * ny);
                double black, blue;
                if (r2 > 0.92)
                {
                    black = 0.92;
                    blue = 1;
                }
                else
                {
                    double nz = Math.Sqrt(0.92 - r2);
                    double light = Math.Clamp((-0.5 * nx) + (0.45 * ny) + (0.74 * nz), 0, 1);
                    double craters = 0.12 * (Math.Sin(nx * 19) * Math.Sin(ny * 23)) + 0.08 * Math.Sin((nx + ny) * 41);
                    black = Math.Clamp(0.9 - (0.85 * light) + craters, 0, 1);
                    blue = Math.Clamp(0.25 + (0.4 * (1 - light)), 0, 1);
                }

                moon[((y * S) + x) * 2] = (byte)(255 * black);
                moon[(((y * S) + x) * 2) + 1] = (byte)(255 * blue);
            }
        }

        PdfImage duo = document.CreateImage(moon, new PdfImageLayout { Width = S, Height = S, BitsPerComponent = 8, ColorSpace = duotone }, PdfImageCompression.Flate);

        // Five inks: process and a spot gold, with a CMYK alternate that adds the gold as yellow and a little magenta.
        PdfFunction fiveTint = document.CreatePostScriptFunction([0, 1, 0, 1, 0, 1, 0, 1, 0, 1], [0, 1, 0, 1, 0, 1, 0, 1],
            "{ 4 index 4 index 2 index 0.25 mul add dup 1 gt { pop 1 } if 4 index 3 index 0.85 mul add dup 1 gt { pop 1 } if " +
            "4 index 4 index 0.05 mul add dup 1 gt { pop 1 } if 9 4 roll pop pop pop pop pop }");
        PdfDeviceNColorSpace five = document.CreateDeviceNColorSpace(Inks, PdfDeviceCmykColorSpace.Instance, fiveTint);

        using (PdfCanvas canvas = page.CreateCanvas())
        {
            Header(canvas, title, sans, "Color reference · Indexed, DeviceN and overprint", 2);
            canvas.SetFillColor(Gray(0.15));
            Text(canvas, sans, 9, Mm(20), Mm(240), "Indexed: a 64-entry RGB palette, 8 bits per index");
            canvas.DrawImage(field, Mm(20), Mm(180), Mm(90), Mm(56.25));
            for (int i = 0; i < 16; i++)
            {
                canvas.SetFillColor(new PdfColor(indexed, [i * 4]));
                canvas.Rectangle(Mm(118) + (i % 4 * Mm(18)), Mm(222) - (i / 4 * Mm(14)), Mm(16), Mm(12));
                canvas.Fill();
            }

            canvas.SetFillColor(Gray(0.15));
            Text(canvas, sans, 9, Mm(20), Mm(170), "DeviceN duotone: Black and Lumen Blue");
            canvas.DrawImage(duo, Mm(20), Mm(105), Mm(62), Mm(62));
            Text(canvas, sans, 9, Mm(95), Mm(170), "DeviceN, five inks: tints of each ink, then mixes with the spot");
            for (int ink = 0; ink < Inks.Length; ink++)
            {
                for (int step = 0; step < 5; step++)
                {
                    float[] tint = new float[5];
                    tint[ink] = (step + 1) / 5f;
                    if (step == 4 && ink != 4)
                    {
                        tint[4] = 0.6f;
                    }

                    canvas.SetFillColor(new PdfColor(five, tint));
                    canvas.Rectangle(Mm(95) + (step * Mm(19)), Mm(155) - (ink * Mm(11)), Mm(17), Mm(9));
                    canvas.Fill();
                }

                canvas.SetFillColor(Gray(0.3));
                Text(canvas, mono, 5.5, Mm(95) + (5 * Mm(19)) - Mm(1), Mm(158) - (ink * Mm(11)), Inks[ink]);
            }

            canvas.SetFillColor(Gray(0.15));
            Text(canvas, sans, 9, Mm(20), Mm(92), "Overprint (OPM 1): magenta printed over cyan, without and with overprint");
            Text(canvas, mono, 6, Mm(20), Mm(48), "Left: knocks out (the magenta replaces the cyan). Right: overprints (an overprint preview shows blue).");
        }

        // The overprint patches, hand-written: an ExtGState with OP, op and OPM 1, and two pairs of overlapping CMYK squares.
        var resources = (CosDictionary)document.Resolve(page.Dictionary[new CosName("Resources")]);
        var states = resources.TryGetValue(new CosName("ExtGState"), out CosObject? existing) ? (CosDictionary)document.Resolve(existing) : new CosDictionary();
        states[new CosName("GSOverprint")] = new CosDictionary
        {
            [new CosName("Type")] = new CosName("ExtGState"),
            [new CosName("OP")] = CosBoolean.True,
            [new CosName("op")] = CosBoolean.True,
            [new CosName("OPM")] = new CosInteger(1),
        };
        resources[new CosName("ExtGState")] = states;
        string patches = string.Create(CultureInfo.InvariantCulture,
            $"q 1 0 0 0 k {Mm(20):0.##} {Mm(55):0.##} {Mm(40):0.##} {Mm(30):0.##} re f 0 1 0 0 k {Mm(40):0.##} {Mm(60):0.##} {Mm(40):0.##} {Mm(25):0.##} re f Q\n" +
            $"q 1 0 0 0 k {Mm(110):0.##} {Mm(55):0.##} {Mm(40):0.##} {Mm(30):0.##} re f /GSOverprint gs 0 1 0 0 k {Mm(130):0.##} {Mm(60):0.##} {Mm(40):0.##} {Mm(25):0.##} re f Q\n");
        CosReference stream = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(patches)));
        CosObject contents = document.Resolve(page.Dictionary[new CosName("Contents")]);
        page.Dictionary[new CosName("Contents")] = contents is CosArray array
            ? new CosArray([.. array, stream])
            : new CosArray([page.Dictionary[new CosName("Contents")], stream]);
    }

    private static void Header(PdfCanvas canvas, PdfCanvasFont title, PdfCanvasFont sans, string heading, int number)
    {
        canvas.SetFillColor(Cmyk(0, 0.25, 0.85, 0.05));
        canvas.Rectangle(0, Mm(297 - 30), Mm(210), Mm(30));
        canvas.Fill();
        canvas.SetFillColor(Gray(0.1));
        Text(canvas, title, 20, Mm(20), Mm(297 - 17), "Lumen Inks");
        Text(canvas, sans, 9.5, Mm(20), Mm(297 - 24), heading);
        Text(canvas, sans, 8, Mm(180), Mm(297 - 24), $"Card {number} of 2");
    }

    private static CosArray Numbers(params double[] values) => new([.. values.Select(static v => new CosReal(v))]);

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
