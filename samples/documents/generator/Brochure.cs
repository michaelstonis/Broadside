using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A four-page A5 planetarium brochure drawn directly with PdfCanvas, as a print-ready file: each sheet carries a 3 mm bleed
/// (BleedBox), its trim (TrimBox) and crop marks in the slug, drawn in the registration colour (Separation All). It exercises what
/// "blue-sky" print files use: a spot colour (Separation), CMYK, axial, radial and mesh shadings, a tiling pattern, nested
/// transforms, every separable and non-separable blend mode, luminosity and alpha soft masks, a knockout group, an ICC-tagged JPEG
/// and a PNG with an alpha channel.
/// </summary>
internal static class Brochure
{
    private static readonly double Slug = Mm(10);
    private static readonly double Bleed = Mm(3);
    private static readonly double Width = Mm(148);
    private static readonly double Height = Mm(210);

    private sealed record Kit(
        PdfDocument Document,
        PdfCanvasFont Display,
        PdfCanvasFont Sans,
        PdfCanvasFont SansBold,
        PdfCanvasFont Light,
        PdfSeparationColorSpace Gold,
        PdfSeparationColorSpace Registration);

    public static void Write(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Lumen Planetarium: spring season", "A print-ready brochure drawn with Broadside's PdfCanvas: bleed, crop marks, a spot colour, shadings, blend modes and soft masks.");

        var kit = new Kit(
            document,
            context.Font(document, "bebasneue/BebasNeue-Regular.ttf"),
            context.Font(document, "firasans/FiraSans-Regular.ttf"),
            context.Font(document, "firasans/FiraSans-Bold.ttf"),
            context.Font(document, "firasans/FiraSans-Light.ttf"),
            document.CreateSeparationColorSpace("Lumen Gold", Cmyk(0, 0.25, 0.85, 0.05)),
            document.CreateSeparationColorSpace("All", Cmyk(1, 1, 1, 1)));

        byte[] earth = File.ReadAllBytes(Path.Combine(context.Root, "catalogue", "images", "earth.jpg"));
        byte[] aldrin = File.ReadAllBytes(Path.Combine(context.Root, "catalogue", "images", "aldrin.jpg"));
        byte[] astronaut = File.ReadAllBytes(Path.Combine(context.Root, "catalogue", "images", "mccandless-cutout.png"));

        Cover(kit, NewSheet(document), document.CreateImageFromJpeg(earth));
        Programme(kit, NewSheet(document));
        Highlights(kit, NewSheet(document), document.CreateImageFromJpeg(aldrin), document.CreateImageFromPng(astronaut));
        Back(kit, NewSheet(document));
        document.Save(context.OutputPath("brochure.pdf"));
    }

