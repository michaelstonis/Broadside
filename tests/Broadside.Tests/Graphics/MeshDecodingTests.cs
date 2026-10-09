using System.Globalization;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Mesh data (Types 4 to 7) for every combination of field widths the tables allow, written by a small encoder here and decoded
/// through <see cref="PdfDocument.GetShading"/>: every field read MSB first with the §8.9.5.2 Decode formula (within one ulp of
/// D<sub>min</sub> + raw × (D<sub>max</sub> − D<sub>min</sub>) / (2<sup>n</sup> − 1) in double), vertices padded to whole bytes for
/// Types 4 and 5, patches unpadded for Types 6 and 7, flags masked to their low two bits, and the triangle or patch topology.
/// </summary>
public class MeshDecodingTests
{
    private static readonly int[] CoordinateWidths = [1, 2, 4, 8, 12, 16, 24, 32];
    private static readonly int[] ComponentWidths = [1, 2, 4, 8, 12, 16];
    private static readonly int[] FlagWidths = [2, 4, 8];

    public static TheoryData<int, bool> TypesAndColorForms => new()
    {
        { 4, false }, { 4, true }, { 5, false }, { 5, true }, { 6, false }, { 6, true }, { 7, false }, { 7, true },
    };

    [Theory]
    [MemberData(nameof(TypesAndColorForms))]
    public void Every_combination_of_widths_decodes_to_the_values_and_topology_written(int type, bool function)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        int[] flagWidths = type == 5 ? [0] : FlagWidths;
        foreach (int coordinateBits in CoordinateWidths)
        {
            foreach (int componentBits in ComponentWidths)
            {
                foreach (int flagBits in flagWidths)
                {
                    var mesh = new Mesh(type, coordinateBits, componentBits, flagBits, function);
                    PdfMeshShading shading = Assert.IsAssignableFrom<PdfMeshShading>(document.GetShading(mesh.ToStream()));
                    mesh.Verify(shading);
                }
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    /// <summary>A mesh of known raw fields, its encoding, and the checks on what the document decodes.</summary>
    private sealed class Mesh
    {
        // Type 4: f=0 (three vertices), then 1, 2, 1, a new f=0 triangle, then 2: triangles worked out by hand from Table 81's rules.
        private static readonly int[] FreeFormFlags = [0, 0, 0, 1, 2, 1, 0, 0, 0, 2];
        private static readonly int[] FreeFormTriangles = [0, 1, 2, 1, 2, 3, 1, 3, 4, 3, 4, 5, 6, 7, 8, 6, 8, 9];

        // Type 5: 3 x 3, cells (V[i,j], V[i,j+1], V[i+1,j]) and (V[i,j+1], V[i+1,j], V[i+1,j+1]).
        private static readonly int[] LatticeTriangles = [0, 1, 3, 1, 3, 4, 1, 2, 4, 2, 4, 5, 3, 4, 6, 4, 6, 7, 4, 5, 7, 5, 7, 8];

        // Types 6 and 7: the stream order of the points as (i, j), and for flags 1 to 3 the previous patch's points that become p00..p03.
        private static readonly (int I, int J)[] Order =
            [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];

        private static readonly (int I, int J)[][] SharedEdge =
        [
            [(0, 3), (1, 3), (2, 3), (3, 3)],
            [(3, 3), (3, 2), (3, 1), (3, 0)],
            [(3, 0), (2, 0), (1, 0), (0, 0)],
        ];

        private readonly int _type;
        private readonly int _coordinateBits;
        private readonly int _componentBits;
        private readonly int _flagBits;
        private readonly bool _function;
        private readonly int _values;
        private readonly double[] _decode;
        private readonly List<(ulong X, ulong Y, ulong[] Colors)> _vertices = [];
        private readonly List<int> _flags = [];
        private ulong _seed = 0x9E3779B97F4A7C15;

        public Mesh(int type, int coordinateBits, int componentBits, int flagBits, bool function)
        {
            _type = type;
            _coordinateBits = coordinateBits;
            _componentBits = componentBits;
            _flagBits = flagBits;
            _function = function;
            _values = function ? 1 : 3;
            _decode = [-100, 900, 50, -250, .. Enumerable.Repeat<double[]>(function ? [0, 2] : [0, 1], _values).SelectMany(pair => pair)];
        }

        private string Label => string.Create(CultureInfo.InvariantCulture, $"Type {_type}, coordinates {_coordinateBits}, components {_componentBits}, flags {_flagBits}, function {_function}");

        public CosStream ToStream()
        {
            var writer = new BitWriter();
            switch (_type)
            {
                case 4:
                    foreach (int flag in FreeFormFlags)
                    {
                        WriteFlag(writer, flag);
                        WriteVertex(writer, Vertex());
                        writer.Align();
                    }

                    break;
                case 5:
                    for (int k = 0; k < 9; k++)
                    {
                        WriteVertex(writer, Vertex());
                        writer.Align();
                    }

                    break;
                default:
                    int points = _type == 7 ? 16 : 12;
                    for (int flag = 0; flag < 4; flag++)
                    {
                        _flags.Add(flag);
                        WriteFlag(writer, flag);
                        for (int n = flag == 0 ? 0 : 4; n < points; n++)
                        {
                            var vertex = Vertex(colors: false);
                            writer.Write(vertex.X, _coordinateBits);
                            writer.Write(vertex.Y, _coordinateBits);
                        }

                        for (int n = flag == 0 ? 0 : 2; n < 4; n++)
                        {
                            var corner = Vertex(point: false);
                            foreach (ulong value in corner.Colors)
                            {
                                writer.Write(value, _componentBits);
                            }
                        }
                    }

                    break;
            }

            string flags = _type == 5 ? "/VerticesPerRow 3" : $"/BitsPerFlag {_flagBits}";
            string colors = _function ? "/Function << /FunctionType 2 /Domain [0 2] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>" : string.Empty;
            string decode = string.Join(' ', _decode.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            return Stream(
                $"<< /ShadingType {_type} /ColorSpace /DeviceRGB /BitsPerCoordinate {_coordinateBits} /BitsPerComponent {_componentBits} {flags} /Decode [{decode}] {colors} >>",
                writer.ToArray());
        }

        public void Verify(PdfMeshShading shading)
        {
            Assert.True(shading.IsValid, Label);
            Assert.Equal(_values, shading.ColorStride);
            if (shading is PdfTriangleMeshShading triangles)
            {
                VerifyTriangles(triangles);
            }
            else
            {
                VerifyPatches(Assert.IsType<PdfPatchMeshShading>(shading));
            }
        }

        private void VerifyTriangles(PdfTriangleMeshShading shading)
        {
            Assert.True(_vertices.Count == shading.VertexCount, Label);
            for (int k = 0; k < _vertices.Count; k++)
            {
                AssertPoint(_vertices[k].X, _vertices[k].Y, shading.Vertices[k], k);
                AssertColors(_vertices[k].Colors, shading.VertexColors.Slice(k * _values, _values), k);
            }

            Assert.True((_type == 4 ? FreeFormTriangles : LatticeTriangles).AsSpan().SequenceEqual(shading.Triangles), Label);
        }

        private void VerifyPatches(PdfPatchMeshShading shading)
        {
            Assert.True(shading.PatchCount == 4, Label);
            int points = _type == 7 ? 16 : 12;
            int vertex = 0;
            for (int patch = 0; patch < 4; patch++)
            {
                if (patch > 0)
                {
                    for (int n = 0; n < 4; n++)
                    {
                        (int i, int j) = SharedEdge[patch - 1][n];
                        Assert.True(shading.GetControlPoint(patch - 1, i, j) == shading.GetControlPoint(patch, Order[n].I, Order[n].J), Label);
                    }
                }

                for (int n = patch == 0 ? 0 : 4; n < points; n++)
                {
                    (ulong x, ulong y, _) = _vertices[vertex++];
                    AssertPoint(x, y, shading.GetControlPoint(patch, Order[n].I, Order[n].J), patch);
                }

                ReadOnlySpan<float> corners = shading.CornerColors.Slice(patch * 4 * _values, 4 * _values);
                if (patch > 0)
                {
                    // c00 and c03 are the previous patch's colours at the corners it shares (Tables 84 and 85).
                    ReadOnlySpan<float> previous = shading.CornerColors.Slice((patch - 1) * 4 * _values, 4 * _values);
                    Assert.True(previous.Slice(patch * _values, _values).SequenceEqual(corners[.._values]), Label);
                    Assert.True(previous.Slice(((patch + 1) & 3) * _values, _values).SequenceEqual(corners.Slice(_values, _values)), Label);
                }

                for (int c = patch == 0 ? 0 : 2; c < 4; c++)
                {
                    AssertColors(_vertices[vertex++].Colors, corners.Slice(c * _values, _values), patch);
                }
            }
        }

        private void AssertPoint(ulong x, ulong y, PathPoint actual, int index)
        {
            AssertNear(Expected(x, _decode[0], _decode[1], _coordinateBits), actual.X, index, "x");
            AssertNear(Expected(y, _decode[2], _decode[3], _coordinateBits), actual.Y, index, "y");
        }

        private void AssertColors(ulong[] raw, ReadOnlySpan<float> actual, int index)
        {
            for (int v = 0; v < raw.Length; v++)
            {
                float expected = (float)Expected(raw[v], _decode[4 + (2 * v)], _decode[5 + (2 * v)], _componentBits);
                Assert.True(expected == actual[v], $"{Label}: colour {v} of {index}: expected {expected}, got {actual[v]}.");
            }
        }

        private void AssertNear(double expected, double actual, int index, string what) =>
            Assert.True(Math.Abs(expected - actual) <= Math.BitIncrement(Math.Abs(expected)) - Math.Abs(expected), $"{Label}: {what} of {index}: expected {expected}, got {actual}.");

        // §8.9.5.2, computed independently of the decoder's code: y = Dmin + x * (Dmax - Dmin) / (2^n - 1).
        private static double Expected(ulong raw, double min, double max, int bits) => min + (raw * (max - min) / (Math.Pow(2, bits) - 1));

        private (ulong X, ulong Y, ulong[] Colors) Vertex(bool point = true, bool colors = true)
        {
            ulong x = point ? Next(_coordinateBits) : 0;
            ulong y = point ? Next(_coordinateBits) : 0;
            ulong[] values = colors ? [.. Enumerable.Range(0, _values).Select(_ => Next(_componentBits))] : [];
            var vertex = (x, y, values);
            _vertices.Add(vertex);
            return vertex;
        }

        private void WriteVertex(BitWriter writer, (ulong X, ulong Y, ulong[] Colors) vertex)
        {
            writer.Write(vertex.X, _coordinateBits);
            writer.Write(vertex.Y, _coordinateBits);
            foreach (ulong value in vertex.Colors)
            {
                writer.Write(value, _componentBits);
            }
        }

        // Wider flags carry junk in their high bits, which readers mask off.
        private void WriteFlag(BitWriter writer, int flag) => writer.Write(_flagBits > 2 ? (ulong)(flag | 4) : (ulong)flag, _flagBits);

        // A deterministic sequence that reaches both ends of every width (all zeros, all ones) as well as values between.
        private ulong Next(int bits)
        {
            _seed = (_seed * 6364136223846793005) + 1442695040888963407;
            ulong mask = (1UL << bits) - 1;
            return (_seed >> 61) switch
            {
                0 => 0,
                1 => mask,
                _ => (_seed >> 11) & mask,
            };
        }
    }

    /// <summary>Packs fields most significant bit first, as mesh data is written (§8.7.4.5.5).</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        public void Write(ulong value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                if (_bit == 0)
                {
                    _bytes.Add(0);
                }

                if (((value >> i) & 1) != 0)
                {
                    _bytes[^1] |= (byte)(0x80 >> _bit);
                }

                _bit = (_bit + 1) & 7;
            }
        }

        public void Align() => _bit = 0;

        public byte[] ToArray() => [.. _bytes];
    }
}
