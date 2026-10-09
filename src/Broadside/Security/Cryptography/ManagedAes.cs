namespace Broadside.Security.Cryptography;

/// <summary>A managed AES block cipher (FIPS 197) for platforms whose base class library has none (the browser).</summary>
/// <remarks>
/// A direct transcription of FIPS 197 §5 with byte-wide S-box lookups. It is not hardened against cache-timing attacks; it reads
/// documents on platforms that offer nothing else, and the base class library's AES is used wherever it exists.
/// </remarks>
internal sealed class ManagedAes
{
    private static readonly byte[] SBox = BuildSBox();
    private static readonly byte[] InverseSBox = BuildInverseSBox(SBox);
    private static readonly byte[] Times9 = BuildProducts(9);
    private static readonly byte[] Times11 = BuildProducts(11);
    private static readonly byte[] Times13 = BuildProducts(13);
    private static readonly byte[] Times14 = BuildProducts(14);

    private readonly byte[] _roundKeys;
    private readonly int _rounds;

    /// <summary>Initializes a new instance of the <see cref="ManagedAes"/> class: key expansion (FIPS 197 §5.2).</summary>
    /// <param name="key">16, 24 or 32 bytes.</param>
    public ManagedAes(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("An AES key is 16, 24 or 32 bytes long.", nameof(key));
        }

