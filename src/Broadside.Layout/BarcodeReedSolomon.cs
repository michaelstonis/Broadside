namespace Broadside.Layout.Flow;

/// <summary>
/// Reed-Solomon error correction over GF(2^8), the field QR Code and Data Matrix share with different primitive polynomials and first
/// roots: QR Code uses x^8 + x^4 + x^3 + x^2 + 1 (285) with roots α^0 to α^(n-1) (ISO/IEC 18004 §7.5.2), Data Matrix uses
/// x^8 + x^5 + x^3 + x^2 + 1 (301) with roots α^1 to α^n (ISO/IEC 16022 §5.7.1).
/// </summary>
internal sealed class BarcodeReedSolomon
{
    private readonly int[] _exp = new int[512];
    private readonly int[] _log = new int[256];
    private readonly int _firstRoot;

    private BarcodeReedSolomon(int primitive, int firstRoot)
    {
        _firstRoot = firstRoot;
        int value = 1;
        for (int power = 0; power < 255; power++)
        {
            _exp[power] = value;
            _log[value] = power;
            value <<= 1;
            if (value > 255)
            {
                value ^= primitive;
            }
        }

        for (int power = 255; power < _exp.Length; power++)
        {
            _exp[power] = _exp[power - 255];
        }
    }

    /// <summary>The field and roots of ISO/IEC 18004.</summary>
    public static BarcodeReedSolomon Qr { get; } = new(0x11D, 0);

    /// <summary>The field and roots of ISO/IEC 16022.</summary>
    public static BarcodeReedSolomon DataMatrix { get; } = new(0x12D, 1);

    /// <summary>Multiplies two field elements.</summary>
    public int Multiply(int a, int b) => a == 0 || b == 0 ? 0 : _exp[_log[a] + _log[b]];

    /// <summary>α raised to <paramref name="power"/>.</summary>
    public int Power(int power) => _exp[power % 255];

    /// <summary>Returns the <paramref name="count"/> error correction codewords of <paramref name="data"/>, highest degree first.</summary>
    public byte[] Encode(ReadOnlySpan<byte> data, int count)
    {
        // The generator: the product of (x - α^i) for the count roots, coefficients highest degree first, leading 1 implied.
        int[] generator = new int[count + 1];
        generator[0] = 1;
        for (int root = 0; root < count; root++)
        {
            int alpha = Power(_firstRoot + root);
            for (int index = root + 1; index > 0; index--)
            {
                generator[index] ^= Multiply(generator[index - 1], alpha);
            }
        }

        byte[] remainder = new byte[count];
        foreach (byte codeword in data)
        {
            int factor = codeword ^ remainder[0];
            Array.Copy(remainder, 1, remainder, 0, count - 1);
            remainder[count - 1] = 0;
            for (int index = 0; index < count; index++)
            {
                remainder[index] ^= (byte)Multiply(generator[index + 1], factor);
            }
        }

        return remainder;
    }
}
