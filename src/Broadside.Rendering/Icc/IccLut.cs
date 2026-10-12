using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Broadside.Rendering.Icc;

/// <summary>The kind of lookup-table tag, which also fixes how the PCS side of the table is encoded.</summary>
internal enum IccLutKind
{
    /// <summary><c>mft1</c>, lut8Type (ICC.1:2022 §10.11): PCSLAB in the 8-bit encoding of Tables 12 and 13.</summary>
    Lut8,

    /// <summary><c>mft2</c>, lut16Type (ICC.1:2022 §10.10): PCSLAB in the legacy 16-bit encoding of Tables 42 and 43.</summary>
    Lut16,

    /// <summary><c>mAB </c>, lutAToBType (ICC.1:2022 §10.12): PCSLAB in the 16-bit encoding of Tables 12 and 13.</summary>
    AToB,

    /// <summary><c>mBA </c>, lutBToAType (ICC.1:2022 §10.13): PCSLAB in the 16-bit encoding of Tables 12 and 13.</summary>
    BToA,
}

/// <summary>
/// A lookup-table tag of an ICC profile, read into its processing elements in processing order: curve sets, matrices and a colour
/// lookup table, all working on values normalized to [0, 1]. Immutable; evaluates without allocating.
/// </summary>
/// <remarks>
/// <para>
/// ICC.1:2022 §10.10 lut16Type and §10.11 lut8Type: (matrix) → input tables → CLUT → output tables, where the matrix is applied only
/// when the input is PCSXYZ (it shall be the identity otherwise). §10.12 lutAToBType: A curves → CLUT → M curves → matrix → B
/// curves; §10.13 lutBToAType: B curves → matrix → M curves → CLUT → A curves. A missing element is not performed; the 3 × 4 matrix's
/// results are clipped to [0, 1] (§10.12.5, §10.13.3).
/// </para>
/// <para>
/// Reading is strict about what makes the tag unusable (counts that do not chain, data that runs past the tag, a CLUT with fewer than
/// two grid points in a dimension) and lenient about the rest: unused grid-point entries and padding bytes are not checked, and an
/// element in a combination §10.12.1 does not list (such as A curves without a CLUT) is performed when the channel counts allow it.
/// </para>
/// </remarks>
internal sealed class IccLut
{
    private readonly Element[] _elements;

    private IccLut(IccLutKind kind, int inputs, int outputs, Element[] elements)
    {
        Kind = kind;
        InputCount = inputs;
        OutputCount = outputs;
        _elements = elements;
    }

    /// <summary>Gets the kind of tag, which fixes the PCS encoding.</summary>
    public IccLutKind Kind { get; }

    /// <summary>Gets the number of input channels.</summary>
    public int InputCount { get; }

    /// <summary>Gets the number of output channels.</summary>
    public int OutputCount { get; }

    /// <summary>Reads a lookup-table tag.</summary>
    /// <param name="tag">The tag data element, from its type signature on.</param>
    /// <param name="pcsXyzInput">Whether the table's input is PCSXYZ (a BToA tag of a PCSXYZ profile), so a lut8Type or lut16Type matrix applies.</param>
    /// <param name="lut">The table.</param>
    /// <param name="problem">Why the tag cannot be used.</param>
    /// <returns>Whether the tag can be used.</returns>
    public static bool TryRead(ReadOnlySpan<byte> tag, bool pcsXyzInput, [NotNullWhen(true)] out IccLut? lut, [NotNullWhen(false)] out string? problem)
    {
        lut = null;
        if (tag.Length < 12)
        {
            problem = "it is too short for a lookup table";
            return false;
        }

        int inputs = tag[8];
        int outputs = tag[9];
        if (inputs is 0 or > IccClut.MaxChannels || outputs is 0 or > IccClut.MaxChannels)
        {
            problem = $"it has {inputs} input and {outputs} output channels";
            return false;
        }

        var elements = new List<Element>(5);
        bool read;
        IccLutKind kind;
        if (tag.StartsWith("mft2"u8))
        {
            kind = IccLutKind.Lut16;
            read = TryReadLegacy(tag, inputs, outputs, wide: true, pcsXyzInput, elements, out problem);
        }
        else if (tag.StartsWith("mft1"u8))
        {
            kind = IccLutKind.Lut8;
            read = TryReadLegacy(tag, inputs, outputs, wide: false, pcsXyzInput, elements, out problem);
        }
        else if (tag.StartsWith("mAB "u8))
        {
            kind = IccLutKind.AToB;
            read = TryReadMultiFunction(tag, inputs, outputs, aToB: true, elements, out problem);
        }
        else if (tag.StartsWith("mBA "u8))
        {
            kind = IccLutKind.BToA;
            read = TryReadMultiFunction(tag, inputs, outputs, aToB: false, elements, out problem);
        }
        else
        {
            problem = "it is not a lut8Type, lut16Type, lutAToBType or lutBToAType";
            return false;
        }

        if (!read)
        {
            problem ??= "it cannot be read";
            return false;
        }

        lut = new IccLut(kind, inputs, outputs, [.. elements]);
        return true;
    }

