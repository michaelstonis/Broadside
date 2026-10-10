namespace Broadside.Graphics.Shadings;

/// <summary>
/// Decodes the data of mesh shadings (Types 4 to 7) into vertices, triangles, patch control points and colours: one allocation per
/// output array, nothing per vertex.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8, Tables 81 to 85. Fields are read most significant bit first and decoded with
/// §8.9.5.2's formula y = D<sub>min</sub> + x × (D<sub>max</sub> − D<sub>min</sub>) / (2<sup>n</sup> − 1), in double precision,
/// with the raw value unsigned (32-bit fields included).
/// </para>
/// <para>
/// Padding: every vertex of Types 4 and 5 occupies a whole number of bytes (§8.7.4.5.5, which Type 5 shares: "the same format").
/// Types 6 and 7 defer to Type 4 "for further details on the format of the data", read here as: each patch occupies a whole number
/// of bytes. pdf.js and PDFBox read patches back to back instead; the readings differ only when a patch's bit count is not a
/// multiple of 8. When the padded reading ends inside a patch and the continuous one consumes the data cleanly, the continuous
/// reading is used and recorded (<see cref="MeshIssues.PatchesUnpadded"/>). Edge flags use their low two bits.
/// </para>
/// <para>
/// Repairs (each reported once through <see cref="MeshData.Issues"/>): an incomplete trailing vertex, triangle or patch is dropped and
/// the decoded prefix kept; a Type 4 flag of 3, or a flag 1 or 2 with no triangle before it, skips that vertex; a patch flag other
/// than 0 with no patch before it drops that patch; a lattice's incomplete last row is dropped, and fewer than two rows give nothing.
/// </para>
/// </remarks>
internal static class MeshDecoder
{
    // The 12 boundary points of a patch in stream order, as (column i, row j); then the 4 inner points of a tensor patch.
    private static ReadOnlySpan<byte> Ring => [0x00, 0x01, 0x02, 0x03, 0x13, 0x23, 0x33, 0x32, 0x31, 0x30, 0x20, 0x10, 0x11, 0x12, 0x22, 0x21];

    // For flags 1, 2 and 3: the previous patch's points that become p00, p01, p02, p03 (Tables 84 and 85).
    private static ReadOnlySpan<byte> Shared => [0x03, 0x13, 0x23, 0x33, 0x33, 0x32, 0x31, 0x30, 0x30, 0x20, 0x10, 0x00];

    /// <summary>Decodes mesh data.</summary>
    /// <param name="layout">The layout, already validated.</param>
    /// <param name="data">The decoded stream data.</param>
    /// <returns>The mesh.</returns>
    public static MeshData Decode(MeshLayout layout, ReadOnlySpan<byte> data) => layout.ShadingType switch
    {
        4 => DecodeFreeForm(layout, data),
        5 => DecodeLattice(layout, data),
        _ => DecodePatches(layout, data),
    };

    private static MeshData DecodeFreeForm(MeshLayout layout, ReadOnlySpan<byte> data)
    {
        int bytesPerVertex = (layout.BitsPerFlag + (2 * layout.BitsPerCoordinate) + (layout.ValueCount * layout.BitsPerComponent) + 7) / 8;
        MeshIssues issues = MeshIssues.None;
        int max = Capacity(data.Length / bytesPerVertex, data.Length % bytesPerVertex != 0, layout.MaxVertices, ref issues);
        int stride = layout.ColorStride;
        var points = new PathPoint[max];
        float[] colors = new float[max * stride];
        int[] triangles = new int[Math.Max(0, max - 2) * 3];
        var fields = new Fields(layout);
        var reader = new MeshBitReader(data);
        int count = 0;
        int triangleCount = 0;
        int pending = 0;
        int a = -1;
        int b = -1;
        int c = -1;
        for (int k = 0; k < max; k++)
        {
            reader.Seek((long)k * bytesPerVertex * 8);
            uint flag = reader.Read(layout.BitsPerFlag) & 3;
            if (pending == 0 && flag != 0 && (flag == 3 || a < 0))
            {
                issues |= MeshIssues.FlagInvalid;
                continue;
            }

            fields.ReadVertex(ref reader, out points[count], colors.AsSpan(count * stride, stride));
            int d = count++;
            if (pending > 0)
            {
                if (--pending == 0)
                {
                    (a, b, c) = (d - 2, d - 1, d);
                    Add(triangles, ref triangleCount, a, b, c);
                }
            }
            else if (flag == 0)
            {
                pending = 2;
            }
            else
            {
                (a, b, c) = flag == 1 ? (b, c, d) : (a, c, d);
                Add(triangles, ref triangleCount, a, b, c);
            }
        }

        if (pending > 0)
        {
            count -= 3 - pending;
            issues |= MeshIssues.Truncated;
        }

        return new MeshData(points, colors, triangles, count, triangleCount, 0, issues);
    }

