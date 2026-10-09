namespace Broadside.Security.Cryptography;

/// <summary>The RC4 stream cipher, implemented in managed code (the base class library has none).</summary>
/// <remarks>
/// ISO 32000-2 §7.6.3.1: RC4 is a symmetric stream cipher, the same operation encrypts and decrypts, and the length of the data does
/// not change. Deprecated in PDF 2.0; used here only to read files that use it. The state lives on the stack: no allocation per call.
/// </remarks>
internal static class Rc4
{
    /// <summary>Encrypts or decrypts <paramref name="input"/> into <paramref name="output"/>, which may be the same memory.</summary>
    /// <param name="key">The key, 1 to 256 bytes.</param>
    /// <param name="input">The data.</param>
    /// <param name="output">At least as long as <paramref name="input"/>.</param>
    public static void Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("The RC4 key cannot be empty.", nameof(key));
        }

        Span<byte> state = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
        {
            state[i] = (byte)i;
        }

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        int x = 0;
        int y = 0;
        for (int k = 0; k < input.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[k] = (byte)(input[k] ^ state[(state[x] + state[y]) & 0xFF]);
        }
    }
}
