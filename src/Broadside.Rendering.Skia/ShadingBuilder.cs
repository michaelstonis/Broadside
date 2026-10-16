using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>
/// Builds the Skia form of a shading (<see cref="ShadingCache.Entry"/>), in shading space so that one form serves every transform.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.3 (axial) and §8.7.4.5.4 (radial): native linear and two-point conical gradients whose stops sample the
/// function over the Domain, as the software rasterizer's table does; an end that is not extended gets a hard transparent stop, so
/// the gradient paints nothing beyond it, and Skia's conical gradient takes the larger t where circles overlap, as §8.7.4.5.4 asks.
/// §8.7.4.5.2 (function-based): the function sampled on a lattice of up to <see cref="MaxTextureSide"/> texels per side over its
/// domain and drawn bilinearly through the shading's Matrix. §8.7.4.5.5 to §8.7.4.5.8 (meshes): Gouraud triangles as
/// <see cref="SKVertices"/>; a mesh with a function carries t as a texture coordinate into a table of the function's colours, so
/// t, not the colour, is interpolated (as the rasterizer does); without one, vertex colours are converted to RGB, triangles in a
/// colour space other than DeviceGray or DeviceRGB being split so that RGB interpolation follows the shading's space; patches are cut
/// into a grid of <see cref="PatchSteps"/> cells per side, drawn in order of v then u so that later parts of a folded patch cover
/// earlier ones (§8.7.4.5.7).
/// </para>
/// <para>Runs once per shading and conversion per backend; allocation here is the cache's fill.</para>
/// </remarks>
internal static class ShadingBuilder
{
    /// <summary>The stops sampled along an axial or radial gradient, and the entries of a mesh's colour table.</summary>
    private const int Stops = 256;

    private const int TableSize = 1024;

    private const int MaxTextureSide = 256;

    private const int PatchSteps = 16;

    private const int MaxVertices = 1 << 21;

    /// <summary>Builds the entry of <paramref name="shading"/>; null for a shading type this backend does not know.</summary>
    public static ShadingCache.Entry? Build(PdfDocument document, in DisplayShading shading)
    {
        PdfShading model = shading.Shading;
        ColorConversion conversion = shading.Conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter converter = document.GetColorConverter(model.InterpolationColorSpace, conversion, shading.DefaultColorSpaces);
        if (converter.PaintsNothing)
        {
            return new ShadingCache.Entry { Conversion = shading.Conversion, Defaults = shading.DefaultColorSpaces, PaintsNothing = true };
        }

        SKColor? background = Background(document, model, converter, conversion, shading.DefaultColorSpaces);
        return model switch
        {
            PdfAxialShading axial => Axial(axial, converter, shading, background),
            PdfRadialShading radial => Radial(radial, converter, shading, background),
            PdfFunctionShading function => Function(function, converter, shading, background),
            PdfTriangleMeshShading triangles => Triangles(triangles, converter, shading, background),
            PdfPatchMeshShading patches => Patches(patches, converter, shading, background),
            _ => null,
        };
    }

    private static ShadingCache.Entry Empty(in DisplayShading shading, SKColor? background) =>
        new() { Conversion = shading.Conversion, Defaults = shading.DefaultColorSpaces, PaintsNothing = background is null, Background = background };

    private static SKColor? Background(PdfDocument document, PdfShading model, PdfColorConverter converter, ColorConversion conversion, PdfDefaultColorSpaces defaults)
    {
        if (model.Background is not { } values)
        {
            return null;
        }

        PdfColorConverter backgroundConverter = ReferenceEquals(model.ColorSpace, model.InterpolationColorSpace)
            ? converter
            : document.GetColorConverter(model.ColorSpace, conversion, defaults);
        if (backgroundConverter.PaintsNothing || values.Count != backgroundConverter.InputCount || values.Count > PdfColor.MaxComponents)
        {
            return null;
        }

        Span<float> components = stackalloc float[PdfColor.MaxComponents];
        for (int i = 0; i < values.Count; i++)
        {
            components[i] = values[i];
        }

        return ToColor(backgroundConverter, components[..values.Count]);
    }

