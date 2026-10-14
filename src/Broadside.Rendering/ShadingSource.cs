using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The colours of an axial or radial shading, or of a triangle mesh with a function, as a lookup table: the shading's function
/// sampled at <see cref="Size"/> evenly spaced values of t across its Domain (a mesh's Decode interval of t, §8.7.4.5.5), converted to opaque premultiplied BGRA through the document's colour converter, so the
/// per-pixel work is finding s and reading one entry. Kept for the last shading paint and rebuilt only when the shading, its
/// default colour spaces or its conversion change.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.3 and §8.7.4.5.4: t = t₀ + (t₁ − t₀) s for s in [0, 1] (extended points take s = 0 or 1), and the colour is
/// the function of t, each component clipped to its range (§8.7.4.5, <see cref="PdfShading.EvaluateFunction"/>). 1024 entries
/// keep neighbouring entries within a quarter of an 8-bit level for a function that spans the whole range once, so the table adds
/// no visible banding. Colour conversion goes through
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, the engine's
/// colour-management extension point (§8.6, §10).
/// </para>
/// <para>
/// The buffers are allocated on first use and kept, so a warm render allocates nothing.
/// </para>
/// </remarks>
internal sealed class ShadingSource
{
    /// <summary>The number of entries of the table.</summary>
    public const int Size = 1024;

    private readonly uint[] _table = new uint[Size];
    private float[] _components = [];
    private float[] _rgb = [];

    private PdfShading? _shading;
    private PdfDefaultColorSpaces? _defaults;
    private ColorConversion _conversion;
    private bool _paintsNothing;

    /// <summary>Gets the table: entry i is the colour at s = i / (<see cref="Size"/> − 1).</summary>
    public uint[] Table => _table;

    /// <summary>Gets the interval of t the table spans, entry 0 at T0 and the last at T1.</summary>
    public (double T0, double T1) ParameterDomain { get; private set; }

    /// <summary>Gets the converter from the shading's <see cref="PdfShading.InterpolationColorSpace"/> to RGB, set by <see cref="Prepare"/>.</summary>
    public PdfColorConverter? Converter { get; private set; }

    /// <summary>Gets a value indicating whether the shading has a Background colour, converted in <see cref="Background"/>.</summary>
    public bool HasBackground { get; private set; }

    /// <summary>Gets the shading's Background colour as opaque premultiplied BGRA, when <see cref="HasBackground"/>.</summary>
    public uint Background { get; private set; }

    /// <summary>Builds the table for <paramref name="shading"/> unless it is the one built last.</summary>
    /// <returns><see langword="false"/> when the shading's colours paint nothing (a Separation None or all-None DeviceN space, §8.6.6.4).</returns>
    public bool Prepare(PdfDocument document, in DisplayShading shading)
    {
        PdfShading model = shading.Shading;
        if (ReferenceEquals(_shading, model) && ReferenceEquals(_defaults, shading.DefaultColorSpaces) && _conversion == shading.Conversion)
        {
            return !_paintsNothing;
        }

        _shading = model;
        _defaults = shading.DefaultColorSpaces;
        _conversion = shading.Conversion;
        ColorConversion conversion = shading.Conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter converter = document.GetColorConverter(model.InterpolationColorSpace, conversion, shading.DefaultColorSpaces);
        _paintsNothing = converter.PaintsNothing;
        Converter = converter;
        if (_paintsNothing)
        {
            return false;
        }

        // A table of t serves axial, radial and mesh shadings with a function; a function-based shading (Type 1) takes two inputs
        // and is sampled by its painter, and a mesh without a function has its colours at the vertices.
        if (model.HasFunction && model is not PdfFunctionShading)
        {
            Fill(model, converter);
        }

        HasBackground = false;
        if (model.Background is { } background)
        {
            PdfColorConverter backgroundConverter = ReferenceEquals(model.ColorSpace, model.InterpolationColorSpace)
                ? converter
                : document.GetColorConverter(model.ColorSpace, conversion, shading.DefaultColorSpaces);
            if (!backgroundConverter.PaintsNothing && background.Count == backgroundConverter.InputCount && background.Count <= PdfColor.MaxComponents)
            {
                Span<float> components = stackalloc float[PdfColor.MaxComponents];
                for (int i = 0; i < background.Count; i++)
                {
                    components[i] = background[i];
                }

                Span<float> rgb = stackalloc float[3];
                backgroundConverter.Convert(components[..background.Count], rgb);
                Background = Pack(rgb[0], rgb[1], rgb[2]);
                HasBackground = true;
            }
        }

        return true;
    }

    /// <summary>Lets go of the shading and its conversion; the buffers stay for the next render.</summary>
    public void Forget()
    {
        _shading = null;
        _defaults = null;
        Converter = null;
    }

    private static uint Pack(float r, float g, float b) =>
        (uint)Channel(b) | ((uint)Channel(g) << 8) | ((uint)Channel(r) << 16) | 0xFF000000u;

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    /// <summary>Samples the function at every entry and converts the colours in one batch.</summary>
    private void Fill(PdfShading model, PdfColorConverter converter)
    {
        int n = model.ColorComponentCount;
        if (_components.Length < Size * n)
        {
            _components = new float[Size * n];
        }

        if (_rgb.Length < Size * 3)
        {
            _rgb = new float[Size * 3];
        }

        (double t0, double t1) = Domain(model);
        ParameterDomain = (t0, t1);
        Span<float> input = stackalloc float[2];
        for (int i = 0; i < Size; i++)
        {
            input[0] = (float)(t0 + ((t1 - t0) * i / (Size - 1)));
            model.EvaluateFunction(input, _components.AsSpan(i * n, n));
        }

        converter.Convert(_components.AsSpan(0, Size * n), _rgb.AsSpan(0, Size * 3), Size);
        Span<float> rgb = _rgb.AsSpan(0, Size * 3);
        for (int i = 0; i < Size; i++)
        {
            _table[i] = Pack(rgb[3 * i], rgb[(3 * i) + 1], rgb[(3 * i) + 2]);
        }
    }

    /// <summary>The interval of t of a mesh with a function: its Decode pair for t, into which every input is forced (§8.7.4.5.5, Table 81).</summary>
    private static (double T0, double T1) MeshDomain(PdfMeshShading mesh)
    {
        IReadOnlyList<double> decode = mesh.Decode;
        return decode.Count >= 6 ? (decode[4], decode[5]) : (0, 1);
    }

    private static (double T0, double T1) Domain(PdfShading model) => model switch
    {
        PdfAxialShading axial => (axial.Domain[0], axial.Domain[1]),
        PdfRadialShading radial => (radial.Domain[0], radial.Domain[1]),
        PdfMeshShading mesh => MeshDomain(mesh),
        _ => (0, 1),
    };
}