        int nk = key.Length / 4;
        _rounds = nk + 6;
        int words = 4 * (_rounds + 1);
        _roundKeys = new byte[words * 4];
        key.CopyTo(_roundKeys);
        Span<byte> temp = stackalloc byte[4];
        byte rcon = 1;
        for (int i = nk; i < words; i++)
        {
            _roundKeys.AsSpan((i - 1) * 4, 4).CopyTo(temp);
            if (i % nk == 0)
            {
                byte first = temp[0];
                temp[0] = (byte)(SBox[temp[1]] ^ rcon);
                temp[1] = SBox[temp[2]];
                temp[2] = SBox[temp[3]];
                temp[3] = SBox[first];
                rcon = XTime(rcon);
            }
            else if (nk > 6 && i % nk == 4)
            {
                for (int b = 0; b < 4; b++)
                {
                    temp[b] = SBox[temp[b]];
                }
            }

            for (int b = 0; b < 4; b++)
            {
                _roundKeys[(i * 4) + b] = (byte)(_roundKeys[((i - nk) * 4) + b] ^ temp[b]);
            }
        }
    }

    /// <summary>Encrypts one 16-byte block (FIPS 197 §5.1).</summary>
    /// <param name="input">16 bytes.</param>
    /// <param name="output">16 bytes; may be the same memory as <paramref name="input"/>.</param>
    public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> state = stackalloc byte[16];
        input[..16].CopyTo(state);
        AddRoundKey(state, 0);
        for (int round = 1; round <= _rounds; round++)
        {
            for (int i = 0; i < 16; i++)
            {
                state[i] = SBox[state[i]];
            }

            ShiftRows(state);
            if (round != _rounds)
            {
                MixColumns(state);
            }

            AddRoundKey(state, round);
        }

        state.CopyTo(output);
    }

    /// <summary>Decrypts one 16-byte block (FIPS 197 §5.3).</summary>
    /// <param name="input">16 bytes.</param>
    /// <param name="output">16 bytes; may be the same memory as <paramref name="input"/>.</param>
    public void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> state = stackalloc byte[16];
        input[..16].CopyTo(state);
        AddRoundKey(state, _rounds);
        for (int round = _rounds - 1; round >= 0; round--)
        {
            InverseShiftRows(state);
            for (int i = 0; i < 16; i++)
            {
                state[i] = InverseSBox[state[i]];
            }

            AddRoundKey(state, round);
            if (round != 0)
            {
                InverseMixColumns(state);
            }
        }

        state.CopyTo(output);
    }

    private static byte XTime(byte value) => (byte)((value << 1) ^ ((value & 0x80) != 0 ? 0x1B : 0));

    private static byte Multiply(byte a, byte b)
    {
        byte product = 0;
        while (b != 0)
        {
            if ((b & 1) != 0)
            {
                product ^= a;
            }

            a = XTime(a);
            b >>= 1;
        }

        return product;
    }

    // The state is column-major: byte r + 4c is row r of column c (FIPS 197 §3.4).
    private static void ShiftRows(Span<byte> state)
    {
        Span<byte> copy = stackalloc byte[16];
        state.CopyTo(copy);
        for (int column = 0; column < 4; column++)
        {
            for (int row = 1; row < 4; row++)
            {
                state[row + (4 * column)] = copy[row + (4 * ((column + row) & 3))];
            }
        }
    }

    private static void InverseShiftRows(Span<byte> state)
    {
        Span<byte> copy = stackalloc byte[16];
        state.CopyTo(copy);
        for (int column = 0; column < 4; column++)
        {
            for (int row = 1; row < 4; row++)
            {
                state[row + (4 * ((column + row) & 3))] = copy[row + (4 * column)];
            }
        }
    }

    private static void MixColumns(Span<byte> state)
    {
        for (int c = 0; c < 16; c += 4)
        {
            byte a0 = state[c], a1 = state[c + 1], a2 = state[c + 2], a3 = state[c + 3];
            state[c] = (byte)(XTime(a0) ^ XTime(a1) ^ a1 ^ a2 ^ a3);
            state[c + 1] = (byte)(a0 ^ XTime(a1) ^ XTime(a2) ^ a2 ^ a3);
            state[c + 2] = (byte)(a0 ^ a1 ^ XTime(a2) ^ XTime(a3) ^ a3);
            state[c + 3] = (byte)(XTime(a0) ^ a0 ^ a1 ^ a2 ^ XTime(a3));
        }
    }

    private static void InverseMixColumns(Span<byte> state)
    {
        for (int c = 0; c < 16; c += 4)
        {
            byte a0 = state[c], a1 = state[c + 1], a2 = state[c + 2], a3 = state[c + 3];
            state[c] = (byte)(Times14[a0] ^ Times11[a1] ^ Times13[a2] ^ Times9[a3]);
            state[c + 1] = (byte)(Times9[a0] ^ Times14[a1] ^ Times11[a2] ^ Times13[a3]);
            state[c + 2] = (byte)(Times13[a0] ^ Times9[a1] ^ Times14[a2] ^ Times11[a3]);
            state[c + 3] = (byte)(Times11[a0] ^ Times13[a1] ^ Times9[a2] ^ Times14[a3]);
        }
    }

    private static byte[] BuildSBox()
    {
        // FIPS 197 §5.1.1: multiplicative inverse in GF(2^8) followed by the affine transformation.
        var box = new byte[256];
        for (int value = 0; value < 256; value++)
        {
            byte inverse = 0;
            for (int candidate = 1; candidate < 256 && value != 0; candidate++)
            {
                if (Multiply((byte)value, (byte)candidate) == 1)
                {
                    inverse = (byte)candidate;
                    break;
                }
            }

            int s = inverse;
            int result = s ^ RotateLeft(s, 1) ^ RotateLeft(s, 2) ^ RotateLeft(s, 3) ^ RotateLeft(s, 4) ^ 0x63;
            box[value] = (byte)result;
        }

        return box;

        static int RotateLeft(int x, int shift) => ((x << shift) | (x >> (8 - shift))) & 0xFF;
    }

    private static byte[] BuildProducts(byte factor)
    {
        var products = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            products[i] = Multiply((byte)i, factor);
        }

        return products;
    }

    private static byte[] BuildInverseSBox(byte[] box)
    {
        var inverse = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            inverse[box[i]] = (byte)i;
        }

        return inverse;
    }

    private void AddRoundKey(Span<byte> state, int round)
    {
        ReadOnlySpan<byte> key = _roundKeys.AsSpan(round * 16, 16);
        for (int i = 0; i < 16; i++)
        {
            state[i] ^= key[i];
        }
    }
}
