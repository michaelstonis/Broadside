using System.Security.Cryptography;
using System.Text;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// The primitives behind the security handlers, each through its facade: the published test vectors (RFC 1321, FIPS 197, NIST
/// SP 800-38D, RFC 3394, RFC 4013) for the managed implementations the browser uses, and agreement with the base class library on
/// random data where the platform has it.
/// </summary>
public sealed class CryptographyTests
{
    public static TheoryData<string, string> Md5Vectors => new()
    {
        { string.Empty, "d41d8cd98f00b204e9800998ecf8427e" },
        { "a", "0cc175b9c0f1b6a831c399e269772661" },
        { "abc", "900150983cd24fb0d6963f7d28e17f72" },
        { "message digest", "f96b697d7cb7938d525a2f31aaf161d0" },
        { "abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b" },
        { "12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57edf4a22be3c955ac49da2e2107b67a" },
    };

    public static TheoryData<string, string> AesVectors => new()
    {
        { "000102030405060708090a0b0c0d0e0f", "69c4e0d86a7b0430d8cdb78070b4c55a" },
        { "000102030405060708090a0b0c0d0e0f1011121314151617", "dda97ca4864cdfe06eaf70a0ec0d7191" },
        { "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "8ea2b7ca516745bfeafc49904b496089" },
    };

    public static TheoryData<string, string, string> Rc4Vectors => new()
    {
        { "Key", "Plaintext", "bbf316e8d940af0ad3" },
        { "Wiki", "pedia", "1021bf0420" },
        { "Secret", "Attack at dawn", "45a01f645fc35b383552544b9bf5" },
    };

    [Theory]
    [MemberData(nameof(Md5Vectors))]
    public void Managed_md5_matches_rfc_1321(string message, string digest)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        byte[] output = new byte[16];

        Md5.HashData(Encoding.ASCII.GetBytes(message), output);

        Assert.Equal(digest, Convert.ToHexStringLower(output));
    }

    [Fact]
    public void Managed_md5_agrees_with_the_platform_at_every_padding_boundary()
    {
        byte[] data = RandomBytes(300, seed: 1);
        for (int length = 0; length <= data.Length; length++)
        {
            byte[] managed = new byte[16];
            ManagedMd5.HashData(data.AsSpan(0, length), managed);
#pragma warning disable CA5351 // The reference implementation the managed one is checked against.
            Assert.Equal(MD5.HashData(data.AsSpan(0, length)), managed);
#pragma warning restore CA5351
        }
    }

    [Theory]
    [MemberData(nameof(AesVectors))]
    public void Managed_aes_matches_fips_197_appendix_c(string key, string ciphertext)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        using AesCipher aes = AesCipher.Create(Convert.FromHexString(key));
        byte[] plaintext = Convert.FromHexString("00112233445566778899aabbccddeeff");
        byte[] output = new byte[16];

        aes.EncryptEcb(plaintext, output);
        Assert.Equal(ciphertext, Convert.ToHexStringLower(output));

        aes.DecryptEcb(output, output);
        Assert.Equal(plaintext, output);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Managed_aes_cbc_agrees_with_the_platform(int keyLength)
    {
        byte[] key = RandomBytes(keyLength, seed: keyLength);
        byte[] iv = RandomBytes(16, seed: 99);
        byte[] plaintext = RandomBytes(16 * 37, seed: 7);
        byte[] expected = new byte[plaintext.Length];
        byte[] actual = new byte[plaintext.Length];
        using (AesCipher platform = AesCipher.Create(key))
        {
            platform.EncryptCbc(plaintext, iv, expected);
        }

        using (CryptographyBackend.ForceManaged())
        using (AesCipher managed = AesCipher.Create(key))
        {
            managed.EncryptCbc(plaintext, iv, actual);
            Assert.Equal(expected, actual);
            managed.DecryptCbc(actual, iv, actual);
        }

        Assert.Equal(plaintext, actual);
    }

    [Fact]
    public void Managed_aes_gcm_matches_the_gcm_specification_test_cases_13_and_14()
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[16];

        AesGcmCipher.Encrypt(new byte[32], new byte[12], [], [], tag);
        Assert.Equal("530f8afbc74536b9a963b4f1c4cb738b", Convert.ToHexStringLower(tag));