    private static PdfPage NewSheet(PdfDocument document)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width + (2 * Slug), Height + (2 * Slug)));
        page.TrimBox = new PdfRectangle(Slug, Slug, Slug + Width, Slug + Height);
        page.BleedBox = new PdfRectangle(Slug - Bleed, Slug - Bleed, Slug + Width + Bleed, Slug + Height + Bleed);
        return page;
    }

    // ---- Page 1: the cover --------------------------------------------------------------------------------------------------

    private static void Cover(Kit kit, PdfPage page, PdfImage earth)
    {
        PdfDocument document = kit.Document;
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SaveState();
        canvas.Translate(Slug, Slug);

        // A night sky to the bleed: a radial shading, then a tiling pattern of stars.
        canvas.SaveState();
        BleedRectangle(canvas);
        canvas.Clip();
        canvas.EndPath();
        canvas.PaintShading(document.CreateRadialShading(new PathPoint(Width * 0.7, Height * 0.75), 0, new PathPoint(Width * 0.5, Height * 0.5), Height * 0.8,
            [new PdfGradientStop(0, Rgb(0.22, 0.16, 0.42)), new PdfGradientStop(0.55, Rgb(0.05, 0.07, 0.2)), new PdfGradientStop(1, Rgb(0.01, 0.01, 0.05))],
            new PdfShadingOptions { ExtendStart = true, ExtendEnd = true }));
        PdfTilingPattern stars = Stars(document);
        canvas.SetFillPattern(stars);
        BleedRectangle(canvas);
        canvas.Fill();
        canvas.RestoreState();

        // The Earth, faded at its edge by a luminosity soft mask drawn with a radial shading.
        double size = Mm(118);
        double ex = (Width - size) / 2;
        double ey = Mm(48);
        PdfFormXObject maskGroup = document.CreateFormXObject(new PdfRectangle(0, 0, Width, Height));
        maskGroup.CreateTransparencyGroup().ColorSpace = PdfDeviceGrayColorSpace.Instance;
        using (PdfCanvas mask = maskGroup.CreateCanvas())
        {
            mask.PaintShading(document.CreateRadialShading(new PathPoint(ex + (size / 2), ey + (size / 2)), size * 0.40, new PathPoint(ex + (size / 2), ey + (size / 2)), size * 0.5,
                [new PdfGradientStop(0, Gray(1)), new PdfGradientStop(1, Gray(0))], new PdfShadingOptions { ExtendStart = true }));
        }

        canvas.SaveState();
        canvas.SetSoftMask(document.CreateSoftMask(PdfSoftMaskType.Luminosity, maskGroup));
        canvas.DrawImage(earth, ex, ey, size, size);
        canvas.RestoreState();

        // Glows in the Screen blend mode.
        canvas.SaveState();
        canvas.SetBlendMode(BlendMode.Screen);
        foreach ((double x, double y, double r, PdfColor c) in new[]
        {
            (Width * 0.18, Height * 0.82, Mm(26), Rgb(0.35, 0.2, 0.6)),
            (Width * 0.86, Height * 0.30, Mm(30), Rgb(0.1, 0.3, 0.55)),
            (Width * 0.62, Height * 0.92, Mm(16), Rgb(0.5, 0.35, 0.1)),
        })
        {
            canvas.SetFillAlpha(0.8);
            canvas.SetFillPattern(document.CreateShadingPattern(document.CreateRadialShading(new PathPoint(x, y), 0, new PathPoint(x, y), r,
                [new PdfGradientStop(0, c), new PdfGradientStop(1, Rgb(0, 0, 0))])));
            Circle(canvas, x, y, r);
            canvas.Fill();
        }

        canvas.RestoreState();

        // The title, in the spot colour, and a band in CMYK.
        canvas.SetFillColor(new PdfColor(kit.Gold, [1f]));
        Text(canvas, kit.Display, 64, Mm(12), Height - Mm(36), "LUMEN");
        canvas.SetFillColor(Gray(1));
        Text(canvas, kit.Light, 13, Mm(13), Height - Mm(44), "PLANETARIUM · SPRING SEASON 2027");

        canvas.SaveState();
        canvas.SetFillColor(Cmyk(0.9, 0.55, 0, 0.1));
        canvas.SetFillAlpha(0.85);
        canvas.Rectangle(-Bleed, Mm(12), Width + (2 * Bleed), Mm(26));
        canvas.Fill();
        canvas.RestoreState();
        canvas.SetFillColor(Gray(1));
        Text(canvas, kit.SansBold, 12, Mm(12), Mm(27), "Twelve new shows under the dome");
        Text(canvas, kit.Sans, 9.5, Mm(12), Mm(20), "Live astronomers · Family Saturdays · Late nights with telescopes");

        // A rotated sticker: nested transforms, a spot-colour tint and text set along the rotation.
        canvas.SaveState();
        canvas.Translate(Width - Mm(28), Height - Mm(72));
        canvas.Rotate(14);
        canvas.SetFillColor(new PdfColor(kit.Gold, [0.85f]));
        Star(canvas, 0, 0, Mm(17), Mm(13), 16);
        canvas.Fill();
        canvas.SetFillColor(Rgb(0.08, 0.06, 0.2));
        CenteredText(canvas, kit.Display, 18, 0, 2, "NEW");
        CenteredText(canvas, kit.SansBold, 6.5, 0, -8, "FULL-DOME");
        canvas.RestoreState();

        canvas.RestoreState();
        CropMarks(kit, canvas);
    }

    private static PdfTilingPattern Stars(PdfDocument document)
    {
        PdfTilingPattern pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 90, 90), 90, 90, PdfTilingPaintType.Colored);
        using PdfCanvas canvas = pattern.CreateCanvas();
