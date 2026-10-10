namespace Broadside.TestSupport;

/// <summary>
/// Large mesh shading data (ISO 32000-2 §8.7.4.5.5 and §8.7.4.5.8) for benchmarks and allocation tests, built deterministically:
/// a free-form triangle strip and a tensor-product patch mesh. Linked as source into the benchmarks.
/// </summary>
public static class MeshSamples
{
    /// <summary>The dictionary entries (PDF syntax, without the brackets) of <see cref="FreeForm"/> data.</summary>
    public const string FreeFormEntries =
        "/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 612 0 792 0 1 0 1 0 1]";

    /// <summary>The dictionary entries (PDF syntax, without the brackets) of <see cref="Tensor"/> data.</summary>
    public const string TensorEntries =
        "/ShadingType 7 /ColorSpace /DeviceCMYK /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 612 0 792 0 1 0 1 0 1 0 1]";

    /// <summary>A Type 4 strip of <paramref name="vertices"/> vertices: flags 0 0 0 then 1, so vertices − 2 triangles; 8 bytes per vertex.</summary>
    public static byte[] FreeForm(int vertices)
    {
        byte[] data = new byte[vertices * 8];
        for (int k = 0; k < vertices; k++)
        {
            Span<byte> vertex = data.AsSpan(k * 8, 8);
            vertex[0] = (byte)(k < 3 ? 0 : 1);
            Write16(vertex[1..], (ushort)(k * 7919));
            Write16(vertex[3..], (ushort)(k * 104729));
            vertex[5] = (byte)k;
            vertex[6] = (byte)(k >> 3);
            vertex[7] = (byte)(255 - k);
        }

        return data;
    }

    /// <summary>A Type 7 mesh of <paramref name="patches"/> patches: flag 0 first (16 points, 4 colours), then flags 1, 2, 3 in turn (12 points, 2 colours).</summary>
    public static byte[] Tensor(int patches)
    {
        var data = new List<byte>(patches * 72);
        for (int p = 0; p < patches; p++)
        {
            int flag = p == 0 ? 0 : 1 + ((p - 1) % 3);
            data.Add((byte)flag);
            int points = flag == 0 ? 16 : 12;
            for (int n = 0; n < points * 2; n++)
            {
                ushort value = (ushort)((p * 31) + (n * 977));
                data.Add((byte)(value >> 8));
                data.Add((byte)value);
            }

            for (int n = 0; n < (flag == 0 ? 16 : 8); n++)
            {
                data.Add((byte)(p + n));
            }
        }

        return [.. data];
    }

    private static void Write16(Span<byte> destination, ushort value)
    {
        destination[0] = (byte)(value >> 8);
        destination[1] = (byte)value;
    }
}
