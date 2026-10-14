using System.Buffers.Binary;
using System.Globalization;
using Broadside.Graphics.Shadings;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// Collects the vertices or patches of a mesh shading (Types 4 to 7) and checks each as it is added; pass it to
/// <see cref="PdfDocument.CreateMeshShading(PdfFreeFormMeshBuilder, PdfShadingOptions?)"/> or its overloads to write the shading.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8, Tables 81 to 85. A mesh's colours are given either as colours of
/// <see cref="ColorSpace"/> or, when the mesh has a <see cref="Function"/>, as one parametric value t each, which the function maps
/// to a colour. Input that does not fit the mesh (a colour where a t is expected or the reverse, a colour of another space or with
/// the wrong number of components, a component outside its range, a t outside the function's domain, a coordinate that is not
/// finite) is refused with an <see cref="ArgumentException"/> when it is added, and nothing is added.
/// </para>
/// <para>
/// The data is written with 32-bit coordinates, 16-bit colour components (8-bit indices for an Indexed space) and 8-bit edge flags,
/// every value starting on a byte boundary. The Decode array maps coordinates over the bounds of the points given, colour components
/// over their ranges in the colour space (§8.6), and t over the function's domain, so a value re-reads within 1/65535 of its range.
/// </para>
/// </remarks>
public abstract class PdfMeshBuilder
{
    private readonly List<double> _coordinates = [];
    private readonly List<double> _values = [];
    private readonly List<Record> _records = [];
    private readonly double _parameterMinimum;
    private readonly double _parameterMaximum;

    private protected PdfMeshBuilder(PdfColorSpace colorSpace, PdfFunction? function)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (colorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            throw new ArgumentException("A shading's colour space shall not be a Pattern space (ISO 32000-2 §8.7.4.3, Table 77).", nameof(colorSpace));
        }

