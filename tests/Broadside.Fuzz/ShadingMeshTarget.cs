using Broadside.Graphics;
using Broadside.Graphics.Shadings;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>shading-mesh</c> target (issue #79): the first six bytes choose a mesh layout (type 4 to 7, field widths from the
/// tables' sets or any width 1 to 32, values per vertex, lattice width, an Indexed palette), the rest is the mesh data, decoded
/// directly. Invariants: nothing throws, counts fit the arrays, every triangle index names a decoded vertex, every coordinate is
/// finite and inside its Decode range, and the work is bounded by the vertex limit.
/// </summary>
/// <remarks>ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8, §8.9.5.2.</remarks>
internal static class ShadingMeshTarget
{
    private static readonly int[] CoordinateWidths = [1, 2, 4, 8, 12, 16, 24, 32];
    private static readonly int[] ComponentWidths = [1, 2, 4, 8, 12, 16];
    private static readonly int[] FlagWidths = [2, 4, 8];

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            return;
        }

        int type = 4 + (data[0] & 3);
        bool odd = (data[0] & 4) != 0;
        int coordinateBits = odd ? 1 + (data[1] % 32) : CoordinateWidths[data[1] % CoordinateWidths.Length];
        int componentBits = odd ? 1 + (data[2] % 32) : ComponentWidths[data[2] % ComponentWidths.Length];
        int flagBits = type == 5 ? 0 : odd ? 1 + (data[3] % 32) : FlagWidths[data[3] % FlagWidths.Length];
        bool indexed = (data[0] & 8) != 0;
        int values = indexed ? 1 : 1 + (data[4] % 4);
        int perRow = 2 + (data[5] % 8);
        double[] decode = [-50, 50, 1000, 0, .. Enumerable.Repeat<double[]>([0, 1], values).SelectMany(pair => pair)];
        var layout = new MeshLayout
        {
            ShadingType = type,
            BitsPerCoordinate = coordinateBits,
            BitsPerComponent = componentBits,
            BitsPerFlag = flagBits,
            Decode = decode,
            ValueCount = values,
            VerticesPerRow = type == 5 ? perRow : 0,
            Palette = indexed ? new MeshPalette(data[5] % 4, data[..Math.Min(6, data.Length)], [new(0, 1), new(-1, 1)]) : null,
            MaxVertices = 1 << 16,
        };

        MeshData mesh = MeshDecoder.Decode(layout, data[6..]);
        int stride = layout.ColorStride;
        Check(mesh.Points.Length >= mesh.VertexCount && mesh.Colors.Length >= (layout.IsPatchMesh ? mesh.PatchCount * 4 : mesh.VertexCount) * stride, "counts exceed the arrays");
        Check(mesh.Triangles.Length >= mesh.TriangleCount * 3 && mesh.VertexCount <= layout.MaxVertices, "triangles exceed the array, or vertices the limit");
        Check(!layout.IsPatchMesh || mesh.VertexCount == mesh.PatchCount * 16, "patch vertex count");
        for (int i = 0; i < mesh.TriangleCount * 3; i++)
        {
            Check((uint)mesh.Triangles[i] < (uint)mesh.VertexCount, "a triangle names a vertex that was not decoded");
        }

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            PathPoint point = mesh.Points[i];
            bool inside = layout.IsPatchMesh && type == 6
                ? double.IsFinite(point.X) && double.IsFinite(point.Y)
                : point.X is >= -50 and <= 50 && point.Y is >= 0 and <= 1000;
            Check(inside, "a coordinate outside its Decode range");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("shading-mesh: " + message + ".");
        }
    }
}
