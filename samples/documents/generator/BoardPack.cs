using System.Globalization;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A board meeting pack, assembled the way an office assembles one: new pages of every common size (US Letter, A4, A3 landscape,
/// US Legal, a till receipt, a poster with <c>UserUnit</c>) and pages taken from other samples, some rotated with <c>/Rotate</c>
/// (90, 180, 270), one with a MediaBox whose origin is not zero and a smaller CropBox. Page labels and an outline name each item.
/// </summary>
internal static class BoardPack
{
    public static void Write(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Board meeting pack, June 2027", "Mixed page sizes, rotated pages and pages assembled from other files.");
        PdfCanvasFont regular = context.Font(document, "firasans/FiraSans-Regular.ttf");
        PdfCanvasFont bold = context.Font(document, "firasans/FiraSans-Bold.ttf");
        PdfCanvasFont mono = context.Font(document, "ibmplexmono/IBMPlexMono-Regular.ttf");
        PdfColor ink = Rgb(0.1, 0.16, 0.22);
        PdfColor blue = Rgb(0.09, 0.31, 0.39);
        var items = new List<(int Page, string Title)>();

        // 1. Cover, US Letter.
        PdfPage cover = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        using (PdfCanvas canvas = cover.CreateCanvas())
        {
            canvas.SetFillColor(blue);
            canvas.Rectangle(0, 520, 612, 272);
            canvas.Fill();
            canvas.SetFillColor(Gray(1));
            Text(canvas, bold, 30, 54, 700, "Board meeting pack");
            Text(canvas, regular, 14, 54, 672, "Riverside Water Authority · June 17, 2027");
            canvas.SetFillColor(ink);
            string[] contents =
            [
                "1  Agenda (A4)",
                "2  Invoice for approval (A4, from the invoicing system)",
                "3  Capital budget 2027–2030 (A3 landscape)",
                "4  Gauge readings (a landscape table on a rotated page)",
                "5  Minutes of May 20 (filed upside down: rotated 180°)",
                "6  Site plan, Elm Street (US Legal, rotated 270°)",
                "7  Expenses receipt (till roll)",
                "8  Proof of the notice (offset media box, cropped)",
                "9  Poster for the open day (36 × 24 in, UserUnit 2)",
            ];
            double y = 470;
            Text(canvas, bold, 13, 54, y + 26, "Contents");
            foreach (string line in contents)
            {
                Text(canvas, regular, 12, 54, y, line);
                y -= 20;
            }

            Text(canvas, regular, 8, 54, 40, "The authority and its business are invented for the Broadside sample documents.");
        }

        items.Add((0, "Cover"));

        // 2. Agenda, A4.
        PdfPage agenda = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        using (PdfCanvas canvas = agenda.CreateCanvas())
        {
            canvas.SetFillColor(ink);
            Text(canvas, bold, 18, Mm(25), Mm(260), "Agenda");
            string[] lines =
            [
                "10:00  Apologies and declarations of interest",
                "10:05  Minutes of the meeting of May 20",
                "10:15  Invoice HF/27/0318 for approval",
                "10:30  Capital budget 2027–2030",
                "11:15  Gauge calibration: findings and actions",
                "11:30  Elm Street water main: programme and notices",
                "11:50  Open day arrangements",
                "12:00  Any other business",
            ];
            double y = Mm(240);
            foreach (string line in lines)
            {
                Text(canvas, regular, 11.5, Mm(25), y, line);
                y -= 22;
            }
        }

        items.Add((1, "1  Agenda"));

        // 3. Two pages from the invoice sample (A4), as the invoicing system printed them.
        using (PdfDocument invoice = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("invoice-conservative.pdf"))))
        {
            document.AppendPages(invoice, 0..2);
        }

        items.Add((2, "2  Invoice for approval"));

        // 4. A3 landscape budget.
        PdfPage budget = document.Pages.Add(new PdfRectangle(0, 0, Mm(420), Mm(297)));
        using (PdfCanvas canvas = budget.CreateCanvas())
        {
            canvas.SetFillColor(ink);
            Text(canvas, bold, 18, Mm(20), Mm(275), "Capital budget 2027–2030 (thousands of dollars)");
            string[] columns = ["Project", "2027", "2028", "2029", "2030", "Total", "Funding"];
            string[][] rows =
            [
                ["Elm Street water main", "1,240", "310", "", "", "1,550", "Bond"],
                ["Weir gauge replacement", "86", "", "", "", "86", "Reserves"],
                ["Treatment works roof", "", "420", "", "", "420", "Reserves"],
                ["Reservoir 2 relining", "", "650", "1,900", "400", "2,950", "Bond"],
                ["Meter upgrade (phase 1)", "300", "300", "300", "300", "1,200", "Rates"],
                ["Pump station 4 controls", "120", "40", "", "", "160", "Rates"],
                ["Riverside footbridge", "", "", "280", "", "280", "Grant"],
                ["Fleet replacement", "95", "110", "80", "120", "405", "Rates"],
                ["Total", "1,841", "1,830", "2,560", "820", "7,051", ""],
            ];
            double[] x = [Mm(20), Mm(150), Mm(185), Mm(220), Mm(255), Mm(290), Mm(330)];
            double y = Mm(255);
            canvas.SetFillColor(Rgb(0.9, 0.94, 0.96));
            canvas.Rectangle(Mm(18), y - 6, Mm(384), 20);
            canvas.Fill();
            canvas.SetFillColor(ink);
            for (int c = 0; c < columns.Length; c++)
            {
                Text(canvas, bold, 11, x[c], y, columns[c]);
            }

            foreach (string[] row in rows)
            {
                y -= 24;
                for (int c = 0; c < row.Length; c++)
                {
                    Text(canvas, row[0] == "Total" ? bold : (c is > 0 and < 6 ? mono : regular), 11, x[c], y, row[c]);
                }

                canvas.SetStrokeColor(Gray(0.8));
                canvas.SetLineWidth(0.5);
                canvas.MoveTo(Mm(18), y - 8);
                canvas.LineTo(Mm(402), y - 8);
                canvas.Stroke();
            }
        }

        items.Add((4, "3  Capital budget"));

        // 5. A landscape table on a portrait page turned with /Rotate 90: the content is drawn turned a quarter, so it reads upright.
        PdfPage turned = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        using (PdfCanvas canvas = turned.CreateCanvas())
        {
            canvas.Transform(new Matrix(0, 1, -1, 0, 612, 0));
            canvas.SetFillColor(ink);
            Text(canvas, bold, 16, 54, 560, "Gauge readings, May 2027 (mm)");
            double y = 520;
            Text(canvas, mono, 10, 54, y, "Day   G1     G2     G3     Staff   Notes");
            for (int day = 1; day <= 24; day++)
            {
                y -= 18;
                int g1 = 600 + (int)(40 * Math.Sin(day / 4.0));
                Text(canvas, mono, 10, 54, y, string.Create(CultureInfo.InvariantCulture,
                    $"{day,3}   {g1,4}   {g1 - 3,4}   {g1 + (day > 13 ? 1 : 24),4}   {g1 - 1,5}   {(day == 14 ? "G3 re-zeroed" : "")}"));
            }
        }

        turned.Rotation = 90;
        items.Add((5, "4  Gauge readings"));

        // 6. Minutes, filed upside down: the content is drawn turned 180 degrees and /Rotate 180 puts it right, as a scanner that
        // fed the sheet the wrong way round and a clerk who fixed it in the viewer would leave it.
        PdfPage minutes = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        using (PdfCanvas canvas = minutes.CreateCanvas())
        {
            canvas.Transform(new Matrix(-1, 0, 0, -1, Mm(210), Mm(297)));
            canvas.SetFillColor(ink);
            Text(canvas, bold, 16, Mm(25), Mm(265), "Minutes of the meeting of May 20, 2027");
            string[] lines =
            [
                "Present: five board members, the general manager and the clerk.",
                "1. The minutes of April 15 were approved without amendment.",
                "2. The board noted the gauge calibration memo and its three findings.",
                "3. The Elm Street programme was approved, subject to notices in three languages.",
                "4. The open day will be held on September 11; volunteers to contact the clerk.",
                "The meeting closed at 12:05.",
            ];
            double y = Mm(245);
            foreach (string line in lines)
            {
                Text(canvas, regular, 11, Mm(25), y, line);
                y -= 20;
            }
        }

        minutes.Rotation = 180;
        items.Add((6, "5  Minutes for noting"));

        // 7. US Legal, stored with /Rotate 270: a site plan drawn across the long side.
        PdfPage legal = document.Pages.Add(new PdfRectangle(0, 0, 612, 1008));
        using (PdfCanvas canvas = legal.CreateCanvas())
        {
            canvas.Transform(new Matrix(0, -1, 1, 0, 0, 1008));
            canvas.SetFillColor(ink);
            Text(canvas, bold, 16, 40, 570, "Site plan: Elm Street water main, 3rd to 6th Avenue");
            canvas.SetStrokeColor(Gray(0.6));
            canvas.SetLineWidth(14);
            canvas.MoveTo(40, 300);
            canvas.LineTo(968, 300);
            canvas.Stroke();
            for (int avenue = 0; avenue < 4; avenue++)
            {
                double ax = 120 + (avenue * 260);
                canvas.MoveTo(ax, 80);
                canvas.LineTo(ax, 520);
                canvas.Stroke();
                Text(canvas, regular, 11, ax - 30, 530, $"{avenue + 3}{(avenue == 0 ? "rd" : "th")} Avenue");
            }

            canvas.SetStrokeColor(Rgb(0.1, 0.4, 0.85));
            canvas.SetLineWidth(3);
            canvas.SetDashPattern([10, 5], 0);
            canvas.MoveTo(120, 292);
            canvas.LineTo(900, 292);
            canvas.Stroke();
            canvas.SetDashPattern([], 0);
            Text(canvas, regular, 10, 130, 270, "New 300 mm main (dashed), laid in sections of about 90 m");
            Text(canvas, regular, 10, 40, 60, "North is up. Not to scale. For the board's information only.");
        }

        legal.Rotation = 270;
        items.Add((7, "6  Site plan"));

        // 8. A till receipt, 80 mm wide.
        PdfPage receipt = document.Pages.Add(new PdfRectangle(0, 0, Mm(80), Mm(160)));
        using (PdfCanvas canvas = receipt.CreateCanvas())
        {
            canvas.SetFillColor(Gray(0.1));
            string[] lines =
            [
                "  RIVERSIDE HARDWARE",
                "  410 Elm Street",
                "",
                "06/03/27        14:22",
                "--------------------",
                "Marker paint x2  15.98",
                "Survey stakes    12.40",
                "Batteries AA      8.99",
                "--------------------",
                "TOTAL           37.37",
                "CARD            37.37",
                "",
                "  THANK YOU",
            ];
            double y = Mm(150);
            foreach (string line in lines)
            {
                Text(canvas, mono, 8, Mm(5), y, line);
                y -= 13;
            }
        }

        items.Add((8, "7  Expenses receipt"));

        // 9. A proof with its media box away from the origin and a smaller crop box (the printer's marks lie outside it).
        PdfPage proof = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        proof.MediaBox = new PdfRectangle(100, 100, 100 + Mm(230), 100 + Mm(317));
        proof.CropBox = new PdfRectangle(100 + Mm(10), 100 + Mm(10), 100 + Mm(220), 100 + Mm(307));
        using (PdfCanvas canvas = proof.CreateCanvas())
        {
            double left = 100 + Mm(10), bottom = 100 + Mm(10), right = 100 + Mm(220), top = 100 + Mm(307);
            canvas.SetStrokeColor(Gray(0));
            canvas.SetLineWidth(0.3);
            foreach ((double cx, double cy, double dx, double dy) in new[] { (left, bottom, -1.0, -1.0), (right, bottom, 1.0, -1.0), (left, top, -1.0, 1.0), (right, top, 1.0, 1.0) })
            {
                canvas.MoveTo(cx + (dx * 3), cy);
                canvas.LineTo(cx + (dx * 24), cy);
                canvas.MoveTo(cx, cy + (dy * 3));
                canvas.LineTo(cx, cy + (dy * 24));
            }

            canvas.Stroke();
            canvas.SetFillColor(blue);
            canvas.Rectangle(left, top - Mm(40), right - left, Mm(40));
            canvas.Fill();
            canvas.SetFillColor(Gray(1));
            Text(canvas, bold, 22, left + Mm(12), top - Mm(24), "Notice of water main replacement");
            canvas.SetFillColor(ink);
            Text(canvas, regular, 12, left + Mm(12), top - Mm(60), "PROOF for approval: see the trilingual notice for the full text.");
            Text(canvas, regular, 8, left + Mm(2), bottom - Mm(7), "Proof 2 · media box from (100, 100) · crop marks outside the crop box");
        }

        items.Add((9, "8  Proof of the notice"));

        // 10. A 36 × 24 in poster: the media box is in units of 2/72 in (UserUnit 2), beyond the 200 in limit of a plain page.
        PdfPage poster = document.Pages.Add(new PdfRectangle(0, 0, 36 * 36, 24 * 36));
        poster.Dictionary[new CosName("UserUnit")] = new CosReal(2);
        using (PdfCanvas canvas = poster.CreateCanvas())
        {
            canvas.SetFillColor(Rgb(0.62, 0.86, 0.95));
            canvas.Rectangle(0, 0, 36 * 36, 24 * 36);
            canvas.Fill();
            canvas.SetFillColor(blue);
            Text(canvas, bold, 90, 80, 600, "Open day");
            Text(canvas, regular, 40, 84, 520, "Riverside treatment works · Saturday, September 11, 2027 · 10 to 4");
            Text(canvas, regular, 24, 84, 460, "Tours of the works every half hour · see the weir gauges · free entry");
        }

        items.Add((10, "9  Open day poster"));

        PdfPageLabels labels = document.CreatePageLabels();
        labels.SetRange(0, PdfPageLabelStyle.None, prefix: "Cover");
        for (int i = 1; i < items.Count; i++)
        {
            labels.SetRange(items[i].Page, PdfPageLabelStyle.Arabic, prefix: $"{i}-");
        }

        PdfOutline outline = document.CreateOutline();
        foreach ((int page, string title) in items)
        {
            outline.Add(title).Destination = document.CreateDestination(document.Pages[page], PdfDestinationView.Fit);
        }

        document.Save(context.OutputPath("board-pack.pdf"));
    }

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