        if (function is not null)
        {
            if (colorSpace.Family == PdfColorSpaceFamily.Indexed)
            {
                throw new ArgumentException("A mesh with a Function shall not use an Indexed colour space (ISO 32000-2 Tables 81 to 83).", nameof(function));
            }

            if (!function.IsValid || function.InputCount != 1 || function.OutputCount != colorSpace.ComponentCount || function.Domain.Count < 2)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A mesh's function shall be a usable function of 1 input to {colorSpace.ComponentCount} output(s), the components of its colour space (ISO 32000-2 Table 81)."),
                    nameof(function));
            }

            _parameterMinimum = function.Domain[0];
            _parameterMaximum = function.Domain[1];
        }

        ColorSpace = colorSpace;
        Function = function;
    }

    /// <summary>Gets the colour space the mesh's colours are in (<c>ColorSpace</c>).</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77.</remarks>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Gets the function that maps each parametric value t to a colour, or <see langword="null"/> when vertices carry colours.</summary>
    /// <remarks>ISO 32000-2 Tables 81 to 83 (<c>Function</c>).</remarks>
    public PdfFunction? Function { get; }

    /// <summary>Gets the shading type the builder writes.</summary>
    internal abstract PdfShadingType ShadingType { get; }

    /// <summary>Gets the number of values per colour: 1 (t) with a function, else the colour space's components.</summary>
    private int ValueCount => Function is null ? ColorSpace.ComponentCount : 1;

    private bool IsIndexed => ColorSpace.Family == PdfColorSpaceFamily.Indexed;

    /// <summary>Throws when the mesh cannot be written yet (no triangle, too few rows, no patch).</summary>
    internal abstract void ThrowIfIncomplete();

    /// <summary>Writes the mesh as a shading stream's dictionary entries and data (without the common entries of Table 77).</summary>
    internal CosStream Encode(CosDictionary dictionary)
    {
        ThrowIfIncomplete();
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity, yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        for (int i = 0; i < _coordinates.Count; i += 2)
        {
            xMin = Math.Min(xMin, _coordinates[i]);
            xMax = Math.Max(xMax, _coordinates[i]);
            yMin = Math.Min(yMin, _coordinates[i + 1]);
            yMax = Math.Max(yMax, _coordinates[i + 1]);
        }

        (xMin, xMax) = Widen(xMin, xMax);
        (yMin, yMax) = Widen(yMin, yMax);
        int valueCount = ValueCount;
        double[] decode = new double[4 + (2 * valueCount)];
        decode[0] = xMin;
        decode[1] = xMax;
        decode[2] = yMin;
        decode[3] = yMax;
        for (int v = 0; v < valueCount; v++)
        {
            (decode[4 + (2 * v)], decode[5 + (2 * v)]) = ValueRange(v);
        }

        bool hasFlags = ShadingType != PdfShadingType.LatticeFormTriangleMesh;
        int componentBytes = IsIndexed ? 1 : 2;
        int size = 0;
        foreach (Record record in _records)
        {
            size += (hasFlags ? 1 : 0) + (record.Points * 8) + (record.Colors * valueCount * componentBytes);
        }

        byte[] data = new byte[size];
        int offset = 0;
        int point = 0;
        int value = 0;
        foreach (Record record in _records)
        {
            if (hasFlags)
            {
                data[offset++] = record.Flag;
            }

            for (int p = 0; p < record.Points; p++, point++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), (uint)Quantize(_coordinates[2 * point], xMin, xMax, uint.MaxValue));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 4), (uint)Quantize(_coordinates[(2 * point) + 1], yMin, yMax, uint.MaxValue));
                offset += 8;
            }

            for (int c = 0; c < record.Colors * valueCount; c++, value++)
            {
                int v = c % valueCount;
                if (IsIndexed)
                {
                    data[offset++] = (byte)_values[value];
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), (ushort)Quantize(_values[value], decode[4 + (2 * v)], decode[5 + (2 * v)], ushort.MaxValue));
                    offset += 2;
                }
            }
        }

        dictionary[ShadingNames.BitsPerCoordinate] = new CosInteger(32);
        dictionary[ShadingNames.BitsPerComponent] = new CosInteger(componentBytes * 8);
        if (hasFlags)
        {
            dictionary[ShadingNames.BitsPerFlag] = new CosInteger(8);
        }

        dictionary[ShadingNames.Decode] = DrawingCache.Numbers(decode);
        return new CosStream(dictionary, data);
    }

    /// <summary>Checks a vertex's coordinates and colour against the mesh, without adding it.</summary>
    private protected void Check(in PdfMeshVertex vertex, string parameter)
    {
        CheckPoint(vertex.Point, parameter);
        if (vertex.IsParametric)
        {
            CheckParameter(vertex.Parameter, parameter);
        }
        else
        {
            CheckColor(vertex.Color, parameter);
        }
    }

    /// <summary>Checks that a point's coordinates are finite.</summary>
    private protected static void CheckPoint(PathPoint point, string parameter)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException("Mesh coordinates shall be finite numbers.", parameter);
        }
    }

    /// <summary>Checks a colour against the mesh: no function, the mesh's space, its component count, each component in range.</summary>
    private protected void CheckColor(in PdfColor color, string parameter)
    {
        if (Function is not null)
        {
            throw new ArgumentException("The mesh has a Function: give each vertex or corner a parametric value t, not a colour (ISO 32000-2 Table 81).", parameter);
        }

        if (!ReferenceEquals(color.ColorSpace, ColorSpace) && !(IsDevice(color.ColorSpace) && color.ColorSpace.Family == ColorSpace.Family))
        {
            throw new ArgumentException("A colour of the mesh shall be in the mesh's colour space.", parameter);
        }

        if (color.ComponentCount != ColorSpace.ComponentCount)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"A colour has {color.ComponentCount} components; the mesh's colour space has {ColorSpace.ComponentCount}."),
                parameter);
        }

        for (int i = 0; i < color.ComponentCount; i++)
        {
            float component = color.Components[i];
            ComponentRange range = ColorSpace.GetComponentRange(i);
            if (!float.IsFinite(component) || component < range.Minimum || component > range.Maximum
                || (IsIndexed && component != MathF.Floor(component)))
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Colour component {i} is {component}; it shall be {(IsIndexed ? "an integer index" : "a number")} from {range.Minimum} to {range.Maximum}."),
                    parameter);
            }
        }
    }

    /// <summary>Checks a parametric value against the mesh: it has a function, and t is within the function's domain.</summary>
    private protected void CheckParameter(double t, string parameter)
    {
        if (Function is null)
        {
            throw new ArgumentException("The mesh has no Function: give each vertex or corner a colour, not a parametric value.", parameter);
        }

        if (!double.IsFinite(t) || t < _parameterMinimum || t > _parameterMaximum)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The parametric value {t} is outside the function's domain [{_parameterMinimum} {_parameterMaximum}] (ISO 32000-2 Table 81)."),
                parameter);
        }
    }

    /// <summary>Adds one vertex record (Types 4 and 5): its flag, its point and its colour, already checked.</summary>
    private protected void AddVertex(byte flag, in PdfMeshVertex vertex)
    {
        _records.Add(new Record(flag, 1, 1));
        AddPoint(vertex.Point);
        if (vertex.IsParametric)
        {
            _values.Add(vertex.Parameter);
        }
        else
        {
            AddColor(vertex.Color);
        }
    }

    /// <summary>Adds one patch record (Types 6 and 7), its points and colours already checked.</summary>
    private protected void AddPatch(ReadOnlySpan<PathPoint> points, ReadOnlySpan<PdfColor> colors, ReadOnlySpan<double> parameters)
    {
        _records.Add(new Record(0, points.Length, 4));
        foreach (PathPoint point in points)
        {
            AddPoint(point);
        }

        foreach (PdfColor color in colors)
        {
            AddColor(color);
        }

        foreach (double t in parameters)
        {
            _values.Add(t);
        }
    }

    private static bool IsDevice(PdfColorSpace space) =>
        space.Family is PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk;

    private static (double Minimum, double Maximum) Widen(double minimum, double maximum) =>
        maximum > minimum ? (minimum, maximum) : (minimum, minimum + 1);

    private static ulong Quantize(double value, double minimum, double maximum, ulong codes) =>
        (ulong)Math.Clamp(Math.Round((value - minimum) / (maximum - minimum) * codes), 0, codes);

    private (double Minimum, double Maximum) ValueRange(int index)
    {
        if (Function is not null)
        {
            return Widen(_parameterMinimum, _parameterMaximum);
        }

        if (IsIndexed)
        {
            return (0, 255);
        }

        ComponentRange range = ColorSpace.GetComponentRange(index);
        return Widen(range.Minimum, range.Maximum);
    }

    private void AddPoint(PathPoint point)
    {
        _coordinates.Add(point.X);
        _coordinates.Add(point.Y);
    }

    private void AddColor(in PdfColor color)
    {
        foreach (float component in color.Components)
        {
            _values.Add(component);
        }
    }

    private readonly record struct Record(byte Flag, int Points, int Colors);
}