    private static ShadingCache.Entry Axial(PdfAxialShading axial, PdfColorConverter converter, in DisplayShading shading, SKColor? background)
    {
        double dx = axial.End.X - axial.Start.X;
        double dy = axial.End.Y - axial.Start.Y;
        double length = (dx * dx) + (dy * dy);
        if (!axial.HasFunction || !(length > 0) || !double.IsFinite(1 / length))
        {
            return Empty(shading, background);
        }

        (SKColor[] colors, float[] positions) = GradientStops(axial, converter, axial.Domain, axial.ExtendStart, axial.ExtendEnd);
        SKShader gradient = SKShader.CreateLinearGradient(Point(axial.Start), Point(axial.End), colors, positions, SKShaderTileMode.Clamp);
        return new ShadingCache.Entry
        {
            Conversion = shading.Conversion,
            Defaults = shading.DefaultColorSpaces,
            Background = background,
            Gradient = gradient,
            Bytes = colors.Length * 8,
        };
    }

    private static ShadingCache.Entry Radial(PdfRadialShading radial, PdfColorConverter converter, in DisplayShading shading, SKColor? background)
    {
        if (!radial.HasFunction || (radial.StartRadius == 0 && radial.EndRadius == 0))
        {
            return Empty(shading, background);
        }

        (SKColor[] colors, float[] positions) = GradientStops(radial, converter, radial.Domain, radial.ExtendStart, radial.ExtendEnd);
        SKShader gradient = SKShader.CreateTwoPointConicalGradient(
            Point(radial.StartCenter),
            (float)radial.StartRadius,
            Point(radial.EndCenter),
            (float)radial.EndRadius,
            colors,
            positions,
            SKShaderTileMode.Clamp);
        return new ShadingCache.Entry
        {
            Conversion = shading.Conversion,
            Defaults = shading.DefaultColorSpaces,
            Background = background,
            Gradient = gradient,
            Bytes = colors.Length * 8,
        };
    }

    /// <summary>The stops of a gradient: the function at evenly spaced t over the Domain, hard transparent stops at ends not extended.</summary>
    private static (SKColor[] Colors, float[] Positions) GradientStops(PdfShading model, PdfColorConverter converter, IReadOnlyList<double> domain, bool extendStart, bool extendEnd)
    {
        double t0 = domain.Count >= 2 ? domain[0] : 0;
        double t1 = domain.Count >= 2 ? domain[1] : 1;
        int count = Stops + (extendStart ? 0 : 1) + (extendEnd ? 0 : 1);
        var colors = new SKColor[count];
        var positions = new float[count];
        Span<float> input = stackalloc float[1];
        Span<float> components = stackalloc float[Math.Max(model.ColorComponentCount, 1)];
        int k = 0;
        if (!extendStart)
        {
            colors[k] = SKColors.Transparent;
            positions[k++] = 0;
        }

        for (int i = 0; i < Stops; i++)
        {
            double s = i / (double)(Stops - 1);
            input[0] = (float)(t0 + (s * (t1 - t0)));
            model.EvaluateFunction(input, components);
            colors[k] = ToColor(converter, components);
            positions[k++] = (float)s;
        }

        if (!extendEnd)
        {
            colors[k] = SKColors.Transparent;
            positions[k] = 1;
        }

        return (colors, positions);
    }

    private static ShadingCache.Entry Function(PdfFunctionShading function, PdfColorConverter converter, in DisplayShading shading, SKColor? background)
    {
        IReadOnlyList<double> domain = function.Domain;
        double x0 = domain.Count >= 4 ? domain[0] : 0;
        double x1 = domain.Count >= 4 ? domain[1] : 1;
        double y0 = domain.Count >= 4 ? domain[2] : 0;
        double y1 = domain.Count >= 4 ? domain[3] : 1;
        if (!function.HasFunction || !(x1 > x0) || !(y1 > y0) || !double.IsFinite(x1 - x0) || !double.IsFinite(y1 - y0))
        {
            return Empty(shading, background);
        }

        const int side = MaxTextureSide;
        byte[] pixels = new byte[side * side * 4];
        Span<float> input = stackalloc float[2];
        Span<float> components = stackalloc float[Math.Max(function.ColorComponentCount, 1)];
        for (int j = 0; j < side; j++)
        {
            input[1] = (float)(y0 + ((j + 0.5) / side * (y1 - y0)));
            for (int i = 0; i < side; i++)
            {
                input[0] = (float)(x0 + ((i + 0.5) / side * (x1 - x0)));
                function.EvaluateFunction(input, components);
                SKColor color = ToColor(converter, components);
                int p = ((j * side) + i) * 4;
                pixels[p] = color.Blue;
                pixels[p + 1] = color.Green;
                pixels[p + 2] = color.Red;
                pixels[p + 3] = 255;
            }
        }

        SKImage texture = SKImage.FromPixelCopy(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, side * 4);
        return new ShadingCache.Entry
        {
            Conversion = shading.Conversion,
            Defaults = shading.DefaultColorSpaces,
            Background = background,
            Texture = texture,
            TextureArea = new SKRect((float)x0, (float)y0, (float)x1, (float)y1),
            TextureMatrix = ToSkia(function.Matrix),
            Bytes = pixels.Length,
        };
    }