        AesGcmCipher.Encrypt(new byte[32], new byte[12], new byte[16], ciphertext, tag);
        Assert.Equal("cea7403d4d606b6e074ec5d3baf39d18", Convert.ToHexStringLower(ciphertext));
        Assert.Equal("d0d1c8a799996bf0265b98b5d48ab919", Convert.ToHexStringLower(tag));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(1000)]
    public void Managed_aes_gcm_agrees_with_the_platform_and_rejects_a_changed_tag(int length)
    {
        Assert.SkipUnless(AesGcm.IsSupported, "The platform has no AES-GCM to compare with.");
        byte[] key = RandomBytes(32, seed: 5);
        byte[] nonce = RandomBytes(12, seed: 6);
        byte[] plaintext = RandomBytes(length, seed: length);
        byte[] expected = new byte[length];
        byte[] expectedTag = new byte[16];
        AesGcmCipher.Encrypt(key, nonce, plaintext, expected, expectedTag);

        using IDisposable managed = CryptographyBackend.ForceManaged();
        byte[] actual = new byte[length];
        byte[] tag = new byte[16];
        AesGcmCipher.Encrypt(key, nonce, plaintext, actual, tag);
        Assert.Equal(expected, actual);
        Assert.Equal(expectedTag, tag);

        byte[] decrypted = new byte[length];
        Assert.True(AesGcmCipher.TryDecrypt(key, nonce, actual, tag, decrypted));
        Assert.Equal(plaintext, decrypted);
        tag[^1] ^= 1;
        Assert.False(AesGcmCipher.TryDecrypt(key, nonce, actual, tag, decrypted));
    }

    [Theory]
    [InlineData("000102030405060708090a0b0c0d0e0f", "00112233445566778899aabbccddeeff", "1fa68b0a8112b447aef34bd8fb5a7b829d3e862371d2cfe5")]
    [InlineData("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "00112233445566778899aabbccddeeff000102030405060708090a0b0c0d0e0f", "28c9f404c4b810f4cbccb35cfb87f8263f5786e2d80ed326cbc7f0e71a99f43bfb988b9b7a02dd21")]
    public void The_key_wrap_matches_rfc_3394(string keyEncryptionKey, string key, string wrapped)
    {
        foreach (bool forceManaged in new[] { false, true })
        {
            using IDisposable? managed = forceManaged ? CryptographyBackend.ForceManaged() : null;
            byte[] kek = Convert.FromHexString(keyEncryptionKey);

            Assert.Equal(wrapped, Convert.ToHexStringLower(AesKeyWrap.Wrap(kek, Convert.FromHexString(key))));
            Assert.Equal(key, Convert.ToHexStringLower(AesKeyWrap.Unwrap(kek, Convert.FromHexString(wrapped))!));
        }
    }

    [Fact]
    public void A_wrapped_key_that_fails_the_integrity_check_does_not_unwrap()
    {
        byte[] wrapped = Convert.FromHexString("1fa68b0a8112b447aef34bd8fb5a7b829d3e862371d2cfe5");
        wrapped[10] ^= 1;

        Assert.Null(AesKeyWrap.Unwrap(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"), wrapped));
        Assert.Null(AesKeyWrap.Unwrap(new byte[16], new byte[12]));
    }

    [Theory]
    [MemberData(nameof(Rc4Vectors))]
    public void Rc4_matches_the_published_vectors(string key, string plaintext, string ciphertext)
    {
        byte[] output = Encoding.ASCII.GetBytes(plaintext);

        Rc4.Transform(Encoding.ASCII.GetBytes(key), output, output);

        Assert.Equal(ciphertext, Convert.ToHexStringLower(output));
    }

    [Theory]
    [InlineData("I\u00adX", "IX")]
    [InlineData("user", "user")]
    [InlineData("USER", "USER")]
    [InlineData("a\u00a0b\u3000c", "a b c")]
    [InlineData("zero\u2060width", "zerowidth")]
    public void Saslprep_maps_like_rfc_4013(string input, string output) => Assert.Equal(output, PasswordEncoding.SaslPrep(input));

    [Theory]
    [InlineData("\u0007")]
    [InlineData("private\ue000use")]
    [InlineData("\ud800")]
    public void Saslprep_rejects_prohibited_characters(string input) => Assert.Null(PasswordEncoding.SaslPrep(input));

    private static byte[] RandomBytes(int length, int seed) => FilterEncoders.SampleData(length, alphabet: 256, seed: seed);
}
