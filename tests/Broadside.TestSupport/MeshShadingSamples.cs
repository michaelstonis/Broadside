using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Synthetic pages of function-based, triangle-mesh and patch-mesh shadings for allocation tests and benchmarks (ISO 32000-2
/// §8.7.4.5.2, §8.7.4.5.5 to §8.7.4.5.8). Linked as source into the benchmarks.
/// </summary>
public static class MeshShadingSamples
{
    /// <summary>
    /// A 612 x 792 page: a grid of <paramref name="columns"/> × <paramref name="rows"/> cells cycling through every mesh paint the
    /// rasterizer has: <c>sh</c> of a function-based shading (a PostScript function), <c>sh</c> of a DeviceRGB free-form strip of
    /// <paramref name="stripVertices"/> vertices, <c>sh</c> of a DeviceCMYK free-form strip (split where the conversion bends), a
    /// fill in a lattice pattern with a function and a Background, a stroke in the RGB strip's pattern, and a fill in a
    /// function-based pattern with a BBox and a Background.
    /// </summary>
    public static byte[] Grid(int columns = 6, int rows = 8, int stripVertices = 64)
    {
        var text = new StringBuilder();
        double width = 612.0 / columns;
        double height = 792.0 / rows;
        int cell = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++, cell++)
            {
                double x = column * width;
                double y = row * height;
                text.Append(Format("q 1 0 0 1 {0} {1} cm 0 0 {2} {3} re W n ", x, y, width, height));
                text.Append((cell % 6) switch
                {
                    0 => "/Fn sh ",
                    1 => "/Rgb sh ",
                    2 => "/Cmyk sh ",
                    3 => Format("/Pattern cs /Lat scn 5 5 {0} {1} re f ", width - 10, height - 10),
                    4 => Format("/Pattern CS /Str SCN 6 w 5 5 m {0} {1} l S ", width - 5, height - 5),
                    _ => Format("/Pattern cs /Box scn 5 5 {0} {1} re f ", width - 10, height - 10),
                });
                text.Append("Q\n");
            }
        }

        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(text.ToString()),
            "/Shading << /Fn 5 0 R /Rgb 6 0 R /Cmyk 7 0 R >> /Pattern << /Lat 8 0 R /Str 9 0 R /Box 10 0 R >>",
            "<< /ShadingType 1 /ColorSpace /DeviceRGB /Matrix [90 0 30 80 5 5] /Function 11 0 R >>",
            Strip("/ColorSpace /DeviceRGB", stripVertices, 3),
            Strip("/ColorSpace /DeviceCMYK", stripVertices, 4),
            "<< /Type /Pattern /PatternType 2 /Shading 12 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Shading 6 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 3 0] /Shading << /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] "
            + "/Matrix [60 0 0 60 10 10] /BBox [0 0 50 90] /Background [0.9 0.9 0.8] /Function 11 0 R >> >>",
            ContentSamples.Stream("{ 2 copy mul }", "/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]"),
            Lattice(8, 8));
    }

    /// <summary>
    /// A 612 x 792 page of patch meshes (ISO 32000-2 §8.7.4.5.7, §8.7.4.5.8): a grid of <paramref name="columns"/> ×
    /// <paramref name="rows"/> cells cycling through <c>sh</c> of a DeviceRGB Coons mesh, <c>sh</c> of a DeviceCMYK tensor-product
    /// mesh (split where the conversion bends), a fill in a Coons pattern with a Function and a Background, and a stroke in the RGB
    /// mesh's pattern; each mesh is <paramref name="across"/> × <paramref name="across"/> patches with wavy edges.
    /// </summary>
    public static byte[] Patches(int columns = 4, int rows = 6, int across = 4)
    {
        var text = new StringBuilder();
        double width = 612.0 / columns;
        double height = 792.0 / rows;
        int cell = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++, cell++)
            {
                text.Append(Format("q 1 0 0 1 {0} {1} cm 0 0 {2} {3} re W n ", column * width, row * height, width, height));
                text.Append((cell % 4) switch
                {
                    0 => "/Coons sh ",
                    1 => "/Tensor sh ",
                    2 => Format("/Pattern cs /Fn scn 5 5 {0} {1} re f ", width - 10, height - 10),
                    _ => Format("/Pattern CS /Str SCN 6 w 5 5 m {0} {1} l S ", width - 5, height - 5),
                });
                text.Append("Q\n");
            }
        }

        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(text.ToString()),
            "/Shading << /Coons 5 0 R /Tensor 6 0 R >> /Pattern << /Fn 7 0 R /Str 8 0 R >>",
            PatchGrid("/ShadingType 6 /ColorSpace /DeviceRGB", across, tensor: false, components: 3),
            PatchGrid("/ShadingType 7 /ColorSpace /DeviceCMYK", across, tensor: true, components: 4),
            "<< /Type /Pattern /PatternType 2 /Shading 9 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Shading 5 0 R >>",
            PatchGrid("/ShadingType 6 /ColorSpace /DeviceRGB /Background [0.2 0.2 0.2] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0.5 0] /C1 [0 0.2 1] /N 1.5 >>", across, tensor: false, components: 1));
    }

    /// <summary>
    /// A patch mesh of <paramref name="across"/> × <paramref name="across"/> patches over a 100 x 100 cell, every patch written in
    /// full (flag 0) with 8-bit fields; every control point but the corners is moved off the straight lattice, so the patches are curved and share
    /// their edges point for point.
    /// </summary>
    private static string PatchGrid(string entries, int across, bool tensor, int components)
    {
        // Grid point (i, j) of the 3·across + 1 control lattice, bowed at the interior points.
        (int X, int Y) At(int i, int j)
        {
            int n = 3 * across;
            int x = i * 255 / n;
            int y = j * 255 / n;
            int bow = i % 3 == 0 && j % 3 == 0 ? 0 : 6;
            return (Math.Clamp(x + (((i + j) % 2 == 0) ? bow : -bow), 0, 255), Math.Clamp(y + (((i / 3) % 2 == 0) ? bow : -bow), 0, 255));
        }

        (int I, int J)[] ring = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0)];
        (int I, int J)[] inner = [(1, 1), (1, 2), (2, 2), (2, 1)];
        var data = new StringBuilder();
        for (int pj = 0; pj < across; pj++)
        {
            for (int pi = 0; pi < across; pi++)
            {
                data.Append('\0');
                foreach ((int i, int j) in tensor ? [.. ring, .. inner] : ring)
                {
                    (int x, int y) = At((3 * pi) + i, (3 * pj) + j);
                    data.Append((char)x).Append((char)y);
                }

                for (int corner = 0; corner < 4; corner++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        data.Append((char)((((pi + pj + corner) * 89) + (c * 53)) % 256));
                    }
                }
            }
        }

        string decode = string.Concat(Enumerable.Repeat(" 0 1", components));
        return ContentSamples.Stream(data.ToString(), $"{entries} /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 100 0 100{decode}]");
    }

    /// <summary>A Type 4 zigzag strip across a 100 x 100 cell: flags 0 0 0 then 1, 8-bit fields, colours cycling.</summary>
    private static string Strip(string entries, int vertices, int components)
    {
        var data = new StringBuilder();
        for (int k = 0; k < vertices; k++)
        {
            data.Append((char)(k < 3 ? 0 : 1));
            data.Append((char)(k * 255 / Math.Max(vertices - 1, 1)));
            data.Append((char)(k % 2 == 0 ? 10 : 245));
            for (int c = 0; c < components; c++)
            {
                data.Append((char)(((k * 97) + (c * 61)) % 256));
            }
        }

        string decode = string.Concat(Enumerable.Repeat(" 0 1", components));
        return ContentSamples.Stream(
            data.ToString(),
            $"/ShadingType 4 {entries} /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 100 0 100{decode}]");
    }

    /// <summary>A Type 5 lattice of <paramref name="across"/> × <paramref name="down"/> vertices over 100 x 100, with t through a function.</summary>
    private static string Lattice(int across, int down)
    {
        var data = new StringBuilder();
        for (int j = 0; j < down; j++)
        {
            for (int i = 0; i < across; i++)
            {
                data.Append((char)(i * 255 / (across - 1)));
                data.Append((char)((j * 255 / (down - 1)) ^ (i % 2 * 9)));
                data.Append((char)((i + j) * 255 / (across + down - 2)));
            }
        }

        return ContentSamples.Stream(
            data.ToString(),
            "/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow " + across.ToString(CultureInfo.InvariantCulture)
            + " /Decode [0 100 0 100 0 1] /Background [0.2 0.2 0.2] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0.5 0] /C1 [0 0.2 1] /N 1.5 >>");
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