    private static MeshData DecodeLattice(MeshLayout layout, ReadOnlySpan<byte> data)
    {
        int bytesPerVertex = ((2 * layout.BitsPerCoordinate) + (layout.ValueCount * layout.BitsPerComponent) + 7) / 8;
        MeshIssues issues = MeshIssues.None;
        int available = Capacity(data.Length / bytesPerVertex, data.Length % bytesPerVertex != 0, layout.MaxVertices, ref issues);
        int perRow = layout.VerticesPerRow;
        int rows = available / perRow;
        if (available % perRow != 0 || rows < 2)
        {
            issues |= MeshIssues.LatticeIncomplete;
        }

        if (rows < 2)
        {
            return new MeshData([], [], [], 0, 0, 0, issues);
        }

        int count = rows * perRow;
        int stride = layout.ColorStride;
        var points = new PathPoint[count];
        float[] colors = new float[count * stride];
        int[] triangles = new int[2 * (rows - 1) * (perRow - 1) * 3];
        var fields = new Fields(layout);
        var reader = new MeshBitReader(data);
        for (int k = 0; k < count; k++)
        {
            reader.Seek((long)k * bytesPerVertex * 8);
            fields.ReadVertex(ref reader, out points[k], colors.AsSpan(k * stride, stride));
        }

        int triangleCount = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            for (int j = 0; j < perRow - 1; j++)
            {
                int v = (i * perRow) + j;
                Add(triangles, ref triangleCount, v, v + 1, v + perRow);
                Add(triangles, ref triangleCount, v + 1, v + perRow, v + perRow + 1);
            }
        }

