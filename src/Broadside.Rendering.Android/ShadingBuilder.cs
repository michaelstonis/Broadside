using Broadside.Graphics;
using LinearGradient = Android.Graphics.LinearGradient;
using RadialGradient = Android.Graphics.RadialGradient;
using Shader = Android.Graphics.Shader;

// Every Android shader made here belongs to the entry it is returned in, which ShadingCache releases.
#pragma warning disable CA2000

namespace Broadside.Rendering.Android;

/// <summary>
/// Builds the Android form of a shading (<see cref="ShadingCache.Entry"/>), in shading space so that one form serves every transform.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.3 (axial) and §8.7.4.5.4 (radial): native linear and two-point conical gradient shaders whose
/// <see cref="Stops"/> stops sample the function over the Domain, as the software rasterizer's table does; an end that is not
/// extended gets a hard transparent stop, so the gradient paints nothing beyond it, and the conical gradient takes the larger t where
/// circles overlap, as §8.7.4.5.4 asks. §8.7.4.5.2 (function-based): the function sampled on a lattice of up to
/// <see cref="MaxTextureSide"/> texels per side over its domain, drawn bilinearly through the shading's Matrix. §8.7.4.5.5 to
/// §8.7.4.5.8 (meshes): the triangles (patches cut into a grid of <see cref="PatchSteps"/> cells per side, in order of v then u so
/// that later parts of a folded patch cover earlier ones, §8.7.4.5.7) are kept for <see cref="MeshRasterizer"/>, which interpolates
/// colours (or t, with a function) in the shading's colour space at each device pixel.
/// </para>
/// <para>Runs once per shading and conversion per backend; allocation here is the cache's fill.</para>
/// </remarks>
internal static class ShadingBuilder
{
    /// <summary>The samples of an axial or radial gradient's function, and the entries of a mesh's colour table.</summary>
    internal const int Stops = 256;

    internal const int TableSize = 1024;

    private const int MaxTextureSide = 256;

    private const int PatchSteps = 16;

    private const int MaxTriangles = 1 << 20;

    private static readonly Shader.TileMode Clamp = Shader.TileMode.Clamp!;

