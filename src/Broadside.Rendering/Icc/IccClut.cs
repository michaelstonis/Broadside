using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Broadside.Rendering.Icc;

/// <summary>
/// The multi-dimensional colour lookup table of a lookup-table tag: a grid over [0, 1] in each input, each grid point holding the
/// outputs, interpolated tetrahedrally for three inputs and multilinearly otherwise. Immutable; interpolates without allocating.
/// </summary>
/// <remarks>
/// ICC.1:2022 §10.10 and §10.11 (one grid-point count for every input), §10.12.3 and §10.13.5 (one per input, Tables 46 and 48):
/// the first input varies least rapidly, each grid point holds the outputs in order, values normalized to [0, 1]. The interpolation
/// method is the CMM's choice; tetrahedral interpolation is used for three inputs (it is exact on the grid's main diagonal, where
/// neutrals lie) and multilinear interpolation for any other number.
/// </remarks>
internal sealed class IccClut
{
    /// <summary>The most inputs or outputs of a lookup table (ICC.1:2022 §10.12.3: 16 grid-point entries; Table 41 NOTE: 15 channels).</summary>
    public const int MaxChannels = 15;

    private readonly int[] _grid;
    private readonly int[] _strides;
    private readonly float[] _values;

    /// <summary>Initializes a new instance of the <see cref="IccClut"/> class.</summary>
    /// <param name="grid">The number of grid points of each input, each at least 2.</param>
    /// <param name="outputs">The number of outputs.</param>
    /// <param name="values">The grid points' outputs, normalized, first input varying least rapidly.</param>
    public IccClut(int[] grid, int outputs, float[] values)
    {
        _grid = grid;
        _values = values;
        OutputCount = outputs;
        _strides = new int[grid.Length];
        int stride = outputs;
        for (int i = grid.Length - 1; i >= 0; i--)
        {
            _strides[i] = stride;
            stride *= grid[i];
        }
    }

    /// <summary>Gets the number of inputs.</summary>
    public int InputCount => _grid.Length;

    /// <summary>Gets the number of outputs.</summary>
    public int OutputCount { get; }

    /// <summary>Interpolates the table.</summary>
    /// <param name="input">At least <see cref="InputCount"/> values, clipped to [0, 1].</param>
    /// <param name="output">At least <see cref="OutputCount"/> values.</param>
    public void Interpolate(ReadOnlySpan<float> input, Span<float> output)
    {
        switch (_grid.Length)
        {
            case 3:
                Tetrahedral(input, output);
                break;
            case 4:
                Quadrilinear(input, output);
                break;
            default:
                Multilinear(input, output);
                break;
        }
    }

    /// <summary>The cell an input falls in: the offset of its lower grid point along that input, and the fraction across the cell.</summary>
    private int Cell(int dimension, float x, out float fraction)
    {
        int last = _grid[dimension] - 1;
        float position = (x > 0 ? MathF.Min(x, 1) : 0) * last;
        int index = Math.Min((int)position, last - 1);
        fraction = position - index;
        return index * _strides[dimension];
    }