    /// <summary>Evaluates the table for one colour.</summary>
    /// <param name="input">At least <see cref="InputCount"/> normalized values; out-of-range values are clipped.</param>
    /// <param name="output">At least <see cref="OutputCount"/> normalized values.</param>
    /// <param name="scratch">At least 2 × <see cref="IccClut.MaxChannels"/> values to work in.</param>
    public void Evaluate(ReadOnlySpan<float> input, Span<float> output, Span<float> scratch)
    {
        Element[] elements = _elements;
        if (elements.Length == 0)
        {
            for (int i = 0; i < OutputCount; i++)
            {
                float x = input[i];
                output[i] = x > 0 ? MathF.Min(x, 1) : 0;
            }

            return;
        }

        // Every element clips what it reads (curves and the CLUT) or what it writes (the matrices), so the input goes straight in.
        Span<float> current = scratch[..IccClut.MaxChannels];
        Span<float> next = scratch.Slice(IccClut.MaxChannels, IccClut.MaxChannels);
        if (elements.Length == 1)
        {
            elements[0].Apply(input, output);
            return;
        }

        elements[0].Apply(input, current);
        for (int i = 1; i < elements.Length - 1; i++)
        {
            elements[i].Apply(current, next);
            Span<float> swap = current;
            current = next;
            next = swap;
        }

        elements[^1].Apply(current, output);
    }

    /// <summary>Reads lut16Type (Table 40) or lut8Type (Table 44).</summary>
    private static bool TryReadLegacy(ReadOnlySpan<byte> tag, int inputs, int outputs, bool wide, bool pcsXyzInput, List<Element> elements, [NotNullWhen(false)] out string? problem)
    {
        int grid = tag[10];
        if (grid < 2)
        {
            problem = $"its CLUT has {grid} grid points";
            return false;
        }

        int header = wide ? 52 : 48;
        if (tag.Length < header)
        {
            problem = "it is too short for its header";
            return false;
        }

        int inEntries = wide ? BinaryPrimitives.ReadUInt16BigEndian(tag[48..]) : 256;
        int outEntries = wide ? BinaryPrimitives.ReadUInt16BigEndian(tag[50..]) : 256;
        if (inEntries is < 2 or > 4096 || outEntries is < 2 or > 4096)
        {
            problem = $"its tables have {inEntries} and {outEntries} entries (2 to 4096 allowed)";
            return false;
        }

        int size = wide ? 2 : 1;
        long points = Points([.. Enumerable.Repeat(grid, inputs)]);
        long needed = header + ((((long)inputs * inEntries) + (points * outputs) + ((long)outputs * outEntries)) * size);
        if (needed > tag.Length)
        {
            problem = $"its tables and CLUT need {needed} bytes but it has {tag.Length}";
            return false;
        }

        if (pcsXyzInput && inputs == 3 && MatrixElement.TryReadLegacy(tag[12..48]) is { } matrix)
        {
            elements.Add(matrix);
        }

        int at = header;
        elements.Add(CurveElement.FromTables(tag[at..], inputs, inEntries, wide));
        at += inputs * inEntries * size;
        elements.Add(new ClutElement(new IccClut([.. Enumerable.Repeat(grid, inputs)], outputs, Samples(tag.Slice(at, (int)(points * outputs * size)), wide))));
        at += (int)(points * outputs * size);
        elements.Add(CurveElement.FromTables(tag[at..], outputs, outEntries, wide));
        elements.RemoveAll(static element => element is CurveElement { IsIdentity: true });
        problem = null;
        return true;
    }