    private static ShadingCache.Entry Triangles(PdfTriangleMeshShading mesh, PdfColorConverter converter, in DisplayShading shading, SKColor? background)
    {
        ReadOnlySpan<PathPoint> points = mesh.Vertices;
        ReadOnlySpan<int> triangles = mesh.Triangles;
        ReadOnlySpan<float> colors = mesh.VertexColors;
        int stride = mesh.ColorStride;
        int count = triangles.Length / 3;
        if (count == 0)
        {
            return Empty(shading, background);
        }

        bool parametric = mesh.HasFunction;
        int split = parametric || IsLinear(mesh.InterpolationColorSpace) ? 1 : 4;
        while (split > 1 && (long)count * split * split * 3 > MaxVertices)
        {
            split--;
        }

        count = (int)Math.Min(count, MaxVertices / (3L * split * split));
        var builder = new VertexBuilder(count * split * split * 3, parametric, stride);
        (double t0, double t1) = MeshDomain(mesh);
        Span<float> a = stackalloc float[stride];
        Span<float> b = stackalloc float[stride];
        Span<float> c = stackalloc float[stride];
        for (int t = 0; t < count; t++)
        {
            int ia = triangles[3 * t];
            int ib = triangles[(3 * t) + 1];
            int ic = triangles[(3 * t) + 2];
            colors.Slice(ia * stride, stride).CopyTo(a);
            colors.Slice(ib * stride, stride).CopyTo(b);
            colors.Slice(ic * stride, stride).CopyTo(c);
            builder.AddTriangle(points[ia], a, points[ib], b, points[ic], c, split, converter, mesh, t0, t1);
        }

        return MeshEntry(builder, parametric ? Table(mesh, converter, t0, t1) : null, shading, background);
    }