    private void Tetrahedral(ReadOnlySpan<float> input, Span<float> output)
    {
        int x0 = Cell(0, input[0], out float rx);
        int y0 = Cell(1, input[1], out float ry);
        int z0 = Cell(2, input[2], out float rz);
        int sx = _strides[0];
        int sy = _strides[1];
        int sz = _strides[2];
        int origin = x0 + y0 + z0;

        // The tetrahedron of the cube holding the point is chosen by the order of the fractions; each output is the origin plus
        // the three edge differences along the path from the origin to the far corner, weighted by the fractions.
        int a;
        int b;
        int c;
        float wa;
        float wb;
        float wc;
        if (rx >= ry)
        {
            if (ry >= rz)
            {
                (a, b, c, wa, wb, wc) = (sx, sy, sz, rx, ry, rz);
            }
            else if (rx >= rz)
            {
                (a, b, c, wa, wb, wc) = (sx, sz, sy, rx, rz, ry);
            }
            else
            {
                (a, b, c, wa, wb, wc) = (sz, sx, sy, rz, rx, ry);
            }
        }
        else if (rx >= rz)
        {
            (a, b, c, wa, wb, wc) = (sy, sx, sz, ry, rx, rz);
        }
        else if (ry >= rz)
        {
            (a, b, c, wa, wb, wc) = (sy, sz, sx, ry, rz, rx);
        }
        else
        {
            (a, b, c, wa, wb, wc) = (sz, sy, sx, rz, ry, rx);
        }

        // Cell() keeps every corner inside the table, so the reads need no bounds checks.
        ref float v0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_values), origin);
        ref float v1 = ref Unsafe.Add(ref v0, a);
        ref float v2 = ref Unsafe.Add(ref v1, b);
        ref float v3 = ref Unsafe.Add(ref v2, c);
        for (int o = 0; o < OutputCount; o++)
        {
            float c0 = Unsafe.Add(ref v0, o);
            float c1 = Unsafe.Add(ref v1, o);
            float c2 = Unsafe.Add(ref v2, o);
            float c3 = Unsafe.Add(ref v3, o);
            output[o] = c0 + (wa * (c1 - c0)) + (wb * (c2 - c1)) + (wc * (c3 - c2));
        }
    }

    private void Quadrilinear(ReadOnlySpan<float> input, Span<float> output)
    {
        int origin = Cell(0, input[0], out float f0) + Cell(1, input[1], out float f1) + Cell(2, input[2], out float f2) + Cell(3, input[3], out float f3);
        int s0 = _strides[0];
        int s1 = _strides[1];
        int s2 = _strides[2];
        int s3 = _strides[3];

        // Linear interpolation along the last input, then the third, the second and the first; Cell() keeps every corner inside
        // the table, so the reads need no bounds checks.
        ref float first = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_values), origin);
        for (int o = 0; o < OutputCount; o++)
        {
            ref float low = ref Unsafe.Add(ref first, o);
            ref float high = ref Unsafe.Add(ref low, s0);
            output[o] = Lerp(Cube(ref low, s1, s2, s3, f1, f2, f3), Cube(ref high, s1, s2, s3, f1, f2, f3), f0);
        }
    }

    /// <summary>Trilinear interpolation of the cube at <paramref name="v"/> with the given strides and fractions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Cube(ref float v, int s1, int s2, int s3, float f1, float f2, float f3)
    {
        float c00 = Lerp(v, Unsafe.Add(ref v, s3), f3);
        float c01 = Lerp(Unsafe.Add(ref v, s2), Unsafe.Add(ref v, s2 + s3), f3);
        float c10 = Lerp(Unsafe.Add(ref v, s1), Unsafe.Add(ref v, s1 + s3), f3);
        float c11 = Lerp(Unsafe.Add(ref v, s1 + s2), Unsafe.Add(ref v, s1 + s2 + s3), f3);
        return Lerp(Lerp(c00, c01, f2), Lerp(c10, c11, f2), f1);
    }

    private void Multilinear(ReadOnlySpan<float> input, Span<float> output)
    {
        int inputs = _grid.Length;
        Span<float> fractions = stackalloc float[MaxChannels];
        int origin = 0;
        for (int d = 0; d < inputs; d++)
        {
            origin += Cell(d, input[d], out fractions[d]);
        }

        output[..OutputCount].Clear();
        float[] v = _values;
        for (int corner = 0; corner < 1 << inputs; corner++)
        {
            float weight = 1;
            int offset = origin;
            for (int d = 0; d < inputs; d++)
            {
                if ((corner & (1 << (inputs - 1 - d))) != 0)
                {
                    weight *= fractions[d];
                    offset += _strides[d];
                }
                else
                {
                    weight *= 1 - fractions[d];
                }
            }

            if (weight == 0)
            {
                continue;
            }

            for (int o = 0; o < OutputCount; o++)
            {
                output[o] += weight * v[offset + o];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float low, float high, float fraction) => low + (fraction * (high - low));
}