    /// <summary>Reads lutAToBType (Table 45) or lutBToAType (Table 47).</summary>
    private static bool TryReadMultiFunction(ReadOnlySpan<byte> tag, int inputs, int outputs, bool aToB, List<Element> elements, [NotNullWhen(false)] out string? problem)
    {
        if (tag.Length < 32)
        {
            problem = "it is too short for its header";
            return false;
        }

        uint bOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[12..]);
        uint matrixOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[16..]);
        uint mOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[20..]);
        uint clutOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[24..]);
        uint aOffset = BinaryPrimitives.ReadUInt32BigEndian(tag[28..]);
        if ((bOffset | matrixOffset | mOffset | clutOffset | aOffset) == 0)
        {
            problem = "it has no processing elements";
            return false;
        }

        // The channel count between the CLUT and the matrix: the outputs for AToB (A → CLUT → M → matrix → B), the inputs for BToA.
        int matrixSide = aToB ? outputs : inputs;
        if (clutOffset == 0 && inputs != outputs)
        {
            problem = $"it has {inputs} inputs and {outputs} outputs but no CLUT";
            return false;
        }

        if (matrixOffset != 0 && matrixSide != 3)
        {
            problem = $"its matrix needs three channels, not {matrixSide}";
            return false;
        }

        Element? a = null;
        Element? clut = null;
        Element? m = null;
        Element? matrix = null;
        Element? b = null;
        if (!TryReadCurves(tag, aOffset, aToB ? inputs : outputs, "A", ref a, out problem)
            || !TryReadCurves(tag, mOffset, matrixSide, "M", ref m, out problem)
            || !TryReadCurves(tag, bOffset, aToB ? outputs : inputs, "B", ref b, out problem)
            || !TryReadClut(tag, clutOffset, inputs, outputs, ref clut, out problem)
            || !TryReadMatrix(tag, matrixOffset, ref matrix, out problem))
        {
            return false;
        }

        Element?[] order = aToB ? [a, clut, m, matrix, b] : [b, matrix, m, clut, a];
        foreach (Element? element in order)
        {
            if (element is not null and not CurveElement { IsIdentity: true })
            {
                elements.Add(element);
            }
        }

        problem = null;
        return true;
    }

    private static bool TryReadCurves(ReadOnlySpan<byte> tag, uint offset, int count, string name, ref Element? element, [NotNullWhen(false)] out string? problem)
    {
        problem = null;
        if (offset == 0)
        {
            return true;
        }

        if (offset >= (uint)tag.Length)
        {
            problem = $"its {name} curves lie outside it";
            return false;
        }

        var tables = new float[count][];
        bool identity = true;
        int at = (int)offset;
        for (int i = 0; i < count; i++)
        {
            if (at >= tag.Length)
            {
                problem = $"its {name} curve {i + 1} lies outside it";
                return false;
            }

            if (!IccCurve.TryRead(tag[at..], out IccCurve? curve, out int length, out string? reason))
            {
                problem = $"its {name} curve {i + 1} is unusable: {reason}";
                return false;
            }

            identity &= curve.IsIdentity;
            tables[i] = curve.ToLookupTable(IccTables.Size);
            at += (length + 3) & ~3;
        }

        element = new CurveElement(tables, identity);
        return true;
    }

    private static bool TryReadClut(ReadOnlySpan<byte> tag, uint offset, int inputs, int outputs, ref Element? element, [NotNullWhen(false)] out string? problem)
    {
        problem = null;
        if (offset == 0)
        {
            return true;
        }

        if (offset > (uint)(tag.Length - 20))
        {
            problem = "its CLUT lies outside it";
            return false;
        }

        ReadOnlySpan<byte> clut = tag[(int)offset..];
        int[] grid = new int[inputs];
        for (int i = 0; i < inputs; i++)
        {
            grid[i] = clut[i];
            if (grid[i] < 2)
            {
                problem = $"its CLUT has {grid[i]} grid points for input {i + 1}";
                return false;
            }
        }

        int precision = clut[16];
        if (precision is not (1 or 2))
        {
            problem = $"its CLUT precision is {precision} bytes, not 1 or 2";
            return false;
        }

        long bytes = Points(grid) * outputs * precision;
        if (bytes > clut.Length - 20)
        {
            problem = $"its CLUT needs {bytes} bytes but has {clut.Length - 20}";
            return false;
        }

        element = new ClutElement(new IccClut(grid, outputs, Samples(clut.Slice(20, (int)bytes), precision == 2)));
        return true;
    }

    private static bool TryReadMatrix(ReadOnlySpan<byte> tag, uint offset, ref Element? element, [NotNullWhen(false)] out string? problem)
    {
        problem = null;
        if (offset == 0)
        {
            return true;
        }

        if (offset > (uint)tag.Length || (uint)tag.Length - offset < 48)
        {
            problem = "its matrix lies outside it";
            return false;
        }

        float[] values = new float[12];
        for (int i = 0; i < 12; i++)
        {
            values[i] = BinaryPrimitives.ReadInt32BigEndian(tag[((int)offset + (4 * i))..]) / 65536f;
        }

        element = new MatrixElement(values);
        return true;
    }

    /// <summary>The number of grid points of a CLUT: the product of the counts, capped so it cannot overflow.</summary>
    private static long Points(int[] grid)
    {
        long points = 1;
        foreach (int count in grid)
        {
            points = Math.Min(points * count, int.MaxValue);
        }

        return points;
    }

    /// <summary>uInt8Number or uInt16Number values normalized to [0, 1].</summary>
    private static float[] Samples(ReadOnlySpan<byte> data, bool wide)
    {
        float[] values = new float[wide ? data.Length / 2 : data.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = wide ? BinaryPrimitives.ReadUInt16BigEndian(data[(2 * i)..]) / 65535f : data[i] / 255f;
        }

        return values;
    }

    /// <summary>A processing element: reads its inputs from one buffer and writes its outputs to the other.</summary>
    private abstract class Element
    {
        public abstract void Apply(ReadOnlySpan<float> input, Span<float> output);
    }

    /// <summary>One curve per channel, as interpolated tables (§10.6, §10.18, and the tables of §10.10 and §10.11).</summary>
    private sealed class CurveElement(float[][] tables, bool identity) : Element
    {
        public bool IsIdentity => identity;

        /// <summary>The input or output tables of a lut16Type or lut8Type, one after another.</summary>
        public static CurveElement FromTables(ReadOnlySpan<byte> data, int count, int entries, bool wide)
        {
            var tables = new float[count][];
            bool identity = true;
            int size = wide ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                tables[i] = Samples(data.Slice(i * entries * size, entries * size), wide);
                for (int j = 0; identity && j < entries; j++)
                {
                    identity = Math.Abs(tables[i][j] - (j / (float)(entries - 1))) < 1e-6f;
                }
            }

            return new CurveElement(tables, identity);
        }

        public override void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            for (int i = 0; i < tables.Length; i++)
            {
                output[i] = IccTables.Lookup(tables[i], input[i]);
            }
        }
    }

    /// <summary>A 3 × 3 matrix with offsets (§10.12.5, §10.13.3, Equations 17 and 19), its results clipped to [0, 1].</summary>
    private sealed class MatrixElement(float[] e) : Element
    {
        /// <summary>The 3 × 3 matrix of a lut16Type or lut8Type (Equations 12 and 14), or <see langword="null"/> when it is the identity.</summary>
        public static MatrixElement? TryReadLegacy(ReadOnlySpan<byte> data)
        {
            float[] values = new float[12];
            bool identity = true;
            for (int i = 0; i < 9; i++)
            {
                values[i] = BinaryPrimitives.ReadInt32BigEndian(data[(4 * i)..]) / 65536f;
                identity &= values[i] == (i % 4 == 0 ? 1 : 0);
            }

            return identity ? null : new MatrixElement(values);
        }

        public override void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            float x = input[0];
            float y = input[1];
            float z = input[2];
            output[0] = Clip((e[0] * x) + (e[1] * y) + (e[2] * z) + e[9]);
            output[1] = Clip((e[3] * x) + (e[4] * y) + (e[5] * z) + e[10]);
            output[2] = Clip((e[6] * x) + (e[7] * y) + (e[8] * z) + e[11]);
        }

        private static float Clip(float value) => value > 0 ? MathF.Min(value, 1) : 0;
    }

    private sealed class ClutElement(IccClut clut) : Element
    {
        public override void Apply(ReadOnlySpan<float> input, Span<float> output) => clut.Interpolate(input, output);
    }
}