    /// <summary>Builds the entry of <paramref name="shading"/>; null for a shading type this backend does not draw.</summary>
    public static ShadingCache.Entry? Build(PdfDocument document, in DisplayShading shading)
    {
        PdfShading model = shading.Shading;
        ColorConversion conversion = shading.Conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter converter = document.GetColorConverter(model.InterpolationColorSpace, conversion, shading.DefaultColorSpaces);
        if (converter.PaintsNothing)
        {
            return new ShadingCache.Entry { Conversion = shading.Conversion, Defaults = shading.DefaultColorSpaces, PaintsNothing = true };
        }

        (byte, byte, byte)? background = Background(document, model, converter, conversion, shading.DefaultColorSpaces);
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

    private static ShadingCache.Entry Empty(in DisplayShading shading, (byte, byte, byte)? background) =>
        new() { Conversion = shading.Conversion, Defaults = shading.DefaultColorSpaces, PaintsNothing = background is null, Background = background };

    private static (byte, byte, byte)? Background(PdfDocument document, PdfShading model, PdfColorConverter converter, ColorConversion conversion, PdfDefaultColorSpaces defaults)
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

    private static ShadingCache.Entry Axial(PdfAxialShading axial, PdfColorConverter converter, in DisplayShading shading, (byte, byte, byte)? background)
    {
        double dx = axial.End.X - axial.Start.X;
        double dy = axial.End.Y - axial.Start.Y;
        double length = (dx * dx) + (dy * dy);
        if (!axial.HasFunction || !(length > 0) || !double.IsFinite(1 / length))
        {
            return Empty(shading, background);
        }

        (int[] colors, float[] positions) = GradientStops(axial, converter, axial.Domain, axial.ExtendStart, axial.ExtendEnd, 0, 1);
        var gradient = new LinearGradient((float)axial.Start.X, (float)axial.Start.Y, (float)axial.End.X, (float)axial.End.Y, colors, positions, Clamp);
        return Entry(gradient, colors.Length, shading, background);
    }

    /// <summary>
    /// A radial shading as a two-point conical gradient (Android 12, API level 31, and later); on earlier versions only concentric
    /// circles that grow, as a centred gradient whose stops start at the first circle; null (unsupported, reported) otherwise.
    /// </summary>
    private static ShadingCache.Entry? Radial(PdfRadialShading radial, PdfColorConverter converter, in DisplayShading shading, (byte, byte, byte)? background)
    {
        if (!radial.HasFunction || (radial.StartRadius == 0 && radial.EndRadius == 0))
        {
            return Empty(shading, background);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            (int[] colors, float[] positions) = GradientStops(radial, converter, radial.Domain, radial.ExtendStart, radial.ExtendEnd, 0, 1);
            long[] packed = new long[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                // Color.pack of an sRGB colour: the ARGB int in the high 32 bits, colour space 0 below.
                packed[i] = (long)(uint)colors[i] << 32;
            }

            // Android refuses an end circle of radius 0, which §8.7.4.5.4 allows (circles shrinking to a point): it becomes a millionth
            // of the start radius, the same circles to well under a pixel, the parameter still growing towards the end.
            double endRadius = radial.EndRadius > 0 ? radial.EndRadius : radial.StartRadius * 1e-6;
            var conical = new RadialGradient(
                (float)radial.StartCenter.X,
                (float)radial.StartCenter.Y,
                (float)Math.Max(radial.StartRadius, 0),
                (float)radial.EndCenter.X,
                (float)radial.EndCenter.Y,
                (float)endRadius,
                packed,
                positions,
                Clamp);
            return Entry(conical, colors.Length, shading, background);
        }

        double r0 = radial.StartRadius;
        double r1 = radial.EndRadius;
        if (radial.StartCenter != radial.EndCenter || !(r1 > 0) || !(r0 >= 0) || r0 > r1 || !double.IsFinite(r1))
        {
            return null;
        }

        (int[] centred, float[] stops) = GradientStops(radial, converter, radial.Domain, radial.ExtendStart, radial.ExtendEnd, (float)(r0 / r1), 1);
        var gradient = new RadialGradient((float)radial.EndCenter.X, (float)radial.EndCenter.Y, (float)r1, centred, stops, Clamp);
        return Entry(gradient, centred.Length, shading, background);
    }

    private static ShadingCache.Entry Entry(Shader gradient, int stops, in DisplayShading shading, (byte, byte, byte)? background) => new()
    {
        Conversion = shading.Conversion,
        Defaults = shading.DefaultColorSpaces,
        Background = background,
        Gradient = gradient,
        Bytes = stops * 12L,
    };

    /// <summary>
    /// The stops of a gradient: the function at <see cref="Stops"/> evenly spaced t over the Domain, placed from
    /// <paramref name="from"/> to <paramref name="to"/>, and a hard transparent stop at each end not extended, so that the gradient
    /// paints nothing beyond it (Extend, Table 80, Table 81).
    /// </summary>
    private static (int[] Colors, float[] Positions) GradientStops(PdfShading model, PdfColorConverter converter, IReadOnlyList<double> domain, bool extendStart, bool extendEnd, float from, float to)
    {
        double t0 = domain.Count >= 2 ? domain[0] : 0;
        double t1 = domain.Count >= 2 ? domain[1] : 1;
        int count = Stops + (extendStart ? 0 : 2) + (extendEnd ? 0 : 1);
        int[] colors = new int[count];
        float[] positions = new float[count];
        Span<float> input = stackalloc float[1];
        Span<float> components = stackalloc float[Math.Max(model.ColorComponentCount, 1)];
        int k = 0;
        if (!extendStart)
        {
            // Transparent up to the start: from 0 when the stops start later (a centred radial gradient's first circle).
            positions[k++] = 0;
            positions[k++] = from;
        }

        for (int i = 0; i < Stops; i++)
        {
            double s = i / (double)(Stops - 1);
            input[0] = (float)(t0 + (s * (t1 - t0)));
            model.EvaluateFunction(input, components);
            (byte r, byte g, byte b) = ToColor(converter, components);
            colors[k] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            positions[k++] = (float)(from + (s * (to - from)));
        }

        if (!extendEnd)
        {
            positions[k] = to;
        }

        return (colors, positions);
    }

    private static ShadingCache.Entry Function(PdfFunctionShading function, PdfColorConverter converter, in DisplayShading shading, (byte, byte, byte)? background)
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
                (byte r, byte g, byte b) = ToColor(converter, components);
                int p = ((j * side) + i) * 4;
                pixels[p] = b;
                pixels[p + 1] = g;
                pixels[p + 2] = r;
                pixels[p + 3] = 255;
            }
        }

        return new ShadingCache.Entry
        {
            Conversion = shading.Conversion,
            Defaults = shading.DefaultColorSpaces,
            Background = background,
            Texture = PixelImage.Copy(pixels, side, side, 4),
            TextureArea = new PdfRectangle(x0, y0, x1, y1),
            TextureMatrix = function.Matrix,
            Bytes = pixels.Length,
        };
    }

    private static ShadingCache.Entry Triangles(PdfTriangleMeshShading mesh, PdfColorConverter converter, in DisplayShading shading, (byte, byte, byte)? background)
    {
        ReadOnlySpan<PathPoint> points = mesh.Vertices;
        ReadOnlySpan<int> triangles = mesh.Triangles;
        ReadOnlySpan<float> colors = mesh.VertexColors;
        int stride = mesh.ColorStride;
        int count = Math.Min(triangles.Length / 3, MaxTriangles);
        if (count == 0 || stride <= 0)
        {
            return Empty(shading, background);
        }

        var builder = new MeshTriangles(count, stride, converter, Table(mesh, converter));
        for (int t = 0; t < count; t++)
        {
            int ia = triangles[3 * t];
            int ib = triangles[(3 * t) + 1];
            int ic = triangles[(3 * t) + 2];
            builder.Add(points[ia], colors.Slice(ia * stride, stride), points[ib], colors.Slice(ib * stride, stride), points[ic], colors.Slice(ic * stride, stride));
        }

        return MeshEntry(builder, shading, background);
    }

    private static ShadingCache.Entry Patches(PdfPatchMeshShading mesh, PdfColorConverter converter, in DisplayShading shading, (byte, byte, byte)? background)
    {
        int patches = mesh.PatchCount;
        int stride = mesh.ColorStride;
        if (patches == 0 || stride <= 0)
        {
            return Empty(shading, background);
        }

        int steps = PatchSteps;
        while (steps > 1 && (long)patches * steps * steps * 2 > MaxTriangles)
        {
            steps--;
        }

        patches = (int)Math.Min(patches, MaxTriangles / (2L * steps * steps));
        var builder = new MeshTriangles(patches * steps * steps * 2, stride, converter, Table(mesh, converter));
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
                    builder.Add(surface[g00], surfaceColors.AsSpan(g00 * stride, stride), surface[g10], surfaceColors.AsSpan(g10 * stride, stride), surface[g11], surfaceColors.AsSpan(g11 * stride, stride));
                    builder.Add(surface[g00], surfaceColors.AsSpan(g00 * stride, stride), surface[g11], surfaceColors.AsSpan(g11 * stride, stride), surface[g01], surfaceColors.AsSpan(g01 * stride, stride));
                }
            }
        }

        return MeshEntry(builder, shading, background);
    }

    private static void Bernstein(double t, Span<double> b)
    {
        double s = 1 - t;
        b[0] = s * s * s;
        b[1] = 3 * t * s * s;
        b[2] = 3 * t * t * s;
        b[3] = t * t * t;
    }

    private static ShadingCache.Entry MeshEntry(MeshTriangles triangles, in DisplayShading shading, (byte, byte, byte)? background) => new()
    {
        Conversion = shading.Conversion,
        Defaults = shading.DefaultColorSpaces,
        Background = background,
        Mesh = triangles,
        Bytes = triangles.Bytes,
    };

    /// <summary>
    /// The colour table of a mesh with a function: <see cref="TableSize"/> sRGB colours of t over the Decode range of t, linear
    /// between entries as t is; null without a function.
    /// </summary>
    private static MeshTable? Table(PdfMeshShading mesh, PdfColorConverter converter)
    {
        if (!mesh.HasFunction)
        {
            return null;
        }

        IReadOnlyList<double> decode = mesh.Decode;
        (double t0, double t1) = decode.Count >= 6 ? (decode[4], decode[5]) : (0, 1);
        var colors = new byte[TableSize * 3];
        Span<float> input = stackalloc float[1];
        Span<float> components = stackalloc float[Math.Max(mesh.ColorComponentCount, 1)];
        for (int i = 0; i < TableSize; i++)
        {
            input[0] = (float)(t0 + (i / (double)(TableSize - 1) * (t1 - t0)));
            mesh.EvaluateFunction(input, components);
            (colors[3 * i], colors[(3 * i) + 1], colors[(3 * i) + 2]) = ToColor(converter, components);
        }

        return new MeshTable(colors, t0, t1);
    }

    internal static (byte R, byte G, byte B) ToColor(PdfColorConverter converter, ReadOnlySpan<float> components)
    {
        Span<float> rgb = stackalloc float[4];
        converter.Convert(components, rgb);
        return (Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
    }

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);
}

