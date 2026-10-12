using System.Buffers.Binary;
using System.Text;

namespace Broadside.Tests.Colors;

/// <summary>Synthesizes ICC profile headers (ICC.1:2022 §7.2, Table 17) for ICCBased colour space tests.</summary>
internal static class IccProfiles
{
    /// <summary>A 128-byte header (and nothing else) with the given class, data colour space and version.</summary>
    public static byte[] Header(string deviceClass, string colorSpace, byte major = 2, byte minorAndFix = 0x10, int? size = null)
    {
        byte[] header = new byte[128];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(size ?? header.Length));
        Encoding.ASCII.GetBytes("bsde").CopyTo(header, 4);
        header[8] = major;
        header[9] = minorAndFix;
        Encoding.ASCII.GetBytes(deviceClass).CopyTo(header, 12);
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(header, 16);
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(header, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(header, 36);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(64), 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(68), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(72), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(76), 0x0000D32D);
        return header;
    }

    /// <summary>An ICCBased stream object holding <paramref name="profile"/> in ASCIIHex, with extra dictionary entries.</summary>
    public static string Stream(byte[] profile, string entries)
    {
        string hex = Convert.ToHexString(profile) + ">";
        return $"<< {entries} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream";
    }
}