    private static ShadingCache.Entry Patches(PdfPatchMeshShading mesh, PdfColorConverter converter, in DisplayShading shading, SKColor? background)
    {
        int patches = mesh.PatchCount;
        if (patches == 0)
        {
            return Empty(shading, background);
        }

        int steps = PatchSteps;
        while (steps > 1 && (long)patches * steps * steps * 6 > MaxVertices)
        {
            steps--;
        }

        patches = (int)Math.Min(patches, MaxVertices / (6L * steps * steps));
        bool parametric = mesh.HasFunction;
        int stride = mesh.ColorStride;
        var builder = new VertexBuilder(patches * steps * steps * 6, parametric, stride);
        (double t0, double t1) = MeshDomain(mesh);
        ReadOnlySpan<PathPoint> control = mesh.ControlPoints;
        ReadOnlySpan<float> corners = mesh.CornerColors;
        int grid = steps + 1;
        var surface = new PathPoint[grid * grid];
        float[] surfaceColors = new float[grid * grid * stride];
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        for (int patch = 0; patch < patches; patch++)
        {
            ReadOnlySpan<PathPoint> p = control.Slice(16 * patch, 16);
            ReadOnlySpan<float> corner = corners.Slice(4 * stride * patch, 4 * stride);
            for (int j = 0; j < grid; j++)
            {
                double v = j / (double)steps;
                Bernstein(v, bv);
                for (int i = 0; i < grid; i++)
                {
                    double u = i / (double)steps;
                    Bernstein(u, bu);
                    double x = 0;
                    double y = 0;
                    for (int pi = 0; pi < 4; pi++)
                    {
                        for (int pj = 0; pj < 4; pj++)
                        {
                            double weight = bu[pi] * bv[pj];
                            x += p[(4 * pi) + pj].X * weight;
                            y += p[(4 * pi) + pj].Y * weight;
                        }
                    }

                    int g = (j * grid) + i;
                    surface[g] = new PathPoint(x, y);

                    // Corner colours at p00 (u 0, v 0), p03 (u 0, v 1), p33 (u 1, v 1), p30 (u 1, v 0).
                    for (int k = 0; k < stride; k++)
                    {
                        surfaceColors[(g * stride) + k] = (float)(((1 - u) * (1 - v) * corner[k]) + ((1 - u) * v * corner[stride + k])
                            + (u * v * corner[(2 * stride) + k]) + (u * (1 - v) * corner[(3 * stride) + k]));
                    }
                }
            }

            for (int j = 0; j < steps; j++)
            {
                for (int i = 0; i < steps; i++)
                {
                    int g00 = (j * grid) + i;
                    int g10 = g00 + 1;
                    int g01 = g00 + grid;
                    int g11 = g01 + 1;
                    builder.AddVertex(surface[g00], surfaceColors.AsSpan(g00 * stride, stride), converter, mesh, t0, t1);
                    builder.AddVertex(surface[g10], surfaceColors.AsSpan(g10 * stride, stride), converter, mesh, t0, t1);
                    builder.AddVertex(surface[g11], surfaceColors.AsSpan(g11 * stride, stride), converter, mesh, t0, t1);
                    builder.AddVertex(surface[g00], surfaceColors.AsSpan(g00 * stride, stride), converter, mesh, t0, t1);
                    builder.AddVertex(surface[g11], surfaceColors.AsSpan(g11 * stride, stride), converter, mesh, t0, t1);
                    builder.AddVertex(surface[g01], surfaceColors.AsSpan(g01 * stride, stride), converter, mesh, t0, t1);
                }
            }
        }

        return MeshEntry(builder, parametric ? Table(mesh, converter, t0, t1) : null, shading, background);
    }

    private static void Bernstein(double t, Span<double> b)
    {
        double s = 1 - t;
        b[0] = s * s * s;
        b[1] = 3 * t * s * s;
        b[2] = 3 * t * t * s;
        b[3] = t * t * t;
    }

    private static ShadingCache.Entry MeshEntry(VertexBuilder builder, SKShader? table, in DisplayShading shading, SKColor? background)
    {
        SKVertices vertices = builder.Build();
        return new ShadingCache.Entry
        {
            Conversion = shading.Conversion,
            Defaults = shading.DefaultColorSpaces,
            Background = background,
            Mesh = vertices,
            MeshTable = table,
            Bytes = (long)builder.Count * 16,
        };
    }

