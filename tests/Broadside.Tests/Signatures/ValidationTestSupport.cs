using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Broadside.Tests.Signatures;

/// <summary>Certificate hierarchies for validation tests, made with <see cref="CertificateRequest"/>: nothing from a store or the network.</summary>
internal static class ValidationCertificates
{
    private static int _serial = 0x1960;

    /// <summary>A self-signed certification authority with an RSA key.</summary>
    public static X509Certificate2 Authority(string subject, int? pathLength = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddAuthorityExtensions(request, pathLength);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(2));
    }

    /// <summary>A self-signed certification authority with an ECDSA key on <paramref name="curve"/>.</summary>
    public static X509Certificate2 EcdsaAuthority(string subject, ECCurve curve, HashAlgorithmName digest)
    {
        using var ecdsa = ECDsa.Create(curve);
        var request = new CertificateRequest(subject, ecdsa, digest);
        AddAuthorityExtensions(request, null);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(2));
    }

    /// <summary>A certificate authority <paramref name="issuer"/> issues, with an RSA key.</summary>
    public static X509Certificate2 IssueAuthority(X509Certificate2 issuer, string subject, int? pathLength = null, bool certificateAuthority = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (certificateAuthority)
        {
            AddAuthorityExtensions(request, pathLength);
        }
        else
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, critical: true));
        }

        using X509Certificate2 issued = Issue(request, issuer, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return issued.CopyWithPrivateKey(rsa);
    }

    /// <summary>A signer certificate with an RSA key, issued by <paramref name="issuer"/>, valid from yesterday for six months.</summary>
    public static X509Certificate2 IssueRsaSigner(
        X509Certificate2 issuer,
        string subject,
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
        RSASignaturePadding? issuerPadding = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        using X509Certificate2 issued = Issue(request, issuer, issuerPadding, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6));
        return issued.CopyWithPrivateKey(rsa);
    }

    /// <summary>A signer certificate with an ECDSA key on <paramref name="curve"/>, issued by <paramref name="issuer"/>.</summary>
    public static X509Certificate2 IssueEcdsaSigner(X509Certificate2 issuer, string subject, ECCurve curve)
    {
        using var ecdsa = ECDsa.Create(curve);
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        using X509Certificate2 issued = Issue(request, issuer, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6));
        return issued.CopyWithPrivateKey(ecdsa);
    }

    /// <summary>
    /// Signs <paramref name="request"/> with <paramref name="issuer"/>'s private key, whatever its type and whether or not it is a
    /// certification authority, with the authority key identifier of its subject key identifier.
    /// </summary>
    private static X509Certificate2 Issue(CertificateRequest request, X509Certificate2 issuer, RSASignaturePadding? padding, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        if (issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { } key)
        {
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(key));
        }

        using RSA? rsa = issuer.GetRSAPrivateKey();
        using ECDsa? ecdsa = rsa is null ? issuer.GetECDsaPrivateKey() : null;
        X509SignatureGenerator generator = rsa is not null
            ? X509SignatureGenerator.CreateForRSA(rsa, padding ?? RSASignaturePadding.Pkcs1)
            : X509SignatureGenerator.CreateForECDsa(ecdsa!);
        return request.Create(issuer.SubjectName, generator, notBefore, notAfter, NextSerial());
    }

    private static void AddAuthorityExtensions(CertificateRequest request, int? pathLength)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, pathLength is not null, pathLength ?? 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
    }

    private static byte[] NextSerial()
    {
        int serial = Interlocked.Increment(ref _serial);
        return [0x01, (byte)(serial >> 16), (byte)(serial >> 8), (byte)serial];
    }
}

/// <summary>
/// Changes a file Broadside signed so validation sees what another signer, or an attacker, would have written: a different CMS object
/// (made by the platform PKCS package, an independent encoder), a different SubFilter, a rewritten ByteRange, changed bytes.
/// </summary>
internal static class SignedFileEditor
{
    /// <summary>
    /// Replaces the last signature's CMS object with the one <paramref name="cms"/> makes over the bytes its byte range covers, after
    /// renaming its SubFilter to <paramref name="subFilter"/> when given (in place, padded with spaces, so no offset moves).
    /// </summary>
    public static byte[] ReplaceSignature(byte[] signed, Func<byte[], byte[]> cms, string? subFilter = null)
    {
        byte[] file = (byte[])signed.Clone();
        if (subFilter is not null)
        {
            Replace(file, "/SubFilter /adbe.pkcs7.detached", "/SubFilter /" + subFilter);
        }

        SignedFile signature = SignedFile.Last(file);
        WriteContents(file, signature.ByteRange, cms(signature.CoveredBytes()));
        return file;
    }

    /// <summary>Writes <paramref name="cms"/> in hexadecimal into the gap the byte range leaves, zero-padded.</summary>
    public static void WriteContents(byte[] file, long[] byteRange, byte[] cms)
    {
        Span<byte> digits = file.AsSpan((int)byteRange[1] + 1, (int)(byteRange[2] - byteRange[1] - 2));
        Assert.True(2 * cms.Length <= digits.Length, $"The CMS object takes {cms.Length} bytes; {digits.Length / 2} are reserved.");
        digits.Fill((byte)'0');
        Encoding.ASCII.GetBytes(Convert.ToHexString(cms), digits);
    }

    /// <summary>Rewrites the last <c>ByteRange</c> array in place, padded with spaces before its <c>]</c>.</summary>
    public static byte[] RewriteByteRange(byte[] signed, params long[] byteRange)
    {
        byte[] file = (byte[])signed.Clone();
        int start = LastIndexOf(file, "/ByteRange [") + "/ByteRange ".Length;
        int end = Array.IndexOf(file, (byte)']', start) + 1;
        string text = "[" + string.Join(' ', byteRange.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        Assert.True(text.Length + 1 <= end - start, "The new byte range does not fit.");
        file.AsSpan(start, end - start).Fill((byte)' ');
        Encoding.ASCII.GetBytes(text, file.AsSpan(start));
        file[end - 1] = (byte)']';
        return file;
    }

    /// <summary>Replaces the last occurrence of <paramref name="find"/> with <paramref name="replacement"/>, padded with spaces.</summary>
    public static void Replace(byte[] file, string find, string replacement)
    {
        Assert.True(replacement.Length <= find.Length);
        int start = LastIndexOf(file, find);
        file.AsSpan(start, find.Length).Fill((byte)' ');
        Encoding.ASCII.GetBytes(replacement, file.AsSpan(start));
    }

    /// <summary>A detached (or, for <paramref name="detached"/> false, encapsulating) CMS signature from the platform PKCS package.</summary>
    public static byte[] PlatformCms(ContentInfo content, CmsSigner signer, bool detached = true)
    {
        var cms = new SignedCms(content, detached);
        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }

    private static int LastIndexOf(byte[] file, string text)
    {
        int index = file.AsSpan().LastIndexOf(Encoding.ASCII.GetBytes(text));
        Assert.True(index >= 0, $"'{text}' is not in the file.");
        return index;
    }
}
