using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Broadside.Security.Cryptography;

/// <summary>
/// A managed SHA3-256, SHA3-384 and SHA3-512 (FIPS 202), for platforms whose base class library has no SHA-3 (macOS, the browser).
/// </summary>
/// <remarks>
/// <para>
/// FIPS 202 §6.1: SHA3-d is KECCAK[c](M || 01, d) with capacity c = 2d, that is the sponge construction (§4) over Keccak-p[1600, 24]
/// (§3.3, §3.4) with the pad10*1 rule (§5.1) and rate r = 1600 - 2d bits: 136 bytes for SHA3-256, 104 for SHA3-384, 72 for
/// SHA3-512. Every digest length is less than the rate, so the squeeze is the first d bits of the state after absorbing.
/// </para>
/// <para>
/// Incremental: <see cref="AppendData"/> absorbs whole rate-sized blocks straight from the caller's span and keeps a partial block
/// in an inline buffer, so hashing allocates nothing after construction, however much data goes through it. Not thread-safe: one
/// instance per hashing operation. Not constant time with respect to the data length (no secret depends on it here).
/// </para>
/// </remarks>
internal sealed class ManagedSha3
{
    /// <summary>The largest rate in bytes, SHA3-256's.</summary>
    private const int MaxRate = 136;

    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001, 0x0000000000008082, 0x800000000000808A, 0x8000000080008000,
        0x000000000000808B, 0x0000000080000001, 0x8000000080008081, 0x8000000000008009,
        0x000000000000008A, 0x0000000000000088, 0x0000000080008009, 0x000000008000000A,
        0x000000008000808B, 0x800000000000008B, 0x8000000000008089, 0x8000000000008003,
        0x8000000000008002, 0x8000000000000080, 0x000000000000800A, 0x800000008000000A,
        0x8000000080008081, 0x8000000000008080, 0x0000000080000001, 0x8000000080008008,
    ];

    private readonly int _rate;
    private State _state;
    private Block _pending;
    private int _pendingLength;

    /// <summary>Initializes a new instance of the <see cref="ManagedSha3"/> class.</summary>
    /// <param name="hashSizeInBytes">The digest length: 32 (SHA3-256), 48 (SHA3-384) or 64 (SHA3-512).</param>
    public ManagedSha3(int hashSizeInBytes)
    {
        if (hashSizeInBytes is not (32 or 48 or 64))
        {
            throw new ArgumentOutOfRangeException(nameof(hashSizeInBytes), hashSizeInBytes, "SHA-3 digests are 32, 48 or 64 bytes here.");
        }

        HashSizeInBytes = hashSizeInBytes;
        _rate = 200 - (2 * hashSizeInBytes);
    }

    /// <summary>Gets the digest length in bytes.</summary>
    public int HashSizeInBytes { get; }

    /// <summary>Hashes <paramref name="source"/> in one call.</summary>
    /// <param name="hashSizeInBytes">The digest length: 32, 48 or 64.</param>
    /// <param name="source">The data.</param>
    /// <param name="destination">At least <paramref name="hashSizeInBytes"/> bytes.</param>
    public static void HashData(int hashSizeInBytes, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var sha3 = new ManagedSha3(hashSizeInBytes);
        sha3.AppendData(source);
        sha3.GetHashAndReset(destination);
    }

    /// <summary>Absorbs <paramref name="data"/> (FIPS 202 Algorithm 8, steps 5-6, a block at a time).</summary>
    /// <param name="data">The next bytes of the message.</param>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = _pending;
        if (_pendingLength > 0)
        {
            int take = Math.Min(_rate - _pendingLength, data.Length);
            data[..take].CopyTo(buffer[_pendingLength..]);
            _pendingLength += take;
            data = data[take..];
            if (_pendingLength < _rate)
            {
                return;
            }

            Absorb(buffer[.._rate]);
            _pendingLength = 0;
        }

        while (data.Length >= _rate)
        {
            Absorb(data[.._rate]);
            data = data[_rate..];
        }

        data.CopyTo(buffer);
        _pendingLength = data.Length;
    }

    /// <summary>Pads, absorbs the last block, writes the digest and starts over with an empty message.</summary>
    /// <param name="destination">At least <see cref="HashSizeInBytes"/> bytes.</param>
    public void GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length < HashSizeInBytes)
        {
            throw new ArgumentException("The destination is shorter than the digest.", nameof(destination));
        }

        // §6.1 and §5.1: the SHA-3 domain suffix 01, then pad10*1. In the byte-oriented, least-significant-bit-first convention
        // (Appendix B.2) that is 0x06 after the message and 0x80 in the block's last byte (0x86 when they are the same byte).
        Span<byte> buffer = _pending;
        Span<byte> last = buffer[.._rate];
        last[_pendingLength..].Clear();
        last[_pendingLength] ^= 0x06;
        last[^1] ^= 0x80;
        Absorb(last);

        Span<ulong> lanes = _state;
        int full = HashSizeInBytes / 8;
        for (int i = 0; i < full; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination[(i * 8)..], lanes[i]);
        }

        Reset();
    }

    /// <summary>Forgets the message absorbed so far.</summary>
    public void Reset()
    {
        ((Span<ulong>)_state).Clear();
        ((Span<byte>)_pending).Clear();
        _pendingLength = 0;
    }

    /// <summary>XORs one rate-sized block into the state and applies the permutation.</summary>
    private void Absorb(ReadOnlySpan<byte> block)
    {
        Span<ulong> lanes = _state;
        for (int i = 0; i < _rate / 8; i++)
        {
            lanes[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
        }

        Permute(lanes);
    }

    /// <summary>
    /// Keccak-p[1600, 24] (FIPS 202 Algorithm 7) on 25 lanes, lane (x, y) at index x + 5y: θ, ρ and π, χ, ι per round, with the
    /// state in locals so the rounds touch no memory but the input and output.
    /// </summary>
    private static void Permute(Span<ulong> a)
    {
        ulong a00 = a[0], a01 = a[1], a02 = a[2], a03 = a[3], a04 = a[4];
        ulong a05 = a[5], a06 = a[6], a07 = a[7], a08 = a[8], a09 = a[9];
        ulong a10 = a[10], a11 = a[11], a12 = a[12], a13 = a[13], a14 = a[14];
        ulong a15 = a[15], a16 = a[16], a17 = a[17], a18 = a[18], a19 = a[19];
        ulong a20 = a[20], a21 = a[21], a22 = a[22], a23 = a[23], a24 = a[24];

        ReadOnlySpan<ulong> constants = RoundConstants;
        for (int round = 0; round < 24; round++)
        {
            // θ (Algorithm 1): each lane absorbs the parity of two neighbouring columns.
            ulong c0 = a00 ^ a05 ^ a10 ^ a15 ^ a20;
            ulong c1 = a01 ^ a06 ^ a11 ^ a16 ^ a21;
            ulong c2 = a02 ^ a07 ^ a12 ^ a17 ^ a22;
            ulong c3 = a03 ^ a08 ^ a13 ^ a18 ^ a23;
            ulong c4 = a04 ^ a09 ^ a14 ^ a19 ^ a24;
            ulong d0 = c4 ^ ulong.RotateLeft(c1, 1);
            ulong d1 = c0 ^ ulong.RotateLeft(c2, 1);
            ulong d2 = c1 ^ ulong.RotateLeft(c3, 1);
            ulong d3 = c2 ^ ulong.RotateLeft(c4, 1);
            ulong d4 = c3 ^ ulong.RotateLeft(c0, 1);

            // ρ (Algorithm 2, offsets of Table 2) and π (Algorithm 3): B[y, 2x + 3y] = rot(A[x, y] ^ D[x], r[x, y]).
            ulong b00 = a00 ^ d0;
            ulong b01 = ulong.RotateLeft(a06 ^ d1, 44);
            ulong b02 = ulong.RotateLeft(a12 ^ d2, 43);
            ulong b03 = ulong.RotateLeft(a18 ^ d3, 21);
            ulong b04 = ulong.RotateLeft(a24 ^ d4, 14);
            ulong b05 = ulong.RotateLeft(a03 ^ d3, 28);
            ulong b06 = ulong.RotateLeft(a09 ^ d4, 20);
            ulong b07 = ulong.RotateLeft(a10 ^ d0, 3);
            ulong b08 = ulong.RotateLeft(a16 ^ d1, 45);
            ulong b09 = ulong.RotateLeft(a22 ^ d2, 61);
            ulong b10 = ulong.RotateLeft(a01 ^ d1, 1);
            ulong b11 = ulong.RotateLeft(a07 ^ d2, 6);
            ulong b12 = ulong.RotateLeft(a13 ^ d3, 25);
            ulong b13 = ulong.RotateLeft(a19 ^ d4, 8);
            ulong b14 = ulong.RotateLeft(a20 ^ d0, 18);
            ulong b15 = ulong.RotateLeft(a04 ^ d4, 27);
            ulong b16 = ulong.RotateLeft(a05 ^ d0, 36);
            ulong b17 = ulong.RotateLeft(a11 ^ d1, 10);
            ulong b18 = ulong.RotateLeft(a17 ^ d2, 15);
            ulong b19 = ulong.RotateLeft(a23 ^ d3, 56);
            ulong b20 = ulong.RotateLeft(a02 ^ d2, 62);
            ulong b21 = ulong.RotateLeft(a08 ^ d3, 55);
            ulong b22 = ulong.RotateLeft(a14 ^ d4, 39);
            ulong b23 = ulong.RotateLeft(a15 ^ d0, 41);
            ulong b24 = ulong.RotateLeft(a21 ^ d1, 2);

            // χ (Algorithm 4) row by row, then ι (Algorithm 6) on lane (0, 0).
            a00 = b00 ^ (~b01 & b02) ^ constants[round];
            a01 = b01 ^ (~b02 & b03);
            a02 = b02 ^ (~b03 & b04);
            a03 = b03 ^ (~b04 & b00);
            a04 = b04 ^ (~b00 & b01);
            a05 = b05 ^ (~b06 & b07);
            a06 = b06 ^ (~b07 & b08);
            a07 = b07 ^ (~b08 & b09);
            a08 = b08 ^ (~b09 & b05);
            a09 = b09 ^ (~b05 & b06);
            a10 = b10 ^ (~b11 & b12);
            a11 = b11 ^ (~b12 & b13);
            a12 = b12 ^ (~b13 & b14);
            a13 = b13 ^ (~b14 & b10);
            a14 = b14 ^ (~b10 & b11);
            a15 = b15 ^ (~b16 & b17);
            a16 = b16 ^ (~b17 & b18);
            a17 = b17 ^ (~b18 & b19);
            a18 = b18 ^ (~b19 & b15);
            a19 = b19 ^ (~b15 & b16);
            a20 = b20 ^ (~b21 & b22);
            a21 = b21 ^ (~b22 & b23);
            a22 = b22 ^ (~b23 & b24);
            a23 = b23 ^ (~b24 & b20);
            a24 = b24 ^ (~b20 & b21);
        }

        a[0] = a00;
        a[1] = a01;
        a[2] = a02;
        a[3] = a03;
        a[4] = a04;
        a[5] = a05;
        a[6] = a06;
        a[7] = a07;
        a[8] = a08;
        a[9] = a09;
        a[10] = a10;
        a[11] = a11;
        a[12] = a12;
        a[13] = a13;
        a[14] = a14;
        a[15] = a15;
        a[16] = a16;
        a[17] = a17;
        a[18] = a18;
        a[19] = a19;
        a[20] = a20;
        a[21] = a21;
        a[22] = a22;
        a[23] = a23;
        a[24] = a24;
    }

    /// <summary>The 1600-bit state as 25 lanes.</summary>
    [InlineArray(25)]
    private struct State
    {
        private ulong _lane;
    }

    /// <summary>A partial block waiting for the rest of its bytes.</summary>
    [InlineArray(MaxRate)]
    private struct Block
    {
        private byte _element;
    }
}
