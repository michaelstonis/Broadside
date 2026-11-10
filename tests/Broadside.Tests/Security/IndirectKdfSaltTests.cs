using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// An indirect <c>KDFSalt</c> in the encryption dictionary (issue #478). Its strings are not encrypted (ISO 32000-2 §7.6.2), so the
/// integrity MAC key derivation (ISO/TS 32004 §6.4) reads the salt as stored, like the dictionary's other entries.
/// </summary>
/// <remarks>
/// The file is <c>encrypted-mac.pdf</c> (AES-256, empty user password) with the salt moved to object 7 and the token recomputed;
/// the file encryption key is <c>fixed_bytes("encrypted-mac:file-key", 32)</c> of <c>tests/Corpus/generate.py</c>.
/// </remarks>
public sealed partial class IndirectKdfSaltTests
{
    private const string Id = "/ID [<8b05a813faaa844eeb06329838ba6583> <8b05a813faaa844eeb06329838ba6583>]";

    [Fact]
    public void An_indirect_salt_verifies_the_integrity_mac()
    {
        using PdfDocument document = PdfDocument.Open(IndirectSalt());

        Assert.Equal(PdfIntegrityStatus.Verified, document.Security!.Integrity);
        Assert.Empty(document.Diagnostics);
    }

    private static byte[] IndirectSalt()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf"));
        string[] objects = [.. ObjectPattern().Matches(text).Select(match => match.Groups[2].Value).Take(6)];
        Match salt = SaltPattern().Match(objects[5]);
        Assert.True(salt.Success);
        objects[5] = objects[5].Replace(salt.Value, "/KDFSalt 7 0 R", StringComparison.Ordinal);
        byte[] kdfSalt = Convert.FromHexString(salt.Groups[1].Value);
        byte[] fileKey = SHA256.HashData("broadside-corpus:encrypted-mac:file-key:0"u8);
        var writer = new IntegrityCodeWriter(fileKey, kdfSalt);

        string placeholder = "<" + new string('0', 2 * writer.TokenLength) + ">";
        const string rangePlaceholder = "[0 0000000000 0000000000 0000000000]";
        byte[] data = new TestPdf { Header = "%PDF-2.0", TrailerEntries = $"/Encrypt 6 0 R {Id} /AuthCode << /MACLocation /Standalone /ByteRange {rangePlaceholder} /MAC {placeholder} >>" }
            .Build([.. objects, $"<{salt.Groups[1].Value}>"]);

        string file = Encoding.Latin1.GetString(data);
        int first = file.IndexOf(placeholder, StringComparison.Ordinal);
        int start = first + placeholder.Length;
        string range = $"[0 {first:D10} {start:D10} {data.Length - start:D10}]";
        file = file.Replace(rangePlaceholder, range, StringComparison.Ordinal);
        data = Encoding.Latin1.GetBytes(file);
        byte[] digest = SHA256.HashData([.. data[..first], .. data[start..]]);
        string token = "<" + Convert.ToHexString(writer.CreateToken(digest)) + ">";
        return [.. data[..first], .. Encoding.Latin1.GetBytes(token), .. data[start..]];
    }

    [GeneratedRegex(@"(\d+) 0 obj\n(.*?)\nendobj", RegexOptions.Singleline)]
    private static partial Regex ObjectPattern();

    [GeneratedRegex(@"/KDFSalt <([0-9a-f]{64})>")]
    private static partial Regex SaltPattern();
}
