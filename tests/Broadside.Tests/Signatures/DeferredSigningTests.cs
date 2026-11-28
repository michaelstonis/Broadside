using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Deferred signing (issue #640): a signature prepared in one step, its state saved, and the signature value injected later
/// without the document, as a remote signing service needs. ISO 32000-2 §12.8.1, §12.8.3.3; ISO/TS 32002 §5.1.4. Checked with
/// <see cref="PdfDocument.ValidateSignatures"/>, OpenSSL's CMS verification and poppler's <c>pdfsig</c>.
/// </summary>
public sealed class DeferredSigningTests
{
    public static TheoryData<string> Keys => new() { "rsa", "rsa-pss", "p256", "p384" };

    [Theory]
    [MemberData(nameof(Keys))]
    public void A_prepared_signature_completed_from_its_saved_state_validates(string key)
    {
        using X509Certificate2 certificate = Certificate(key);
        RSASignaturePadding? padding = key == "rsa-pss" ? RSASignaturePadding.Pss : null;
        (byte[] file, byte[] state) = Prepare(certificate, padding: padding);

        // A fresh, process-like context: only the file and the state blob survive.
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        byte[] value = SignValue(certificate, prepared);
        using var stream = new MemoryStream();
        stream.Write(file);
        PdfDocument.CompleteSignature(stream, prepared, value);

        byte[] signed = stream.ToArray();
        Assert.Equal(file.Length, signed.Length);
        PdfSignatureValidation validation = SignatureValidationTests.ValidateSingle(signed, SignatureValidationTests.Trusting(certificate));
        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.True(validation.CoversWholeFile);
        Assert.Equal(prepared.FieldName, validation.FieldName);

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    [Fact]
    public void The_state_round_trips_and_exposes_what_the_signer_needs()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        PdfPreparedSignature prepared = document.PrepareSignature(output, certificate, options: new PdfSignatureOptions().WithFieldName("Approval"));

        Assert.Equal("Approval", prepared.FieldName);
        Assert.Equal(HashAlgorithmName.SHA256, prepared.DigestAlgorithm);
        Assert.Equal(RSASignaturePadding.Pkcs1, prepared.RsaSignaturePadding);
        Assert.Equal(SHA256.HashData(prepared.SignedAttributes.Span), prepared.DataToSign.ToArray());
        Assert.Equal(0x31, prepared.SignedAttributes.Span[0]);

        using var state = new MemoryStream();
        prepared.Save(state);
        PdfPreparedSignature loaded = PdfPreparedSignature.Open(new MemoryStream(state.ToArray()));
        Assert.Equal(prepared.FieldName, loaded.FieldName);
        Assert.Equal(prepared.DigestAlgorithm, loaded.DigestAlgorithm);
        Assert.Equal(prepared.ReservedLength, loaded.ReservedLength);
        Assert.Equal(prepared.DataToSign.ToArray(), loaded.DataToSign.ToArray());
        Assert.Equal(prepared.SignedAttributes.ToArray(), loaded.SignedAttributes.ToArray());

        // The state holds the certificate but never its private key, and none of the document.
        byte[] blob = state.ToArray();
        Assert.True(blob.Length < 8 * 1024);
        Assert.True(certificate.HasPrivateKey);
        using RSA rsa = certificate.GetRSAPrivateKey()!;
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        Assert.True(blob.AsSpan().IndexOf(privateKey.AsSpan(privateKey.Length - 64)) < 0);

        // The document now reads the prepared file: the field holds a signature dictionary, and a second preparation is a new field.
        Assert.Equal(output.Length, document.Revisions[^1].Length);
        Assert.True(document.AcroForm!.FindField("Approval") is PdfSignatureField { IsSigned: true });
    }

    [Fact]
    public void A_prepared_file_changed_after_preparation_is_refused_and_left_as_it_is()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        byte[] value = SignValue(certificate, prepared);

        byte[] tampered = (byte[])file.Clone();
        tampered[20] ^= 0x01;
        using var stream = new MemoryStream();
        stream.Write(tampered);
        InvalidOperationException changed = Assert.Throws<InvalidOperationException>(() => PdfDocument.CompleteSignature(stream, prepared, value));
        Assert.Contains("changed after preparation", changed.Message, StringComparison.Ordinal);
        Assert.Equal(tampered, stream.ToArray());

        using var longer = new MemoryStream();
        longer.Write(file);
        longer.Write("%"u8);
        Assert.Throws<InvalidOperationException>(() => PdfDocument.CompleteSignature(longer, prepared, value));
    }

    [Fact]
    public async Task A_second_completion_is_refused()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        byte[] value = SignValue(certificate, prepared);
        using var stream = new MemoryStream();
        stream.Write(file);

        await PdfDocument.CompleteSignatureAsync(stream, prepared, value, cancellationToken: TestContext.Current.CancellationToken);
        byte[] completed = stream.ToArray();

        InvalidOperationException again = Assert.Throws<InvalidOperationException>(() => PdfDocument.CompleteSignature(stream, prepared, value));
        Assert.Contains("already completed", again.Message, StringComparison.Ordinal);
        Assert.Equal(completed, stream.ToArray());
        Assert.Equal(PdfSignatureStatus.Valid, SignatureValidationTests.ValidateSingle(completed, SignatureValidationTests.Trusting(certificate)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_deferred_b_t_signature_carries_a_token_over_its_signature_value(bool asynchronous)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);
        PdfSignatureOptions options = new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached).WithTimestampClient(client);
        (byte[] file, byte[] state) = Prepare(certificate, options);
        Assert.Empty(authority.Requests);

        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        byte[] value = SignValue(certificate, prepared);
        using var stream = new MemoryStream();
        stream.Write(file);
        if (asynchronous)
        {
            await PdfDocument.CompleteSignatureAsync(stream, prepared, value, client, TestContext.Current.CancellationToken);
        }
        else
        {
            PdfDocument.CompleteSignature(stream, prepared, value, client);
        }

        byte[] signed = stream.ToArray();
        SignedFile signature = SignedFile.Last(signed);
        var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
        cms.Decode(signature.Cms);
        SignerInfo signer = Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
        CryptographicAttributeObject attribute = Assert.Single(signer.UnsignedAttributes.Cast<CryptographicAttributeObject>());
        Assert.Equal("1.2.840.113549.1.9.16.2.14", attribute.Oid.Value);
        Assert.True(Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out Rfc3161TimestampToken? token, out _));
        Assert.True(token!.VerifySignatureForSignerInfo(signer, out _));
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        Assert.Single(authority.Requests);
    }

    [Fact]
    public void A_token_larger_than_the_reserved_room_is_refused_before_anything_is_written()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using var authority = new TestTimestampAuthority { Padding = 6000 };
        using var http = new HttpClient(authority, disposeHandler: false);
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address).WithTokenSizeEstimate(4096);
        (byte[] file, byte[] state) = Prepare(certificate, new PdfSignatureOptions().WithTimestampClient(client));
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        using var stream = new MemoryStream();
        stream.Write(file);

        InvalidOperationException oversized = Assert.Throws<InvalidOperationException>(
            () => PdfDocument.CompleteSignature(stream, prepared, SignValue(certificate, prepared), client));

        Assert.Contains("TokenSizeEstimate", oversized.Message, StringComparison.Ordinal);
        Assert.Equal(file, stream.ToArray());
    }

    [Fact]
    public void A_timestamp_without_room_reserved_for_it_is_refused()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        using var stream = new MemoryStream();
        stream.Write(file);

        Assert.Throws<InvalidOperationException>(() => PdfDocument.CompleteSignature(
            stream, prepared, SignValue(certificate, prepared), new PdfTimestampClient(http, TestTimestampAuthority.Address)));
        Assert.Empty(authority.Requests);
        Assert.Equal(file, stream.ToArray());
    }

    [Fact]
    public void A_signature_value_made_with_another_key_is_refused()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using X509Certificate2 other = TestCertificates.SelfSigned("CN=Broadside other signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        using var stream = new MemoryStream();
        stream.Write(file);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => PdfDocument.CompleteSignature(stream, prepared, SignValue(other, prepared)));
        Assert.Equal("signatureValue", refused.ParamName);
        Assert.Equal(file, stream.ToArray());
    }

    [Fact]
    public void A_cms_object_from_signed_cms_completes_the_signature()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        using var stream = new MemoryStream();
        stream.Write(file);

        byte[] cms = PlatformCms(certificate, CoveredBytes(file, prepared));
        PdfDocument.CompleteSignatureWithCms(stream, prepared, cms);

        byte[] signed = stream.ToArray();
        Assert.Equal(PdfSignatureStatus.Valid, SignatureValidationTests.ValidateSingle(signed, SignatureValidationTests.Trusting(certificate)).Status);
        SignedFile signature = SignedFile.Last(signed);
        Assert.Equal(cms, signature.Cms);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
    }

    [Fact]
    public void A_cms_object_over_other_bytes_or_by_another_signer_is_refused()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using X509Certificate2 other = TestCertificates.SelfSigned("CN=Broadside other signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));
        using var stream = new MemoryStream();
        stream.Write(file);
        byte[] covered = CoveredBytes(file, prepared);

        ArgumentException otherBytes = Assert.Throws<ArgumentException>(
            () => PdfDocument.CompleteSignatureWithCms(stream, prepared, PlatformCms(certificate, [.. covered, 0x20])));
        Assert.Contains("message-digest", otherBytes.Message, StringComparison.Ordinal);
        ArgumentException otherSigner = Assert.Throws<ArgumentException>(
            () => PdfDocument.CompleteSignatureWithCms(stream, prepared, PlatformCms(other, covered)));
        Assert.Contains("not the prepared certificate", otherSigner.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PdfDocument.CompleteSignatureWithCms(stream, prepared, [0x30, 0x03, 0x02, 0x01, 0x01]));
        Assert.Equal(file, stream.ToArray());
    }

    [Fact]
    public void An_encrypted_document_is_prepared_and_completed()
    {
        // §7.6.2: the signature dictionary's Contents is not encrypted, so completion writes it as it is.
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("encrypted-aes-256.pdf"));
        using var output = new MemoryStream();
        PdfPreparedSignature prepared = document.PrepareSignature(output, certificate);

        PdfDocument.CompleteSignature(output, prepared, SignValue(certificate, prepared));

        Assert.Equal(PdfSignatureStatus.Valid, SignatureValidationTests.ValidateSingle(output.ToArray(), SignatureValidationTests.Trusting(certificate)).Status);
    }

    [Fact]
    public void A_document_with_an_integrity_mac_is_refused_and_left_as_it_was()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("encrypted-mac.pdf"), new PdfOptions().WithPassword("owner"));
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => document.PrepareSignature(output, certificate));

        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_stream_that_cannot_be_written_in_place_is_refused()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        (byte[] file, byte[] state) = Prepare(certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Open(new MemoryStream(state));

        Assert.Throws<ArgumentException>(() => PdfDocument.CompleteSignature(new MemoryStream(file, writable: false), prepared, SignValue(certificate, prepared)));
    }

    [Fact]
    public void A_damaged_or_later_state_is_refused_with_a_clear_exception()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside deferred signer");
        (_, byte[] state) = Prepare(certificate);

        Assert.Throws<InvalidDataException>(() => PdfPreparedSignature.Open(new MemoryStream(state[..^3])));
        Assert.Throws<InvalidDataException>(() => PdfPreparedSignature.Open(new MemoryStream([.. state, 0x00])));
        Assert.Throws<InvalidDataException>(() => PdfPreparedSignature.Open(new MemoryStream([])));

        // The version is the first value of the sequence: raise it.
        var reader = new AsnReader(state, AsnEncodingRules.DER);
        AsnReader sequence = reader.ReadSequence();
        Assert.True(sequence.TryReadInt32(out int version));
        Assert.Equal(1, version);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(2);
            writer.WriteEncodedValue(sequence.ReadEncodedValue().Span);
        }

        NotSupportedException later = Assert.Throws<NotSupportedException>(() => PdfPreparedSignature.Open(new MemoryStream(writer.Encode())));
        Assert.Contains("format version 2", later.Message, StringComparison.Ordinal);
    }

    /// <summary>Prepares a signature of <c>empty-page.pdf</c> and returns the prepared file and the saved state; the document is disposed.</summary>
    private static (byte[] File, byte[] State) Prepare(X509Certificate2 certificate, PdfSignatureOptions? options = null, RSASignaturePadding? padding = null)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();
        PdfPreparedSignature prepared = document.PrepareSignature(output, certificate, options: options, rsaSignaturePadding: padding);
        using var state = new MemoryStream();
        prepared.Save(state);
        return (output.ToArray(), state.ToArray());
    }

    /// <summary>What a remote signer does: signs <see cref="PdfPreparedSignature.DataToSign"/> with the certificate's private key.</summary>
    private static byte[] SignValue(X509Certificate2 certificate, PdfPreparedSignature prepared)
    {
        if (certificate.GetECDsaPrivateKey() is { } ecdsa)
        {
            using (ecdsa)
            {
                // IEEE P1363 r||s, what a PKCS #11 token returns: the document writes it as DER.
                return ecdsa.SignHash(prepared.DataToSign.Span);
            }
        }

        using RSA rsa = certificate.GetRSAPrivateKey()!;
        return rsa.SignHash(prepared.DataToSign.Span, prepared.DigestAlgorithm, prepared.RsaSignaturePadding!);
    }

    private static X509Certificate2 Certificate(string key) => key switch
    {
        "p256" => TestCertificates.SelfSignedEcdsa("CN=Broadside deferred P-256 signer", ECCurve.NamedCurves.nistP256),
        "p384" => TestCertificates.SelfSignedEcdsa("CN=Broadside deferred P-384 signer", ECCurve.NamedCurves.nistP384),
        _ => TestCertificates.SelfSigned("CN=Broadside deferred RSA signer"),
    };

    /// <summary>The prepared file's bytes the byte ranges cover, read from the file itself.</summary>
    private static byte[] CoveredBytes(byte[] file, PdfPreparedSignature prepared)
    {
        using PdfDocument document = PdfDocument.Open(file);
        var field = (PdfSignatureField)document.AcroForm!.FindField(prepared.FieldName)!;
        var range = (Broadside.Objects.CosArray)field.SignatureDictionary![new Broadside.Objects.CosName("ByteRange")];
        long[] byteRange = [.. range.Select(element => ((Broadside.Objects.CosInteger)element).Value)];
        return [.. file.AsSpan(0, (int)byteRange[1]), .. file.AsSpan((int)byteRange[2], (int)byteRange[3])];
    }

    /// <summary>A detached CMS signature the platform builds over <paramref name="content"/>, as a signing service returning CMS would.</summary>
    private static byte[] PlatformCms(X509Certificate2 certificate, byte[] content)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };

        // With no signed attribute of its own, the platform signs the content directly; one makes it add the message digest (RFC 5652 §5.3).
        signer.SignedAttributes.Add(new Pkcs9SigningTime());
        cms.ComputeSignature(signer);
        return cms.Encode();
    }
}
