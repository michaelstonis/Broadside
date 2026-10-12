using System.Security.Cryptography;
using System.Text;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// SHA3-256, SHA3-384 and SHA3-512 (FIPS 202 §6.1) through the digest facade, which ISO/TS 32001 §5.1 adds to signatures and
/// ISO/TS 32004 Table 8 to PDF MAC tokens: the published example messages for the managed sponge macOS and the browser use, and
/// agreement with the base class library where the platform has SHA-3.
/// </summary>
public sealed class Sha3Tests
{
    private const string TwoBlocks448 = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";

    private const string TwoBlocks896 =
        "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu";

    // NIST's example messages for the SHA-2 and SHA-3 families: the empty string, "abc", and the 448-bit and 896-bit messages.
    public static TheoryData<string, string, string> Vectors => new()
    {
        { "SHA3-256", "", "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a" },
        { "SHA3-384", "", "0c63a75b845e4f7d01107d852e4c2485c51a50aaaa94fc61995e71bbee983a2ac3713831264adb47fb6bd1e058d5f004" },
        { "SHA3-512", "", "a69f73cca23a9ac5c8b567dc185a756e97c982164fe25859e0d1dcc1475c80a615b2123af1f5f94c11e3e9402c3ac558f500199d95b6d3e301758586281dcd26" },
        { "SHA3-256", "abc", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532" },
        { "SHA3-384", "abc", "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25" },
        { "SHA3-512", "abc", "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0" },
        { "SHA3-256", TwoBlocks448, "41c0dba2a9d6240849100376a8235e2c82e1b9998a999e21db32dd97496d3376" },
        { "SHA3-384", TwoBlocks448, "991c665755eb3a4b6bbdfb75c78a492e8c56a22c5c4d7e429bfdbc32b9d4ad5aa04a1f076e62fea19eef51acd0657c22" },
        { "SHA3-512", TwoBlocks448, "04a371e84ecfb5b8b77cb48610fca8182dd457ce6f326a0fd3d7ec2f1e91636dee691fbe0c985302ba1b0d8dc78c086346b533b49c030d99a27daf1139d6e75e" },
        { "SHA3-256", TwoBlocks896, "916f6061fe879741ca6469b43971dfdb28b1a32dc36cb3254e812be27aad1d18" },
        { "SHA3-384", TwoBlocks896, "79407d3b5916b59c3e30b09822974791c313fb9ecc849e406f23592d04f625dc8c709b98b43b3852b337216179aa7fc7" },
        { "SHA3-512", TwoBlocks896, "afebb2ef542e6579c50cad06d2e578f9f8dd6881d7dc824d26360feebf18a4fa73e3261122948efcfd492e74e82e2189ed0fb440d187f382270cb455f21dd185" },
    };

    // The 1600-bit message of 0xA3 bytes (NIST's SHA-3 examples with intermediate values) and one million 'a' bytes.
    public static TheoryData<string, byte, int, string> RepeatedVectors => new()
    {
        { "SHA3-256", 0xA3, 200, "79f38adec5c20307a98ef76e8324afbfd46cfd81b22e3973c65fa1bd9de31787" },
        { "SHA3-384", 0xA3, 200, "1881de2ca7e41ef95dc4732b8f5f002b189cc1e42b74168ed1732649ce1dbcdd76197a31fd55ee989f2d7050dd473e8f" },
        { "SHA3-512", 0xA3, 200, "e76dfad22084a8b1467fcf2ffa58361bec7628edf5f3fdc0e4805dc48caeeca81b7c13c30adf52a3659584739a2df46be589c51ca1a4a8416df6545a1ce8ba00" },
        { "SHA3-256", (byte)'a', 1_000_000, "5c8875ae474a3634ba4fd55ec85bffd661f32aca75c6d699d0cdcb6c115891c1" },
        { "SHA3-384", (byte)'a', 1_000_000, "eee9e24d78c1855337983451df97c8ad9eedf256c6334f8e948d252d5e0e76847aa0774ddb90a842190d2c558b4b8340" },
        { "SHA3-512", (byte)'a', 1_000_000, "3c3a876da14034ab60627c077bb98f7e120a2a5370212dffb3385a18d4f38859ed311d0a9d5141ce9cc5c66ee689b266a8aa18ace8282a0e0db596c90b0a7b87" },
    };

    // The bytes i mod 251 for lengths one short of, equal to, one past and twice each rate (136, 104 and 72 bytes), where the
    // pad10*1 rule puts its first and last bit in the same byte, fills a block exactly or spills into a new one. Expected digests
    // from Python's hashlib (OpenSSL), an implementation independent of this one and of the base class library.
    public static TheoryData<string, int, string> BoundaryVectors => new()
    {
        { "SHA3-256", 135, "fded8fd9d6551c601eeb3b7c6bc5e5cfd8aad1d015b7e9aaa9c9b9475231d5e2" },
        { "SHA3-256", 136, "cf3ccff92480a29160c2d38317c430e14749bfee1788106957dfe73f8c4930e5" },
        { "SHA3-256", 137, "ce9d7dc90913ee5d92745019479a5352c6d6279bef18ed07dc0a83ee8084daca" },
        { "SHA3-256", 272, "b7ccd55b6c2c3fa144c9e0624059294975a348b02f321abe289701d3012f7794" },
        { "SHA3-384", 103, "1f91ee551ad18f268876d1fc262f137fe196580216c5193819a95ec5222537d2a658dd129c3d8080e65ec7460f1f4704" },
        { "SHA3-384", 104, "5b8d0d5cf8b41be507be8fcbfcbdbac3a28eb368d430fed6780aaa78a93a8da4a6c50485949ca344f228be91a96005a3" },
        { "SHA3-384", 105, "4a2f0a8f2f1f4cc4605cc2537e0be28cf8b465c30f0a54b494a7128ec54ee4e85706b5e47a5697344d15cbf85680cd40" },
        { "SHA3-384", 208, "13a929eb9e4ac18a07de84b17e79bb420a86924b9dc4cd80038dd61f17770fc42460f2a0a717dd26fb6b6b4de357ae02" },
        { "SHA3-512", 71, "3ccc850d53a1287af7b4560b2ef0d43eb5d9a80d62a0e9cf1dbc040135921104d4395168e90bfc871773ebb34bca1bd67056e1cc7dc7a48ff7c3167d389f117c" },
        { "SHA3-512", 72, "5d63f2bbe971a983ac6847480106e4e1264ee3a0befd79954914e1d86e795b2e18238f12fc5e46cb9cc78efdec610a93647cc04e1c23d8caaa6a58c21dd26c07" },
        { "SHA3-512", 73, "921d9b7b2b0f3066a1646dbb058c979cb3925dec0f8c269faaa7f9648e73465ae55ec527257d5d5e1cfdbf5d6799bea1004b6186f5108c74e3b92fe924166558" },
        { "SHA3-512", 144, "e1951b8bcb58ca75a34af80a7a2b765cad4257fe383a79b55bf21f180b75f6e5b08f09598851eeea7d13486387618d6c6bf88cf23c0088a3f783f59a06d60493" },
    };

    public static TheoryData<string> Algorithms => ["SHA3-256", "SHA3-384", "SHA3-512"];

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Managed_sha3_matches_the_fips_202_examples(string algorithm, string message, string digest)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();

        byte[] output = IncrementalDigest.HashData(new HashAlgorithmName(algorithm), Encoding.ASCII.GetBytes(message));

        Assert.Equal(digest, Convert.ToHexStringLower(output));
    }