        return new MeshData(points, colors, triangles, count, triangleCount, 0, issues);
    }

    private static MeshData DecodePatches(MeshLayout layout, ReadOnlySpan<byte> data)
    {
        MeshData padded = DecodePatches(layout, data, padded: true);
        if ((padded.Issues & MeshIssues.Truncated) == 0 || PatchesAreWholeBytes(layout))
        {
            return padded;
        }

        MeshData continuous = DecodePatches(layout, data, padded: false);
        return (continuous.Issues & MeshIssues.Truncated) == 0 ? continuous.With(MeshIssues.PatchesUnpadded) : padded;
    }

    /// <summary>Returns whether every patch, full or sharing an edge, takes a whole number of bytes, so both readings agree.</summary>
    private static bool PatchesAreWholeBytes(MeshLayout layout)
    {
        int pointCount = layout.ShadingType == 7 ? 16 : 12;
        long pointBits = 2L * layout.BitsPerCoordinate;
        long colorBits = (long)layout.ValueCount * layout.BitsPerComponent;
        long full = layout.BitsPerFlag + (pointCount * pointBits) + (4 * colorBits);
        long shared = layout.BitsPerFlag + ((pointCount - 4) * pointBits) + (2 * colorBits);
        return full % 8 == 0 && shared % 8 == 0;
    }

    private static MeshData DecodePatches(MeshLayout layout, ReadOnlySpan<byte> data, bool padded)
    {
        bool tensor = layout.ShadingType == 7;
        int pointCount = tensor ? 16 : 12;
        int pointBits = 2 * layout.BitsPerCoordinate;
        int colorBits = layout.ValueCount * layout.BitsPerComponent;
        long minimumBits = layout.BitsPerFlag + ((long)(pointCount - 4) * pointBits) + (2L * colorBits);
        long totalBits = (long)data.Length * 8;
        MeshIssues issues = MeshIssues.None;
        int max = (int)Math.Min(totalBits / minimumBits, int.MaxValue);
        if (max > layout.MaxVertices / 16)
        {
            max = layout.MaxVertices / 16;
            issues |= MeshIssues.LimitExceeded;
        }

        int stride = layout.ColorStride;
        var points = new PathPoint[max * 16];
        float[] colors = new float[max * 4 * stride];
        var fields = new Fields(layout);
        var reader = new MeshBitReader(data);
        int patches = 0;
        while (patches < max && reader.RemainingBits >= layout.BitsPerFlag)
        {
            long start = reader.Position;
            uint flag = reader.Read(layout.BitsPerFlag) & 3;
            int explicitPoints = flag == 0 ? pointCount : pointCount - 4;
            int explicitColors = flag == 0 ? 4 : 2;
            long needed = ((long)explicitPoints * pointBits) + ((long)explicitColors * colorBits);
            if (reader.RemainingBits < needed)
            {
                // Fewer than 8 bits left over are the padding of the last byte, not a patch.
                issues |= totalBits - start >= 8 ? MeshIssues.Truncated : MeshIssues.None;
                break;
            }

            if (flag != 0 && patches == 0)
            {
                issues |= MeshIssues.FlagInvalid;
                reader.Seek(reader.Position + needed);
                if (padded)
                {
                    reader.AlignToByte();
                }

                continue;
            }

            Span<PathPoint> patch = points.AsSpan(patches * 16, 16);
            Span<float> corners = colors.AsSpan(patches * 4 * stride, 4 * stride);
            int first = 0;
            if (flag != 0)
            {
                ReadOnlySpan<PathPoint> previous = points.AsSpan((patches - 1) * 16, 16);
                ReadOnlySpan<float> previousCorners = colors.AsSpan((patches - 1) * 4 * stride, 4 * stride);
                int shared = (int)(flag - 1) * 4;
                for (int n = 0; n < 4; n++)
                {
                    patch[Index(Ring[n])] = previous[Index(Shared[shared + n])];
                }

                // c00 and c03 take the previous patch's colours at its corners shared as p00 and p03 (Tables 84 and 85).
                int firstCorner = (int)flag;
                previousCorners.Slice(firstCorner * stride, stride).CopyTo(corners);
                previousCorners.Slice(((firstCorner + 1) & 3) * stride, stride).CopyTo(corners[stride..]);
                first = 4;
            }

            for (int n = first; n < pointCount; n++)
            {
                patch[Index(Ring[n])] = fields.ReadPoint(ref reader);
            }

            for (int n = 4 - explicitColors; n < 4; n++)
            {
                fields.ReadColor(ref reader, corners.Slice(n * stride, stride));
            }

            if (!tensor)
            {
                CoonsInterior(patch);
            }

            patches++;
            if (padded)
            {
                reader.AlignToByte();
            }
        }

        return new MeshData(points, colors, [], patches * 16, 0, patches, issues);
    }

    /// <summary>The index in a patch's 16 points of p<sub>ij</sub> packed as 0xij: i × 4 + j.</summary>
    private static int Index(byte packed) => ((packed >> 4) * 4) + (packed & 0xF);

    /// <summary>The four interior points of a Coons patch, which make it the equivalent tensor-product patch (§8.7.4.5.8).</summary>
    private static void CoonsInterior(Span<PathPoint> p)
    {
        // p.266: p11 = (−4 p00 + 6(p01 + p10) − 2(p03 + p30) + 3(p31 + p13) − p33) / 9, and symmetrically for p12, p21, p22.
        p[5] = Inner(p, 0x00, 0x01, 0x10, 0x03, 0x30, 0x31, 0x13, 0x33);
        p[6] = Inner(p, 0x03, 0x02, 0x13, 0x00, 0x33, 0x32, 0x10, 0x30);
        p[9] = Inner(p, 0x30, 0x31, 0x20, 0x33, 0x00, 0x01, 0x23, 0x03);
        p[10] = Inner(p, 0x33, 0x32, 0x23, 0x30, 0x03, 0x02, 0x20, 0x00);
    }

    private static PathPoint Inner(ReadOnlySpan<PathPoint> p, byte corner, byte a1, byte a2, byte b1, byte b2, byte c1, byte c2, byte opposite)
    {
        PathPoint k = p[Index(corner)];
        PathPoint pa1 = p[Index(a1)];
        PathPoint pa2 = p[Index(a2)];
        PathPoint pb1 = p[Index(b1)];
        PathPoint pb2 = p[Index(b2)];
        PathPoint pc1 = p[Index(c1)];
        PathPoint pc2 = p[Index(c2)];
        PathPoint o = p[Index(opposite)];
        return new PathPoint(
            ((-4 * k.X) + (6 * (pa1.X + pa2.X)) - (2 * (pb1.X + pb2.X)) + (3 * (pc1.X + pc2.X)) - o.X) / 9,
            ((-4 * k.Y) + (6 * (pa1.Y + pa2.Y)) - (2 * (pb1.Y + pb2.Y)) + (3 * (pc1.Y + pc2.Y)) - o.Y) / 9);
    }

    private static int Capacity(int whole, bool partial, int limit, ref MeshIssues issues)
    {
        if (whole > limit)
        {
            issues |= MeshIssues.LimitExceeded;
            return limit;
        }

        if (partial)
        {
            issues |= MeshIssues.Truncated;
        }

        return whole;
    }

    private static void Add(int[] triangles, ref int count, int a, int b, int c)
    {
        int at = count++ * 3;
        triangles[at] = a;
        triangles[at + 1] = b;
        triangles[at + 2] = c;
    }

    /// <summary>The per-field constants of a layout, computed once per decode.</summary>
    private readonly struct Fields
    {
        private readonly MeshLayout _layout;
        private readonly double[] _decode;
        private readonly double _coordinateDenominator;
        private readonly double _componentDenominator;

        public Fields(MeshLayout layout)
        {
            _layout = layout;
            _decode = layout.Decode;
            _coordinateDenominator = Denominator(layout.BitsPerCoordinate);
            _componentDenominator = Denominator(layout.BitsPerComponent);
        }

        public PathPoint ReadPoint(ref MeshBitReader reader)
        {
            int bits = _layout.BitsPerCoordinate;
            double x = Value(reader.Read(bits), _decode[0], _decode[1], _coordinateDenominator);
            double y = Value(reader.Read(bits), _decode[2], _decode[3], _coordinateDenominator);
            return new PathPoint(x, y);
        }

        public void ReadVertex(ref MeshBitReader reader, out PathPoint point, Span<float> color)
        {
            point = ReadPoint(ref reader);
            ReadColor(ref reader, color);
        }

        public void ReadColor(ref MeshBitReader reader, Span<float> color)
        {
            int bits = _layout.BitsPerComponent;
            if (_layout.Palette is { } palette)
            {
                palette.Convert(Value(reader.Read(bits), _decode[4], _decode[5], _componentDenominator), color);
                return;
            }

            for (int v = 0; v < _layout.ValueCount; v++)
            {
                color[v] = (float)Value(reader.Read(bits), _decode[4 + (2 * v)], _decode[5 + (2 * v)], _componentDenominator);
            }
        }

        private static double Denominator(int bits) => (double)((1UL << bits) - 1);

        private static double Value(uint raw, double minimum, double maximum, double denominator) =>
            minimum + ((raw * (maximum - minimum)) / denominator);
    }
}
