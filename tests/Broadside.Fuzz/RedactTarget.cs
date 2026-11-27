using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Redaction;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>redact</c> target (issue #624): the input is a page's decoded content, with Helvetica as <c>/F1</c>; two areas chosen from
/// the input's bytes are redacted. Invariant: the page, read again, shows no glyph, image or painted path segment inside either area
/// (shrunk by half a point, so content cut exactly at an area's edge does not count), and the redaction never throws.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.6.23; §9.4.3 (Table 107), §8.5, §8.9, §7.8.2.</remarks>
internal static class RedactTarget
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName Font = new("Font");

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        page.Dictionary[Contents] = document.AddObject(new CosStream(new CosDictionary(), data.ToArray()));
        var helvetica = new CosDictionary
        {
            [new CosName("Type")] = Font,
            [new CosName("Subtype")] = new CosName("Type1"),
            [new CosName("BaseFont")] = new CosName("Helvetica"),
        };
        page.Dictionary[Resources] = new CosDictionary { [Font] = new CosDictionary { [new CosName("F1")] = helvetica } };

        PdfRectangle[] boxes = Boxes(data);
        Quad[] areas = [.. boxes.Select(static box => Quad.FromRectangle(Matrix.Identity, box.Left, box.Bottom, box.Right, box.Top))];
        page.Redact(areas, new PdfRedactionOptions().WithFillColor(null));

        var checker = new Checker([.. boxes.Select(static box => Quad.FromRectangle(Matrix.Identity, box.Left + 0.5, box.Bottom + 0.5, box.Right - 0.5, box.Top - 0.5))]);
        page.ProcessContent(checker);
        if (checker.Found is { } found)
        {
            throw new InvalidOperationException($"After redaction the page still shows {found} inside an area.");
        }
    }

    /// <summary>Two boxes on the page, chosen from the input's length and bytes.</summary>
    private static PdfRectangle[] Boxes(ReadOnlySpan<byte> data)
    {
        int hash = data.Length;
        foreach (byte value in data[..Math.Min(data.Length, 64)])
        {
            hash = (hash * 31) + value;
        }

        uint seed = (uint)hash;
        PdfRectangle Next()
        {
            seed = (seed * 1664525) + 1013904223;
            double x = seed % 560;
            seed = (seed * 1664525) + 1013904223;
            double y = seed % 740;
            seed = (seed * 1664525) + 1013904223;
            double w = 5 + (seed % 200);
            seed = (seed * 1664525) + 1013904223;
            double h = 5 + (seed % 200);
            return new PdfRectangle(x, y, x + w, y + h);
        }

        return [Next(), Next()];
    }

    private sealed class Checker(Quad[] areas) : ContentProcessor
    {
        public string? Found { get; private set; }

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Images | ContentEvents.Paths | ContentEvents.HiddenContent;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            if (context.Depth != 0 || glyph.Font is not { } font)
            {
                return;
            }

            (double descent, double ascent) = Text.TextExtractor.ReadExtent(font);
            double width = glyph.HorizontalDisplacement;
            Quad quad = Quad.FromRectangle(glyph.TextMatrix * glyph.Ctm, Math.Min(0, width), descent, Math.Max(0, width), ascent);
            if (Meets(quad))
            {
                Found ??= $"glyph {glyph.CharacterCode}";
            }
        }

        public override void PaintImage(in ImageEvent image, ContentContext context)
        {
            if (context.Depth == 0 && Meets(Quad.FromRectangle(image.Ctm, 0, 0, 1, 1)))
            {
                Found ??= "an image";
            }
        }

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            if (context.Depth != 0 || path.Paint == PathPaint.None || !context.State.Ctm.TryInvert(out _))
            {
                return;
            }

            ReadOnlySpan<PathVerb> verbs = path.Path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Path.Points;
            Matrix ctm = context.State.Ctm;
            int point = 0;
            PathPoint current = default;
            PathPoint start = default;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        current = start = ctm.Transform(points[point++]);
                        break;
                    case PathVerb.LineTo:
                        Segment(current, ctm.Transform(points[point]));
                        current = ctm.Transform(points[point++]);
                        break;
                    case PathVerb.QuadTo:
                        point += 2;
                        Segment(current, ctm.Transform(points[point - 1]));
                        current = ctm.Transform(points[point - 1]);
                        break;
                    case PathVerb.CubicTo:
                        // Flattened as the redaction tests curves, in 16 steps.
                        PathPoint p0 = current, p1 = ctm.Transform(points[point]), p2 = ctm.Transform(points[point + 1]), p3 = ctm.Transform(points[point + 2]);
                        for (int step = 1; step <= 16; step++)
                        {
                            double t = step / 16.0, u = 1 - t;
                            var next = new PathPoint(
                                (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                                (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
                            Segment(current, next);
                            current = next;
                        }

                        point += 3;
                        break;
                    case PathVerb.Close:
                        Segment(current, start);
                        current = start;
                        break;
                }
            }
        }

        private void Segment(PathPoint from, PathPoint to)
        {
            foreach (Quad area in areas)
            {
                if (RedactionGeometry.ClipSegment(from, to, area.Bounds, out double enter, out double leave) && leave - enter > 1e-6)
                {
                    Found ??= $"a path segment from {from} to {to}";
                }
            }
        }

        private bool Meets(Quad quad)
        {
            foreach (Quad area in areas)
            {
                if (RedactionGeometry.Intersects(area, quad))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
