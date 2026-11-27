using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Signatures;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>prepared-signature-state</c> target (issue #640): the input is a deferred signature's state blob read by
/// <see cref="PdfPreparedSignature.Open"/>. The documented outcomes besides a prepared signature are
/// <see cref="InvalidDataException"/> (damaged) and <see cref="NotSupportedException"/> (a later format version); anything else
/// escaping is a finding. Invariant: a blob that loads is saved again byte for byte (the state is DER, so it has one encoding).
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 and §12.8.3.3. The smoke runner feeds whole corpus files, which are no states: an input starting with
/// <c>%PDF-</c> is turned into <see cref="ValidState"/> with up to 16 bytes changed at positions the file's bytes choose, so its
/// mutations corrupt a state that loads. <c>--seeds</c> writes <see cref="ValidState"/> itself. The certificate is fixed (a
/// self-signed P-256 certificate whose private key was discarded), so every run sees the same state.
/// </remarks>
internal static class PreparedSignatureStateTarget
{
    private const string Certificate =
        "MIIBdjCCARygAwIBAgIUDVxBr3nNgiGegsrv70p3d8zJdWswCgYIKoZIzj0EAwIwIDEeMBwGA1UEAwwVQnJvYWRzaWRlIGZ1enogc2lnbmVyMCAXDTI2MTEy" +
        "NzIwNDg0MFoYDzIxMjYxMTAzMjA0ODQwWjAgMR4wHAYDVQQDDBVCcm9hZHNpZGUgZnV6eiBzaWduZXIwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAASIH1Pw" +
        "gC6YdYK40gWCaEy/mLqMKQlgcXehhYdLmyaO5Hh/7NA2sK+kYjl7W7yuO+LJtaftMen5m1L7VGFPoWM7ozIwMDAdBgNVHQ4EFgQU2ZxaMj/5nd33SUGrPpC+" +
        "RXLt0N8wDwYDVR0TAQH/BAUwAwEB/zAKBggqhkjOPQQDAgNIADBFAiBp3bH3vX/7ZhGOnlGzWnyVcbTMnbrDU8Cam5mCDWzRjgIhALxtj1D0sWy5wNuFGGlo" +
        "vG3TcXlw8UdUUOT5dCF1BIXS";

    private static readonly Lazy<byte[]> Valid = new(ValidState);

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] state = data.StartsWith("%PDF-"u8) ? Perturb(data) : data.ToArray();
        PdfPreparedSignature prepared;
        try
        {
            prepared = PdfPreparedSignature.Open(new MemoryStream(state, writable: false));
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException && !state.AsSpan().SequenceEqual(Valid.Value))
        {
            return;
        }

        using var saved = new MemoryStream();
        prepared.Save(saved);
        if (!saved.ToArray().AsSpan().SequenceEqual(state))
        {
            throw new InvalidOperationException($"A {state.Length}-byte state that loads saves as {saved.Length} different bytes.");
        }

        if (prepared.ReservedLength <= 0 || prepared.DataToSign.Length == 0 || prepared.FieldName.Length == 0)
        {
            throw new InvalidOperationException("A state that loads describes an impossible signature.");
        }
    }

    /// <summary>A state as preparing writes it: a P-256 signature over SHA-256 of a 1000-byte file with 4000 bytes reserved.</summary>
    public static byte[] ValidState()
    {
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(Certificate));
        byte[] messageDigest = [.. Enumerable.Repeat((byte)0x5A, 32)];
        byte[] signedAttributes = CmsSignedData.SignedAttributes(messageDigest, HashAlgorithmName.SHA256, certificate);
        PdfPreparedSignature prepared = PdfPreparedSignature.Create(
            "Signature1",
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1,
            [0, 600, 600 + 8002, 398],
            4000,
            0,
            [.. Enumerable.Repeat((byte)0xA5, 32)],
            signedAttributes,
            certificate,
            [],
            CmsSignedData.ReadKey(certificate));
        using var state = new MemoryStream();
        prepared.Save(state);
        return state.ToArray();
    }

    private static byte[] Perturb(ReadOnlySpan<byte> file)
    {
        byte[] state = (byte[])Valid.Value.Clone();
        ReadOnlySpan<byte> choices = file.Length > 8 ? file[8..] : [];
        for (int index = 0; index + 1 < choices.Length && index < 32; index += 2)
        {
            state[choices[index] % state.Length] ^= choices[index + 1];
        }

        return state;
    }
}
