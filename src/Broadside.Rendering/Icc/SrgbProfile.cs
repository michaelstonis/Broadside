using System.Buffers.Binary;
using System.Text;
using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>Writes Broadside's own sRGB profile: IEC 61966-2-1 as an ICC version 4.4 display profile, generated, not copied.</summary>
/// <remarks>
/// <para>
/// ICC.1:2022 §7.2 (header: display class, RGB data, PCSXYZ, the D50 illuminant, perceptual rendering intent, no profile ID),
/// §7.3 (tag table), §8.4.3 (the required tags of a three-component matrix-based display profile: <c>desc</c>, <c>cprt</c>,
/// <c>wtpt</c>, <c>chad</c>, <c>rXYZ</c>, <c>gXYZ</c>, <c>bXYZ</c>, <c>rTRC</c>, <c>gTRC</c>, <c>bTRC</c>), §9.2.34 (a display's media
/// white point is the PCS white), §9.2.15 and Annex E (the Bradford D65 to D50 adaptation as <c>chad</c>), §10.15
/// (multiLocalizedUnicodeType), §10.18 (the sRGB curve as parametric function type 3), §10.22 and §10.31.
/// </para>
/// <para>
/// The colorants are the IEC 61966-2-1 primaries (the columns of its RGB to XYZ matrix, under D65) adapted to D50 by the Bradford
/// transform, rounded to s15Fixed16Number as the tags store them. Deterministic: every byte follows from these definitions.
/// </para>
/// </remarks>
internal static class SrgbProfile
{
    /// <summary>The linear sRGB to XYZ (D65) matrix of IEC 61966-2-1, row-major.</summary>
    private static readonly double[] SrgbToXyz = [0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041];

    /// <summary>Writes the profile.</summary>
    /// <returns>A new array holding the profile.</returns>
    public static byte[] Create()
    {
        double[] chad = Colorimetry.Bradford(CieXyz.D65, CieXyz.D50);
        double[] colorants = Colorimetry.Multiply(chad, SrgbToXyz);
        byte[] curve = Parametric(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045);
        (string Signature, byte[] Data)[] tags =
        [
            ("desc", Text("sRGB IEC61966-2.1 (Broadside)")),
            ("cprt", Text("No copyright, use freely")),
            ("wtpt", Xyz(CieXyz.D50.X, CieXyz.D50.Y, CieXyz.D50.Z)),
            ("chad", [.. "sf32"u8, 0, 0, 0, 0, .. chad.SelectMany(Fixed)]),
            ("rXYZ", Xyz(colorants[0], colorants[3], colorants[6])),
            ("gXYZ", Xyz(colorants[1], colorants[4], colorants[7])),
            ("bXYZ", Xyz(colorants[2], colorants[5], colorants[8])),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        ];

        // The three TRC tags share one data element (§7.3.1 allows tags to share data).
        var offsets = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        var data = new List<byte>();
        int start = IccProfileHeader.Length + 4 + (12 * tags.Length);
        var table = new List<byte>();
        foreach ((string signature, byte[] body) in tags)
        {
            if (!offsets.TryGetValue(body, out int offset))
            {
                offset = start + data.Count;
                offsets.Add(body, offset);
                data.AddRange(body);
                data.AddRange(new byte[(4 - (body.Length % 4)) % 4]);
            }

            table.AddRange(Encoding.ASCII.GetBytes(signature));
            table.AddRange(UInt32(offset));
            table.AddRange(UInt32(body.Length));
        }

        byte[] profile = new byte[start + data.Count];
        Span<byte> header = profile;
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)profile.Length);
        header[8] = 4;
        header[9] = 0x40;
        "mntr"u8.CopyTo(header[12..]);
        "RGB "u8.CopyTo(header[16..]);
        "XYZ "u8.CopyTo(header[20..]);

        // §7.2.8 date and time: 2026-01-01 00:00:00, fixed so the bytes never change.
        BinaryPrimitives.WriteUInt16BigEndian(header[24..], 2026);
        BinaryPrimitives.WriteUInt16BigEndian(header[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(header[28..], 1);
        "acsp"u8.CopyTo(header[36..]);
        Fixed(CieXyz.D50.X).CopyTo(header[68..]);
        Fixed(CieXyz.D50.Y).CopyTo(header[72..]);
        Fixed(CieXyz.D50.Z).CopyTo(header[76..]);
        BinaryPrimitives.WriteUInt32BigEndian(header[IccProfileHeader.Length..], (uint)tags.Length);
        table.CopyTo(profile, IccProfileHeader.Length + 4);
        data.CopyTo(profile, start);
        return profile;
    }

    private static byte[] Xyz(double x, double y, double z) => [.. "XYZ "u8, 0, 0, 0, 0, .. Fixed(x), .. Fixed(y), .. Fixed(z)];

    private static byte[] Parametric(ushort function, params double[] parameters) =>
        [.. "para"u8, 0, 0, 0, 0, (byte)(function >> 8), (byte)function, 0, 0, .. parameters.SelectMany(Fixed)];

    /// <summary>A multiLocalizedUnicodeType with one en-US record (§10.15).</summary>
    private static byte[] Text(string text)
    {
        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        return [.. "mluc"u8, 0, 0, 0, 0, .. UInt32(1), .. UInt32(12), .. "enUS"u8, .. UInt32(utf16.Length), .. UInt32(28), .. utf16];
    }

    /// <summary>An s15Fixed16Number, rounded to the nearest (§4.6).</summary>
    private static byte[] Fixed(double value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, (int)Math.Round(value * 65536));
        return bytes;
    }

    private static byte[] UInt32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        return bytes;
    }
}