/// <summary>The colours of a mesh's function: <see cref="ShadingBuilder.TableSize"/> sRGB entries over t from <c>T0</c> to <c>T1</c>.</summary>
internal sealed record MeshTable(byte[] Colors, double T0, double T1);

/// <summary>
/// The triangles of a mesh shading in shading space, each with its corners' colour components in the shading's colour space (or t,
/// with a function), in the order they paint (§8.7.4.5.5: later triangles cover earlier ones).
/// </summary>
internal sealed class MeshTriangles(int capacity, int stride, PdfColorConverter converter, MeshTable? table)
{
    private readonly PathPoint[] _points = new PathPoint[capacity * 3];
    private readonly float[] _components = new float[capacity * 3 * stride];

    public int Count { get; private set; }

    public int Stride => stride;

    public PdfColorConverter Converter => converter;

    public MeshTable? Table => table;

    public ReadOnlySpan<PathPoint> Points => _points.AsSpan(0, Count * 3);

    public ReadOnlySpan<float> Components => _components.AsSpan(0, Count * 3 * stride);

    public long Bytes => ((long)_points.Length * 16) + ((long)_components.Length * 4);

    /// <summary>Gets the smallest rectangle in shading space holding every corner (empty while there are none).</summary>
    public PdfRectangle Extent => Count == 0 ? default : new PdfRectangle(_minX, _minY, _maxX, _maxY);

    private double _minX = double.PositiveInfinity;
    private double _minY = double.PositiveInfinity;
    private double _maxX = double.NegativeInfinity;
    private double _maxY = double.NegativeInfinity;

    private void Include(PathPoint point)
    {
        if (double.IsFinite(point.X) && double.IsFinite(point.Y))
        {
            _minX = Math.Min(_minX, point.X);
            _minY = Math.Min(_minY, point.Y);
            _maxX = Math.Max(_maxX, point.X);
            _maxY = Math.Max(_maxY, point.Y);
        }
    }

    public void Add(PathPoint a, ReadOnlySpan<float> ca, PathPoint b, ReadOnlySpan<float> cb, PathPoint c, ReadOnlySpan<float> cc)
    {
        if (Count * 3 == _points.Length)
        {
            return;
        }

        int p = Count * 3;
        _points[p] = a;
        _points[p + 1] = b;
        _points[p + 2] = c;
        Include(a);
        Include(b);
        Include(c);
        ca[..stride].CopyTo(_components.AsSpan(p * stride, stride));
        cb[..stride].CopyTo(_components.AsSpan((p + 1) * stride, stride));
        cc[..stride].CopyTo(_components.AsSpan((p + 2) * stride, stride));
        Count++;
    }
}
