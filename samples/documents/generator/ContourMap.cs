using System.Globalization;
using Broadside.Fonts;
using Broadside.Graphics;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// One huge vector page: an A1 topographic sheet of invented terrain. The heights come from deterministic fractal noise; the
/// contours (every 2 m, index contours every 10 m) are traced by marching squares over a fine grid and chained into long paths,
/// several hundred thousand segments in all, under a grid, spot heights, a title block and a scale bar. Contours, grid
/// and labels are separate optional content groups. The deep-zoom and huge-path sample.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1814", Justification = "The height grid is rectangular.")]
internal static class ContourMap
{
    private const int Nx = 1000;
    private const int Ny = 700;
    private const double Interval = 2;

    public static void Write(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Ashcombe Hills 1:25,000 (invented terrain)", "A topographic sheet: hundreds of thousands of contour segments on one A1 page.");
        PdfCanvasFont regular = context.Font(document, "ptsans/PT_Sans-Web-Regular.ttf");
        PdfCanvasFont bold = context.Font(document, "ptsans/PT_Sans-Web-Bold.ttf");
        PdfOptionalContentGroup contoursLayer = document.CreateOptionalContentGroup("Contours");
        PdfOptionalContentGroup gridLayer = document.CreateOptionalContentGroup("Grid");
        PdfOptionalContentGroup labelsLayer = document.CreateOptionalContentGroup("Spot heights and names");

        double width = Mm(841), height = Mm(594);
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, width, height));
        double left = Mm(20), bottom = Mm(20), mapWidth = Mm(700), mapHeight = Mm(490);
        double sx = mapWidth / (Nx - 1), sy = mapHeight / (Ny - 1);

        double[,] h = new double[Nx, Ny];
        double low = double.MaxValue, high = double.MinValue;
        for (int i = 0; i < Nx; i++)
        {
            for (int j = 0; j < Ny; j++)
            {
                double v = Height(i / 260.0, j / 260.0);
                h[i, j] = v;
                low = Math.Min(low, v);
                high = Math.Max(high, v);
            }
        }

        PdfCanvas canvas = page.CreateCanvas();
        canvas.SetFillColor(Rgb(0.98, 0.97, 0.93));
        canvas.Rectangle(left, bottom, mapWidth, mapHeight);
        canvas.Fill();

        // Contours, one path per level, chained.
        canvas.BeginOptionalContent(contoursLayer);
        canvas.SetLineJoin(LineJoin.Round);
        canvas.SetLineCap(LineCap.Round);
        int segments = 0;
        for (double level = Math.Ceiling(low / Interval) * Interval; level < high; level += Interval)
        {
            bool index = Math.Abs(level % 10) < 0.001;
            canvas.SetStrokeColor(index ? Rgb(0.55, 0.32, 0.12) : Rgb(0.78, 0.55, 0.35));
            canvas.SetLineWidth(index ? 0.7 : 0.3);
            foreach (List<(double X, double Y)> line in Trace(h, level))
            {
                canvas.MoveTo(left + (line[0].X * sx), bottom + (line[0].Y * sy));
                for (int k = 1; k < line.Count; k++)
                {
                    canvas.LineTo(left + (line[k].X * sx), bottom + (line[k].Y * sy));
                }

                segments += line.Count - 1;
            }

            canvas.Stroke();
        }

        canvas.EndMarkedContent();

        // Grid every kilometer (40 mm at 1:25,000).
        canvas.BeginOptionalContent(gridLayer);
        canvas.SetStrokeColor(Rgb(0.25, 0.45, 0.7));
        canvas.SetLineWidth(0.25);
        for (double x = left; x <= left + mapWidth + 0.1; x += Mm(40))
        {
            canvas.MoveTo(x, bottom);
            canvas.LineTo(x, bottom + mapHeight);
        }

        for (double y = bottom; y <= bottom + mapHeight + 0.1; y += Mm(40))
        {
            canvas.MoveTo(left, y);
            canvas.LineTo(left + mapWidth, y);
        }

        canvas.Stroke();
        canvas.EndMarkedContent();

        // Spot heights at local maxima.
        canvas.BeginOptionalContent(labelsLayer);
        canvas.SetFillColor(Gray(0.1));
        for (int i = 10; i < Nx - 10; i += 5)
        {
            for (int j = 10; j < Ny - 10; j += 5)
            {
                bool peak = true;
                for (int di = -10; di <= 10 && peak; di += 2)
                {
                    for (int dj = -10; dj <= 10 && peak; dj += 2)
                    {
                        peak = h[i + di, j + dj] <= h[i, j];
                    }
                }

                if (peak)
                {
                    double x = left + (i * sx), y = bottom + (j * sy);
                    canvas.Rectangle(x - 1, y - 1, 2, 2);
                    canvas.Fill();
                    Text(canvas, regular, 6, x + 2.5, y - 2, h[i, j].ToString("0", CultureInfo.InvariantCulture));
                }
            }
        }

        canvas.EndMarkedContent();

        // Neat line, title block, scale bar.
        canvas.SetStrokeColor(Gray(0));
        canvas.SetLineWidth(1.2);
        canvas.Rectangle(left, bottom, mapWidth, mapHeight);
        canvas.Stroke();
        double tx = left + mapWidth + Mm(10);
        canvas.SetFillColor(Gray(0.1));
        Text(canvas, bold, 28, tx, height - Mm(40), "Ashcombe Hills");
        Text(canvas, regular, 12, tx, height - Mm(50), "Sheet 7 · 1:25,000");
        Text(canvas, regular, 9, tx, height - Mm(62), "Contour interval 2 m; index contours every 10 m.");
        Text(canvas, regular, 9, tx, height - Mm(68), string.Create(CultureInfo.InvariantCulture, $"{segments:N0} contour segments."));
        Text(canvas, regular, 9, tx, height - Mm(74), "Grid squares are 1 km.");
        Text(canvas, regular, 8, tx, bottom + Mm(4), "Invented terrain, for the Broadside sample documents.");
        for (int k = 0; k < 4; k++)
        {
            canvas.SetFillColor(k % 2 == 0 ? Gray(0.1) : Gray(1));
            canvas.Rectangle(tx + (k * Mm(20)), bottom + Mm(20), Mm(20), Mm(3));
            canvas.FillAndStroke();
        }

        canvas.SetFillColor(Gray(0.1));
        Text(canvas, regular, 8, tx, bottom + Mm(14), "0");
        Text(canvas, regular, 8, tx + Mm(78), bottom + Mm(14), "2 km");
        canvas.Dispose();

        document.Save(context.OutputPath("contour-map.pdf"));
    }

    // Fractal value noise: six octaves of smoothed lattice noise, scaled to heights of roughly 40 to 420 m.
    private static double Height(double x, double y)
    {
        double sum = 0, amplitude = 1, frequency = 1, norm = 0;
        for (int octave = 0; octave < 6; octave++)
        {
            sum += amplitude * Noise(x * frequency, y * frequency, octave);
            norm += amplitude;
            amplitude *= 0.5;
            frequency *= 2.03;
        }

        return 40 + (380 * (sum / norm));
    }

    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double u = fx * fx * (3 - (2 * fx)), v = fy * fy * (3 - (2 * fy));
        double a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed), c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
        return (a * (1 - u) * (1 - v)) + (b * u * (1 - v)) + (c * (1 - u) * v) + (d * u * v);
    }

    private static double Lattice(int x, int y, int seed)
    {
        unchecked
        {
            uint n = (uint)((x * 374761393) + (y * 668265263) + (seed * 1442695041));
            n = (n ^ (n >> 13)) * 1274126177;
            n ^= n >> 16;
            return (n & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    /// <summary>Marching squares at one level, the crossings chained into polylines through the grid edges they share.</summary>
    private static List<List<(double X, double Y)>> Trace(double[,] h, double level)
    {
        // An edge key: horizontal edges (i, j)-(i+1, j) are 2 * index, vertical edges (i, j)-(i, j+1) are 2 * index + 1.
        long Key(int i, int j, bool vertical) => ((((long)j * Nx) + i) * 2) + (vertical ? 1 : 0);
        var points = new Dictionary<long, (double X, double Y)>();
        var links = new Dictionary<long, List<long>>();
        (double, double) Cross(int i, int j, bool vertical)
        {
            double a = h[i, j], b = vertical ? h[i, j + 1] : h[i + 1, j];
            double t = (level - a) / (b - a);
            return vertical ? (i, j + t) : (i + t, j);
        }

        void Link(long p, long q)
        {
            if (!links.TryGetValue(p, out List<long>? lp))
            {
                links[p] = lp = [];
            }

            if (!links.TryGetValue(q, out List<long>? lq))
            {
                links[q] = lq = [];
            }

            lp.Add(q);
            lq.Add(p);
        }

        for (int i = 0; i < Nx - 1; i++)
        {
            for (int j = 0; j < Ny - 1; j++)
            {
                var edges = new List<long>(4);
                (int I, int J, bool V)[] sides = [(i, j, false), (i + 1, j, true), (i, j + 1, false), (i, j, true)];
                foreach ((int si, int sj, bool sv) in sides)
                {
                    double a = h[si, sj], b = sv ? h[si, sj + 1] : h[si + 1, sj];
                    if ((a < level) != (b < level))
                    {
                        long key = Key(si, sj, sv);
                        if (!points.ContainsKey(key))
                        {
                            points[key] = Cross(si, sj, sv);
                        }

                        edges.Add(key);
                    }
                }

                if (edges.Count == 2)
                {
                    Link(edges[0], edges[1]);
                }
                else if (edges.Count == 4)
                {
                    Link(edges[0], edges[1]);
                    Link(edges[2], edges[3]);
                }
            }
        }

        var lines = new List<List<(double X, double Y)>>();
        var used = new HashSet<long>();
        // Start from ends (one link) first, then close the loops.
        foreach (long start in links.Where(static e => e.Value.Count == 1).Select(static e => e.Key).Concat(links.Keys).ToList())
        {
            if (used.Contains(start))
            {
                continue;
            }

            var line = new List<(double X, double Y)>();
            long current = start;
            long previous = -1;
            while (true)
            {
                used.Add(current);
                line.Add(points[current]);
                long next = -1;
                foreach (long candidate in links[current])
                {
                    if (candidate != previous && !used.Contains(candidate))
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next < 0)
                {
                    if (links[current].Contains(start) && line.Count > 2)
                    {
                        line.Add(points[start]);
                    }

                    break;
                }

                previous = current;
                current = next;
            }

            if (line.Count > 1)
            {
                lines.Add(line);
            }
        }

        return lines;
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
