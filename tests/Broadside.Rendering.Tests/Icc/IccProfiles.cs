using System.Buffers.Binary;
using System.Text;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>Synthesizes ICC profiles (ICC.1:2022 §7.2 header, §7.3 tag table, §10 tag types) and ICCBased spaces for the engine's tests.</summary>
internal static class IccProfiles
{
    /// <summary>A profile with the given class, data colour space, connection space, version and tags (signature, data).</summary>
    public static byte[] Profile(string deviceClass, string space, (string Signature, byte[] Data)[] tags, byte major = 4, string pcs = "XYZ ")
    {
        var table = new List<byte>();
        var data = new List<byte>();
        int offset = 128 + 4 + (12 * tags.Length);
        foreach ((string signature, byte[] body) in tags)
        {
            table.AddRange(Encoding.ASCII.GetBytes(signature));
            table.AddRange(UInt32(offset + data.Count));
            table.AddRange(UInt32(body.Length));
            data.AddRange(body);
            data.AddRange(new byte[(4 - (body.Length % 4)) % 4]);
        }

        byte[] profile = new byte[128 + 4 + table.Count + data.Count];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        profile[8] = major;
        profile[9] = 0x40;
        Encoding.ASCII.GetBytes(deviceClass).CopyTo(profile, 12);
        Encoding.ASCII.GetBytes(space).CopyTo(profile, 16);
        Encoding.ASCII.GetBytes(pcs).CopyTo(profile, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(68), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(72), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(76), 0x0000D32D);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), (uint)tags.Length);
        table.CopyTo(profile, 132);
        data.CopyTo(profile, 132 + table.Count);
        return profile;
    }

    /// <summary>A display RGB matrix/TRC profile: the D50-adapted sRGB primaries and the given curves (default gamma 1.8 each).</summary>
    public static byte[] Rgb(byte[]? red = null, byte[]? green = null, byte[]? blue = null) => Profile("mntr", "RGB ",
    [
        ("wtpt", Xyz(0.9642, 1, 0.8249)),
        ("rXYZ", Xyz(0.4361, 0.2225, 0.0139)),
        ("gXYZ", Xyz(0.3851, 0.7169, 0.0971)),
        ("bXYZ", Xyz(0.1431, 0.0606, 0.7141)),
        ("rTRC", red ?? Gamma(1.8)),
        ("gTRC", green ?? Gamma(1.8)),
        ("bTRC", blue ?? Gamma(1.8)),
    ]);

    /// <summary>An XYZType tag (§10.31).</summary>
    public static byte[] Xyz(double x, double y, double z) => [.. "XYZ "u8, 0, 0, 0, 0, .. Fixed(x), .. Fixed(y), .. Fixed(z)];

    /// <summary>A curveType tag with one u8Fixed8Number gamma (§10.6).</summary>
    public static byte[] Gamma(double gamma) => [.. "curv"u8, 0, 0, 0, 0, 0, 0, 0, 1, .. UInt16((int)Math.Round(gamma * 256))];

    /// <summary>A curveType tag with a raw entry count and the given entries (§10.6).</summary>
    public static byte[] Curve(uint count, params ushort[] entries) =>
        [.. "curv"u8, 0, 0, 0, 0, .. UInt32((int)count), .. entries.SelectMany(static e => UInt16(e))];

    /// <summary>A parametricCurveType tag (§10.18).</summary>
    public static byte[] Parametric(ushort function, params double[] parameters) =>
        [.. "para"u8, 0, 0, 0, 0, .. UInt16(function), 0, 0, .. parameters.SelectMany(Fixed)];

    /// <summary>A direct <c>[/ICCBased stream]</c> array holding <paramref name="profile"/>, with extra stream dictionary entries.</summary>
    public static CosArray Space(byte[] profile, int components, string? alternate = null)
    {
        var dictionary = new CosDictionary { [new CosName("N")] = new CosInteger(components) };
        if (alternate is not null)
        {
            dictionary[new CosName("Alternate")] = new CosName(alternate);
        }

        return [new CosName("ICCBased"), new CosStream(dictionary, profile)];
    }

    /// <summary>Converts one colour to RGB through <paramref name="document"/>'s colour management.</summary>
    public static float[] Rgb(this PdfDocument document, PdfColorSpace space, params float[] components)
    {
        float[] rgb = new float[3];
        document.GetColorConverter(space).Convert(components, rgb);
        return rgb;
    }

    private static byte[] Fixed(double value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, (int)Math.Round(value * 65536));
        return bytes;
    }

    private static byte[] UInt16(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] UInt32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        return bytes;
    }
}