    [Theory]
    [MemberData(nameof(RepeatedVectors))]
    public void Managed_sha3_matches_the_long_examples_fed_in_uneven_pieces(string algorithm, byte value, int count, string digest)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        byte[] message = new byte[count];
        message.AsSpan().Fill(value);
        using IncrementalDigest hash = IncrementalDigest.Create(new HashAlgorithmName(algorithm));
        int[] pieces = [1, 7, 135, 136, 137, 4096];

        for (int offset = 0, piece = 0; offset < count; piece++)
        {
            int length = Math.Min(pieces[piece % pieces.Length], count - offset);
            hash.AppendData(message.AsSpan(offset, length));
            offset += length;
        }

        Assert.Equal(digest, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    [Theory]
    [MemberData(nameof(BoundaryVectors))]
    public void Managed_sha3_pads_at_every_rate_boundary(string algorithm, int length, string digest)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        byte[] message = [.. Enumerable.Range(0, length).Select(i => (byte)(i % 251))];

        Assert.Equal(digest, Convert.ToHexStringLower(IncrementalDigest.HashData(new HashAlgorithmName(algorithm), message)));
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void A_digest_starts_over_after_it_is_read(string algorithm)
    {
        var name = new HashAlgorithmName(algorithm);
        using IDisposable managed = CryptographyBackend.ForceManaged();
        using IncrementalDigest hash = IncrementalDigest.Create(name);

        hash.AppendData("discarded"u8);
        hash.GetHashAndReset();
        hash.AppendData("abc"u8);

        Assert.Equal(IncrementalDigest.HashData(name, "abc"u8), hash.GetHashAndReset());
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void Managed_sha3_agrees_with_the_platform_where_it_has_sha3(string algorithm)
    {
        var name = new HashAlgorithmName(algorithm);
        Assert.SkipUnless(
            CryptographyBackend.UseManagedSha3(name) is false,
            "This platform's base class library has no SHA-3 (macOS, iOS, the browser); the published examples cover it.");

        byte[] data = FilterEncoders.SampleData(700, alphabet: 256, seed: 3);
        for (int length = 0; length <= data.Length; length++)
        {
            byte[] managed;
            using (CryptographyBackend.ForceManaged())
            {
                managed = IncrementalDigest.HashData(name, data.AsSpan(0, length));
            }

            using var platform = IncrementalHash.CreateHash(name);
            platform.AppendData(data.AsSpan(0, length));
            Assert.Equal(platform.GetHashAndReset(), managed);
        }
    }
}
