using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>The integrity MAC of an encrypted document. ISO/TS 32004 §5, §6, Annex B.</summary>
public sealed class IntegrityTests
{
    [Fact]
    public void A_standalone_mac_that_covers_the_whole_file_is_verified()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-mac.pdf"));

        Assert.Equal(PdfIntegrityStatus.Verified, document.Security!.Integrity);
        Assert.True(document.Security.RequiresIntegrityCode);
        Assert.Equal(-4100, document.Security.RawPermissions);
        Assert.Empty(document.Diagnostics);
    }

    // ISO/TS 32004 Table 8 and ISO/TS 32001 §5.1: SHA3-256 digests are checked on every platform, with the managed SHA-3 where the
    // base class library has none (macOS, iOS, the browser) and with it forced where the library has one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_mac_with_sha3_digests_is_verified_on_every_platform(bool forceManaged)
    {
        using IDisposable? managed = forceManaged ? CryptographyBackend.ForceManaged() : null;

        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-mac-sha3.pdf"));

        Assert.Equal(PdfIntegrityStatus.Verified, document.Security!.Integrity);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_changed_byte_under_a_sha3_mac_is_a_mismatch()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac-sha3.pdf"))
            .Replace("Integrity check target: original", "Integrity check target: Original", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("IntegrityCodeMismatch", diagnostic.Code);
        Assert.Contains("dataDigest", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mac_is_verified_whichever_password_opens_the_document()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-mac.pdf"), new PdfOptions().WithPassword("owner"));

        Assert.Equal(PdfIntegrityStatus.Verified, document.Security!.Integrity);
    }

    [Fact]
    public void A_tampered_file_opens_with_a_mismatch_diagnostic_in_lenient_mode()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-mac-tampered.pdf"));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("IntegrityCodeMismatch", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("BT /F1 24 Tf 72 700 Td (Integrity MAC (R6)) Tj ET", EncryptedCorpusTests.ContentText(document));
    }

    [Fact]
    public void A_tampered_file_throws_in_strict_mode()
    {
        var exception = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Corpus.Path("encrypted-mac-tampered.pdf"), new PdfOptions().UseStrict()));

        Assert.Equal("IntegrityCodeMismatch", exception.Diagnostic.Code);
    }

    [Fact]
    public void Bytes_appended_after_the_covered_range_make_the_mac_incomplete()
    {
        byte[] file = [.. Corpus.Bytes("encrypted-mac.pdf"), .. "% appended\n"u8];

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);

        // The appended line also follows the last %%EOF, which §7.5.5 forbids (issue #47).
        Assert.Equal(["EndOfFileMarkerNotLast", "IntegrityCodeIncomplete"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_missing_mac_that_the_permissions_require_is_reported()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf")).Replace("/AuthCode", "/AuthCodX", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(PdfIntegrityStatus.Missing, document.Security!.Integrity);
        Assert.Equal("IntegrityCodeMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_mac_whose_token_was_changed_is_reported()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf"));
        int mac = text.IndexOf("/MAC <", StringComparison.Ordinal) + "/MAC <".Length;
        int end = text.IndexOf('>', mac);
        char[] chars = text.ToCharArray();
        chars[end - 1] = chars[end - 1] == '0' ? '1' : '0'; // the last byte of the HMAC value

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(chars));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Assert.Equal("IntegrityCodeMismatch", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_mac_with_the_wrong_key_derivation_salt_does_not_unwrap()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf"));
        int salt = text.IndexOf("/KDFSalt <", StringComparison.Ordinal) + "/KDFSalt <".Length;
        char[] chars = text.ToCharArray();
        chars[salt] = chars[salt] == '0' ? '1' : '0';

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(chars));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("IntegrityCodeMismatch", diagnostic.Code);
        Assert.Contains("unwrap", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_range_that_does_not_frame_the_mac_string_is_invalid()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf"));
        int range = text.IndexOf("/ByteRange [0 ", StringComparison.Ordinal) + "/ByteRange [0 ".Length;
        long l1 = long.Parse(text.AsSpan(range, 10), CultureInfo.InvariantCulture);
        text = string.Concat(text.AsSpan(0, range), (l1 - 1).ToString("D10", CultureInfo.InvariantCulture), text.AsSpan(range + 10)); // one byte short

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Assert.Equal("IntegrityCodeInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_mac_whose_location_is_unknown_is_not_verified_and_only_noted()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-mac.pdf")).Replace("/MACLocation /Standalone", "/MACLocation /Elsewhere ", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text), new PdfOptions().UseStrict());

        Assert.Equal(PdfIntegrityStatus.NotVerified, document.Security!.Integrity);
        Assert.Equal(DiagnosticSeverity.Information, Assert.Single(document.Diagnostics).Severity);
    }
}