    /// <summary>The colour table of a mesh with a function: <see cref="TableSize"/> colours of t over the Decode range of t, one row.</summary>
    private static SKShader Table(PdfMeshShading mesh, PdfColorConverter converter, double t0, double t1)
    {
        var pixels = new SKColor[TableSize];
        Span<float> input = stackalloc float[1];
        Span<float> components = stackalloc float[Math.Max(mesh.ColorComponentCount, 1)];
        for (int i = 0; i < TableSize; i++)
        {
            input[0] = (float)(t0 + (i / (double)(TableSize - 1) * (t1 - t0)));
            mesh.EvaluateFunction(input, components);
            pixels[i] = ToColor(converter, components);
        }

        using var bitmap = new SKBitmap(new SKImageInfo(TableSize, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Pixels = pixels;
        using SKImage image = SKImage.FromBitmap(bitmap);
        return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }

    private static (double T0, double T1) MeshDomain(PdfMeshShading mesh)
    {
        IReadOnlyList<double> decode = mesh.Decode;
        return decode.Count >= 6 ? (decode[4], decode[5]) : (0, 1);
    }

    private static bool IsLinear(PdfColorSpace space) => space.Family is PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb;

    private static SKColor ToColor(PdfColorConverter converter, ReadOnlySpan<float> components)
    {
        Span<float> rgb = stackalloc float[4];
        converter.Convert(components, rgb);
        return new SKColor(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
    }

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    private static SKPoint Point(PathPoint point) => new((float)point.X, (float)point.Y);

    private static SKMatrix ToSkia(in Matrix m) => new((float)m.A, (float)m.C, (float)m.E, (float)m.B, (float)m.D, (float)m.F, 0, 0, 1);

    /// <summary>The triangles of a mesh as Skia vertices: positions, and colours or t as a texture coordinate into the table.</summary>
    private sealed class VertexBuilder(int capacity, bool parametric, int stride)
    {
        private readonly SKPoint[] _positions = new SKPoint[capacity];
        private readonly SKColor[]? _colors = parametric ? null : new SKColor[capacity];
        private readonly SKPoint[]? _texture = parametric ? new SKPoint[capacity] : null;

        public int Count { get; private set; }

        public void AddVertex(PathPoint point, ReadOnlySpan<float> color, PdfColorConverter converter, PdfMeshShading mesh, double t0, double t1)
        {
            if (Count == _positions.Length)
            {
                return;
            }

            _positions[Count] = new SKPoint((float)point.X, (float)point.Y);
            if (_texture is not null)
            {
                double s = t1 != t0 ? (color[0] - t0) / (t1 - t0) : 0;
                _texture[Count] = new SKPoint((float)(0.5 + (Math.Clamp(s, 0, 1) * (TableSize - 1))), 0.5f);
            }
            else
            {
                _colors![Count] = ToColor(converter, color[..stride]);
            }

            Count++;
        }

        /// <summary>Adds a triangle, split into <paramref name="split"/>² triangles whose corners interpolate the colours in the shading's space.</summary>
        public void AddTriangle(
            PathPoint a,
            ReadOnlySpan<float> ca,
            PathPoint b,
            ReadOnlySpan<float> cb,
            PathPoint c,
            ReadOnlySpan<float> cc,
            int split,
            PdfColorConverter converter,
            PdfMeshShading mesh,
            double t0,
            double t1)
        {
            if (split == 1)
            {
                AddVertex(a, ca, converter, mesh, t0, t1);
                AddVertex(b, cb, converter, mesh, t0, t1);
                AddVertex(c, cc, converter, mesh, t0, t1);
                return;
            }

            Span<float> c0 = stackalloc float[stride];
            Span<float> c1 = stackalloc float[stride];
            Span<float> c2 = stackalloc float[stride];
            for (int j = 0; j < split; j++)
            {
                for (int i = 0; i + j < split; i++)
                {
                    // The lower triangle of the cell, then the upper one where the cell is whole.
                    for (int upper = 0; upper < (i + j + 1 < split ? 2 : 1); upper++)
                    {
                        (int i0, int j0, int i1, int j1, int i2, int j2) = upper == 0 ? (i, j, i + 1, j, i, j + 1) : (i + 1, j, i + 1, j + 1, i, j + 1);
                        PathPoint p0 = Interpolate(a, ca, b, cb, c, cc, i0 / (double)split, j0 / (double)split, c0);
                        PathPoint p1 = Interpolate(a, ca, b, cb, c, cc, i1 / (double)split, j1 / (double)split, c1);
                        PathPoint p2 = Interpolate(a, ca, b, cb, c, cc, i2 / (double)split, j2 / (double)split, c2);
                        AddVertex(p0, c0, converter, mesh, t0, t1);
                        AddVertex(p1, c1, converter, mesh, t0, t1);
                        AddVertex(p2, c2, converter, mesh, t0, t1);
                    }
                }
            }
        }

        private static PathPoint Interpolate(PathPoint a, ReadOnlySpan<float> ca, PathPoint b, ReadOnlySpan<float> cb, PathPoint c, ReadOnlySpan<float> cc, double u, double v, Span<float> color)
        {
            double w = 1 - u - v;
            for (int k = 0; k < color.Length; k++)
            {
                color[k] = (float)((ca[k] * w) + (cb[k] * u) + (cc[k] * v));
            }

            return new PathPoint((a.X * w) + (b.X * u) + (c.X * v), (a.Y * w) + (b.Y * u) + (c.Y * v));
        }

        public SKVertices Build()
        {
            SKPoint[] positions = _positions.AsSpan(0, Count).ToArray();
            return _texture is not null
                ? SKVertices.CreateCopy(SKVertexMode.Triangles, positions, _texture.AsSpan(0, Count).ToArray(), null)
                : SKVertices.CreateCopy(SKVertexMode.Triangles, positions, _colors.AsSpan(0, Count).ToArray());
        }
    }
}
