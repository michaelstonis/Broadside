using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Broadside.Security.Cryptography;

/// <summary>MD5 (RFC 1321), which the deprecated standard security handler revisions 2 to 4 require for key derivation.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.3.1: "Encrypt versions 1-4 ... make use of the MD5 message-digest algorithm for key generation purposes". Used only
/// to read such files, never to protect anything new. The base class library's MD5 where the platform has it, the managed
/// implementation below elsewhere (<see cref="CryptographyBackend"/>).
/// </remarks>
[SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "ISO 32000-2 §7.6.3.2 and §7.6.4.3.2 prescribe MD5 for reading revision 2-4 files.")]
internal static class Md5
{
    /// <summary>The digest length in bytes.</summary>
    public const int HashSize = 16;

    /// <summary>Hashes <paramref name="source"/> into the first 16 bytes of <paramref name="destination"/>.</summary>
    /// <param name="source">The data.</param>
    /// <param name="destination">At least 16 bytes.</param>
    public static void HashData(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (CryptographyBackend.UseManaged)
        {
            ManagedMd5.HashData(source, destination);
        }
        else
        {
            MD5.HashData(source, destination);
        }
    }
}

/// <summary>A managed MD5 (RFC 1321 §3), for platforms whose base class library has none.</summary>
internal static class ManagedMd5
{
    private static readonly uint[] Sines = BuildSines();

    private static readonly int[] Shifts =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    /// <summary>Hashes <paramref name="source"/> into the first 16 bytes of <paramref name="destination"/>.</summary>
    /// <param name="source">The data.</param>
    /// <param name="destination">At least 16 bytes.</param>
    public static void HashData(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Span<uint> state = [0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476];
        int full = source.Length & ~63;
        for (int offset = 0; offset < full; offset += 64)
        {
            Transform(state, source.Slice(offset, 64));
        }

        // Step 1 and 2: append a 1 bit, zeros to 56 mod 64, then the bit length, low-order byte first.
        Span<byte> tail = stackalloc byte[128];
        tail.Clear();
        ReadOnlySpan<byte> rest = source[full..];
        rest.CopyTo(tail);
        tail[rest.Length] = 0x80;
        int tailLength = rest.Length < 56 ? 64 : 128;
        BinaryPrimitives.WriteUInt64LittleEndian(tail[(tailLength - 8)..], (ulong)source.Length * 8);
        for (int offset = 0; offset < tailLength; offset += 64)
        {
            Transform(state, tail.Slice(offset, 64));
        }

        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[(i * 4)..], state[i]);
        }
    }

    private static void Transform(Span<uint> state, ReadOnlySpan<byte> block)
    {
        Span<uint> x = stackalloc uint[16];
        for (int i = 0; i < 16; i++)
        {
            x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * 4)..]);
        }

        uint a = state[0], b = state[1], c = state[2], d = state[3];
        for (int i = 0; i < 64; i++)
        {
            uint f;
            int g;
            switch (i >> 4)
            {
                case 0:
                    f = (b & c) | (~b & d);
                    g = i;
                    break;
                case 1:
                    f = (d & b) | (~d & c);
                    g = ((5 * i) + 1) & 15;
                    break;
                case 2:
                    f = b ^ c ^ d;
                    g = ((3 * i) + 5) & 15;
                    break;
                default:
                    f = c ^ (b | ~d);
                    g = (7 * i) & 15;
                    break;
            }

            uint rotated = uint.RotateLeft(a + f + Sines[i] + x[g], Shifts[i]);
            a = d;
            d = c;
            c = b;
            b += rotated;
        }

        state[0] += a;
        state[1] += b;
        state[2] += c;
        state[3] += d;
    }

    private static uint[] BuildSines()
    {
        var sines = new uint[64];
        for (int i = 0; i < 64; i++)
        {
            sines[i] = (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0);
        }

        return sines;
    }
}