#pragma warning disable CA5394 // a fixed seed, so the stars are the same in every build; nothing here is security-sensitive
        var random = new Random(1908);
        for (int i = 0; i < 26; i++)
        {
            double x = random.NextDouble() * 90;
            double y = random.NextDouble() * 90;
            double r = 0.2 + (random.NextDouble() * random.NextDouble() * 1.1);
            double level = 0.55 + (random.NextDouble() * 0.45);
            canvas.SetFillColor(Rgb(level, level, Math.Min(1, level + 0.1)));
            Circle(canvas, x, y, r);
            canvas.Fill();
        }

#pragma warning restore CA5394
        return pattern;
    }

    // ---- Page 2: the programme -----------------------------------------------------------------------------------------------

    private static void Programme(Kit kit, PdfPage page)
    {
        PdfDocument document = kit.Document;
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SaveState();
        canvas.Translate(Slug, Slug);

        // Paper tint to the bleed, with an axial shading band down the outer edge.
        canvas.SetFillColor(Cmyk(0.02, 0.02, 0.06, 0));
        BleedRectangle(canvas);
        canvas.Fill();
        canvas.SaveState();
        canvas.Rectangle(Width - Mm(24), -Bleed, Mm(24) + Bleed, Height + (2 * Bleed));
        canvas.Clip();
        canvas.EndPath();
        canvas.PaintShading(document.CreateAxialShading(new PathPoint(0, 0), new PathPoint(0, Height),
            [new PdfGradientStop(0, Cmyk(0.9, 0.55, 0, 0.1)), new PdfGradientStop(0.6, Cmyk(0.75, 0.9, 0, 0.2)), new PdfGradientStop(1, Cmyk(0.1, 0.3, 0.9, 0))]));
        canvas.RestoreState();

        // A rosette of rotated, translucent squares: transforms and Multiply.
        canvas.SaveState();
        canvas.Translate(Width - Mm(12), Height - Mm(30));
        canvas.SetBlendMode(BlendMode.Multiply);
        for (int i = 0; i < 12; i++)
        {
            canvas.SaveState();
            canvas.Rotate(i * 15);
            canvas.Scale(1 - (i * 0.04), 1 - (i * 0.04));
            canvas.SetFillColor(i % 2 == 0 ? new PdfColor(kit.Gold, [0.5f]) : Cmyk(0.5, 0.1, 0, 0));
            canvas.SetFillAlpha(0.35);
            canvas.Rectangle(-Mm(9), -Mm(9), Mm(18), Mm(18));
            canvas.Fill();
            canvas.RestoreState();
        }

        canvas.RestoreState();

        // The section label, rotated up the band.
        canvas.SaveState();
        canvas.Translate(Width - Mm(9), Mm(20));
        canvas.Rotate(90);
        canvas.SetFillColor(Gray(1));
        Text(canvas, kit.Display, 22, 0, 0, "THE PROGRAMME · MARCH TO JUNE");
        canvas.RestoreState();

        canvas.SetFillColor(Cmyk(0.85, 0.65, 0.2, 0.55));
        Text(canvas, kit.Display, 34, Mm(12), Height - Mm(24), "WHAT'S ON");
        double y = Height - Mm(44);
        (string When, string Title, string About)[] shows =
        [
            ("Daily 11:00", "Our Restless Sun", "Sunspots, flares and the solar wind, with live images from our own solar telescope on clear days."),
            ("Daily 14:00", "Moonwalkers", "The six Apollo landings, retold under the dome with the photographs the astronauts took."),
            ("Sat 10:00", "Little Stargazers", "A gentle first show for children under seven, with songs, constellations and a sleepy owl."),
            ("Fri 20:30", "Deep Sky Late", "Galaxies, nebulae and clusters, then telescopes on the roof terrace if the sky allows."),
            ("Sun 16:00", "Living on Mars", "What it would take to build a home on the red planet, from water to soil to air."),
            ("Wed 18:00", "Ask an Astronomer", "Forty minutes of your questions, answered live. Nothing is too simple or too strange."),
        ];
        foreach ((string when, string title, string about) in shows)
        {
            canvas.SetFillColor(new PdfColor(kit.Gold, [1f]));
            canvas.Rectangle(Mm(12), y - Mm(1.5), Mm(1.2), Mm(15));
            canvas.Fill();
            canvas.SetFillColor(Cmyk(0.6, 0.3, 0, 0.3));
            Text(canvas, kit.SansBold, 7.5, Mm(16), y + Mm(10), when.ToUpperInvariant());
            canvas.SetFillColor(Cmyk(0.85, 0.65, 0.2, 0.55));
            Text(canvas, kit.SansBold, 12, Mm(16), y + Mm(5), title);
            canvas.SetFillColor(Gray(0.25));
            Paragraph(canvas, kit.Sans, 8.5, Mm(16), y, Width - Mm(48), 10.5, about);
            y -= Mm(22);
        }

        // Ticket prices on an axial shading strip, knocked out of a group so overlapping rows do not double their alpha.
        PdfFormXObject group = document.CreateFormXObject(new PdfRectangle(0, 0, Width, Mm(30)));
        PdfTransparencyGroup transparency = group.CreateTransparencyGroup();
        transparency.IsKnockout = true;
        using (PdfCanvas inner = group.CreateCanvas())
        {
            inner.SetFillAlpha(0.5);
            for (int i = 0; i < 3; i++)
            {
                inner.SetFillColor(i == 1 ? new PdfColor(kit.Gold, [1f]) : Cmyk(0.9, 0.55, 0, 0.1));
                RoundedRectangle(inner, Mm(12) + (i * Mm(33)), Mm(4), Mm(36), Mm(20), Mm(3));
                inner.Fill();
            }
        }

        canvas.DrawForm(group);
        canvas.SetFillColor(Gray(0.05));
        string[] prices = ["Adult  £9", "Child  £5", "Family  £24"];
        for (int i = 0; i < 3; i++)
        {
            Text(canvas, kit.Display, 17, Mm(17) + (i * Mm(33)), Mm(12), prices[i]);
        }

        canvas.RestoreState();
        CropMarks(kit, canvas);
    }

    // ---- Page 3: highlights and blend modes ----------------------------------------------------------------------------------

    private static void Highlights(Kit kit, PdfPage page, PdfImage aldrin, PdfImage astronaut)
    {
        PdfDocument document = kit.Document;
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SaveState();
        canvas.Translate(Slug, Slug);

        canvas.SetFillColor(Rgb(0.06, 0.07, 0.16));
        BleedRectangle(canvas);
        canvas.Fill();

        // A photograph in a rotated frame bleeding off the top.
        canvas.SaveState();
        canvas.Translate(Mm(4), Height - Mm(80));
        canvas.Rotate(-6);
        canvas.SetFillColor(Gray(1));
        canvas.Rectangle(-Mm(2), -Mm(2), Mm(84), Mm(88));
        canvas.Fill();
        canvas.DrawImage(aldrin, 0, Mm(4), Mm(80), Mm(80));
        canvas.SetFillColor(Gray(0.2));
        Text(canvas, kit.Sans, 7, Mm(1), Mm(0.5), "Moonwalkers: the Apollo 11 landing site (NASA)");
        canvas.RestoreState();

        // The astronaut cut-out (an image with an alpha channel, so an SMask) drifting over the frame.
        canvas.SaveState();
        canvas.Translate(Mm(70), Height - Mm(110));
        canvas.Rotate(10);
        canvas.DrawImage(astronaut, 0, 0, Mm(68), Mm(68));
        canvas.RestoreState();

        canvas.SetFillColor(new PdfColor(kit.Gold, [1f]));
        Text(canvas, kit.Display, 30, Mm(12), Mm(104), "SIXTEEN WAYS TO MIX LIGHT");
        canvas.SetFillColor(Gray(0.85));
        Paragraph(canvas, kit.Sans, 8.5, Mm(12), Mm(96), Width - Mm(24), 10.5,
            "Our new projector blends sixteen layers of light in real time. Each disc below is a gold circle over a sky-blue square, " +
            "drawn with one of the sixteen blend modes a PDF file can use.");

        // Every blend mode, four by four.
        BlendMode[] modes = Enum.GetValues<BlendMode>();
        double cell = Mm(30);
        for (int i = 0; i < modes.Length; i++)
        {
            double x = Mm(12) + ((i % 4) * cell);
            double y = Mm(66) - ((i / 4) * Mm(15));
            canvas.SetFillColor(Rgb(0.25, 0.55, 0.85));
            canvas.Rectangle(x, y, Mm(14), Mm(11));
            canvas.Fill();
            canvas.SaveState();
            canvas.SetBlendMode(modes[i]);
            canvas.SetFillColor(Rgb(0.95, 0.7, 0.15));
            Circle(canvas, x + Mm(14), y + Mm(5.5), Mm(5.5));
            canvas.Fill();
            canvas.RestoreState();
            canvas.SetFillColor(Gray(0.8));
            Text(canvas, kit.Sans, 5.5, x, y - Mm(3), modes[i].ToString());
        }

        // A soft-masked caption panel: an alpha mask made of strips of falling opacity fades it from left to right.
        PdfFormXObject maskGroup = document.CreateFormXObject(new PdfRectangle(-Bleed, -Bleed, Width + Bleed, Height + Bleed));
        maskGroup.CreateTransparencyGroup();
        using (PdfCanvas mask = maskGroup.CreateCanvas())
        {
            mask.SetFillColor(Gray(0));
            for (int i = 0; i < 24; i++)
            {
                mask.SetFillAlpha(1 - (i / 24.0));
                mask.Rectangle(-Bleed + (i * (Width + (2 * Bleed)) / 24), -Bleed, ((Width + (2 * Bleed)) / 24) + 0.5, Mm(16));
                mask.Fill();
            }
        }

        canvas.SaveState();
        canvas.SetSoftMask(document.CreateSoftMask(PdfSoftMaskType.Alpha, maskGroup));
        canvas.SetFillColor(Cmyk(0.75, 0.9, 0, 0.2));
        canvas.Rectangle(-Bleed, Mm(-3), Width + (2 * Bleed), Mm(13));
        canvas.Fill();
        canvas.RestoreState();
        canvas.SetFillColor(Gray(1));
        Text(canvas, kit.SansBold, 9, Mm(12), Mm(3), "Book shows online or at the door · members see every show free");

        canvas.RestoreState();
        CropMarks(kit, canvas);
    }

    // ---- Page 4: the back cover ----------------------------------------------------------------------------------------------

    private static void Back(Kit kit, PdfPage page)
    {
        PdfDocument document = kit.Document;
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SaveState();
        canvas.Translate(Slug, Slug);

        // A Coons patch mesh to the bleed, like an aurora.
        var mesh = new PdfPatchMeshBuilder(PdfDeviceRgbColorSpace.Instance, tensorProduct: false);
        double l = -Bleed;
        double r = Width + Bleed;
        double b = -Bleed;
        double t = Height + Bleed;
        double my = Height * 0.55;
        mesh.AddPatch(
            [
                new PathPoint(l, b), new PathPoint(l, b + ((my - b) / 3)), new PathPoint(l, my - ((my - b) / 3)), new PathPoint(l, my),
                new PathPoint(l + ((r - l) / 3), my + Mm(30)), new PathPoint(r - ((r - l) / 3), my - Mm(40)), new PathPoint(r, my),
                new PathPoint(r, my - ((my - b) / 3)), new PathPoint(r, b + ((my - b) / 3)), new PathPoint(r, b),
                new PathPoint(r - ((r - l) / 3), b), new PathPoint(l + ((r - l) / 3), b),
            ],
            [Rgb(0.02, 0.04, 0.12), Rgb(0.1, 0.55, 0.45), Rgb(0.2, 0.25, 0.6), Rgb(0.03, 0.03, 0.1)]);
        mesh.AddPatch(
            [
                new PathPoint(l, my), new PathPoint(l, my + ((t - my) / 3)), new PathPoint(l, t - ((t - my) / 3)), new PathPoint(l, t),
                new PathPoint(l + ((r - l) / 3), t), new PathPoint(r - ((r - l) / 3), t), new PathPoint(r, t),
                new PathPoint(r, t - ((t - my) / 3)), new PathPoint(r, my + ((t - my) / 3)), new PathPoint(r, my),
                new PathPoint(r - ((r - l) / 3), my - Mm(40)), new PathPoint(l + ((r - l) / 3), my + Mm(30)),
            ],
            [Rgb(0.1, 0.55, 0.45), Rgb(0.05, 0.05, 0.15), Rgb(0.15, 0.1, 0.3), Rgb(0.2, 0.25, 0.6)]);
        canvas.SaveState();
        BleedRectangle(canvas);
        canvas.Clip();
        canvas.EndPath();
        canvas.PaintShading(document.CreateMeshShading(mesh));
        canvas.RestoreState();

        canvas.SetFillColor(new PdfColor(kit.Gold, [1f]));
        Text(canvas, kit.Display, 36, Mm(12), Height - Mm(30), "VISIT US");
        canvas.SetFillColor(Gray(1));
        string[] lines =
        [
            "Lumen Planetarium",
            "Observatory Hill, an invented address",
            "Open Tuesday to Sunday, 10:00 to 18:00",
            "Late nights every Friday until 22:30",
        ];
        double y = Height - Mm(42);
        foreach (string line in lines)
        {
            Text(canvas, kit.Sans, 10, Mm(12), y, line);
            y -= 14;
        }

        // A simple map: streets as stroked paths with round joins, the building as a rotated square, a dashed walking route.
        canvas.SaveState();
        canvas.Translate(Mm(12), Mm(40));
        canvas.SetStrokeColor(Rgb(0.8, 0.85, 0.95));
        canvas.SetLineWidth(5);
        canvas.SetLineCap(LineCap.Round);
        canvas.SetLineJoin(LineJoin.Round);
        canvas.SetStrokeAlpha(0.6);
        canvas.MoveTo(0, Mm(20));
        canvas.CurveTo(Mm(30), Mm(30), Mm(60), Mm(5), Mm(120), Mm(18));
        canvas.MoveTo(Mm(40), 0);
        canvas.LineTo(Mm(48), Mm(60));
        canvas.Stroke();
        canvas.SetStrokeAlpha(1);
        canvas.SetStrokeColor(new PdfColor(kit.Gold, [1f]));
        canvas.SetLineWidth(1.4);
        canvas.SetDashPattern([4, 3], 0);
        canvas.MoveTo(Mm(4), Mm(21));
        canvas.CurveTo(Mm(20), Mm(40), Mm(35), Mm(48), Mm(46), Mm(50));
        canvas.Stroke();
        canvas.SetDashPattern([], 0);
        canvas.Translate(Mm(48), Mm(52));
        canvas.Rotate(45);
        canvas.SetFillColor(new PdfColor(kit.Gold, [1f]));
        canvas.Rectangle(-Mm(4), -Mm(4), Mm(8), Mm(8));
        canvas.Fill();
        canvas.RestoreState();
        canvas.SetFillColor(Gray(0.9));
        Text(canvas, kit.Sans, 8, Mm(66), Mm(92), "Lumen");
        Text(canvas, kit.Sans, 8, Mm(14), Mm(66), "Station");

        canvas.SetFillColor(Gray(0.75));
        Paragraph(canvas, kit.Light, 7, Mm(12), Mm(18), Width - Mm(24), 9,
            "Lumen Planetarium is invented for a Broadside sample document. Photographs: NASA, public domain. Printed in CMYK plus one spot " +
            "colour, Lumen Gold, on 170 g silk with a 3 mm bleed.");

        canvas.RestoreState();
        CropMarks(kit, canvas);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------------------

    private static void BleedRectangle(PdfCanvas canvas) => canvas.Rectangle(-Bleed, -Bleed, Width + (2 * Bleed), Height + (2 * Bleed));

    private static void CropMarks(Kit kit, PdfCanvas canvas)
    {
        canvas.SaveState();
        canvas.SetStrokeColor(new PdfColor(kit.Registration, [1f]));
        canvas.SetLineWidth(0.25);
        double gap = Bleed + Mm(1);
        double length = Mm(5);
        foreach (double x in new[] { Slug, Slug + Width })
        {
            foreach (double y in new[] { Slug, Slug + Height })
            {
                double dx = x == Slug ? -1 : 1;
                double dy = y == Slug ? -1 : 1;
                canvas.MoveTo(x + (dx * gap), y);
                canvas.LineTo(x + (dx * (gap + length)), y);
                canvas.MoveTo(x, y + (dy * gap));
                canvas.LineTo(x, y + (dy * (gap + length)));
            }
        }

        canvas.Stroke();
        canvas.RestoreState();
    }

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text, kerning: true);
        canvas.EndText();
    }

    private static void CenteredText(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text) =>
        Text(canvas, font, size, x - (font.MeasureWidth(text, size) / 2), y, text);

    private static void Paragraph(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, double width, double leading, string text)
    {
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureWidth(candidate, size) > width && line.Length > 0)
            {
                Text(canvas, font, size, x, y, line.ToString());
                y -= leading;
                line.Clear().Append(word);
            }
            else
            {
                line.Clear().Append(candidate);
            }
        }

        if (line.Length > 0)
        {
            Text(canvas, font, size, x, y, line.ToString());
        }
    }

    private static void Circle(PdfCanvas canvas, double cx, double cy, double r)
    {
        const double k = 0.5523;
        canvas.MoveTo(cx + r, cy);
        canvas.CurveTo(cx + r, cy + (k * r), cx + (k * r), cy + r, cx, cy + r);
        canvas.CurveTo(cx - (k * r), cy + r, cx - r, cy + (k * r), cx - r, cy);
        canvas.CurveTo(cx - r, cy - (k * r), cx - (k * r), cy - r, cx, cy - r);
        canvas.CurveTo(cx + (k * r), cy - r, cx + r, cy - (k * r), cx + r, cy);
        canvas.ClosePath();
    }

    private static void RoundedRectangle(PdfCanvas canvas, double x, double y, double w, double h, double r)
    {
        const double k = 0.5523;
        canvas.MoveTo(x + r, y);
        canvas.LineTo(x + w - r, y);
        canvas.CurveTo(x + w - r + (k * r), y, x + w, y + r - (k * r), x + w, y + r);
        canvas.LineTo(x + w, y + h - r);
        canvas.CurveTo(x + w, y + h - r + (k * r), x + w - r + (k * r), y + h, x + w - r, y + h);
        canvas.LineTo(x + r, y + h);
        canvas.CurveTo(x + r - (k * r), y + h, x, y + h - r + (k * r), x, y + h - r);
        canvas.LineTo(x, y + r);
        canvas.CurveTo(x, y + r - (k * r), x + r - (k * r), y, x + r, y);
        canvas.ClosePath();
    }

    private static void Star(PdfCanvas canvas, double cx, double cy, double outer, double inner, int points)
    {
        for (int i = 0; i < points * 2; i++)
        {
            double angle = Math.PI * i / points;
            double radius = i % 2 == 0 ? outer : inner;
            double x = cx + (radius * Math.Sin(angle));
            double y = cy + (radius * Math.Cos(angle));
            if (i == 0)
            {
                canvas.MoveTo(x, y);
            }
            else
            {
                canvas.LineTo(x, y);
            }
        }

        canvas.ClosePath();
    }
}
