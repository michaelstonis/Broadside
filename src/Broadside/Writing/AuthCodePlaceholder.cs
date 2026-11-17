using System.Buffers;
using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;

namespace Broadside.Writing;

/// <summary>
/// The trailer's <c>AuthCode</c> dictionary while a file is written: written with a byte range and a MAC string of their final
/// widths, then filled in once every other byte of the file is known (ISO/TS 32004 §5.2.3, Table 6).
/// </summary>
/// <remarks>
/// <para>
/// ISO/TS 32004 Table 6: <c>MACLocation</c> <c>Standalone</c>; <c>ByteRange</c> <c>[0 L1 S L2]</c>, the file digested from its header
/// to the MAC string and from after it to the end (§6.5.1: the whole file but the token, in an unsigned revision); <c>MAC</c> the
/// DER-encoded token as a hexadecimal string, its region exactly <c>&lt;</c>, the digits and <c>&gt;</c>, with no white space and no
/// padding. Space for it "shall be allocated before it is computed": every field of the token has a fixed length
/// (<see cref="IntegrityCodeWriter.TokenLength"/>).
/// </para>
/// <para>
/// The byte range's three offsets are written with leading zeros to a fixed width (at least ten digits, as the classic
/// cross-reference table's offsets, §7.5.4), so filling them in moves no byte. All entries are direct objects (Table 6).
/// </para>
/// <para>ISO/TS 32004 §6.6.</para>
/// </remarks>
internal sealed class AuthCodePlaceholder
{
    private static readonly CosName MacLocation = new("MACLocation");
    private static readonly CosName Standalone = new("Standalone");
    private static readonly CosName Mac = new("MAC");
    private static readonly CosName AttachedToSig = new("AttachedToSig");
    private static readonly CosName SigObjRef = new("SigObjRef");

    private readonly int _width;
    private readonly byte[] _byteRangeText;
    private readonly byte[] _macText;

    private AuthCodePlaceholder(CosDictionary dictionary, int width, byte[] byteRangeText, byte[] macText)
    {
        Dictionary = dictionary;
        _width = width;
        _byteRangeText = byteRangeText;
        _macText = macText;
    }

    /// <summary>Gets the trailer key the dictionary is written under (ISO/TS 32004 §5.2.1, Table 5).</summary>
    public static CosName Key { get; } = new("AuthCode");

    /// <summary>Gets the dictionary: placeholders until <see cref="Fill"/>, the written values after it.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>
    /// The <c>AuthCode</c> dictionary of a signed revision, whose PDF MAC token is an unsigned attribute of the signature (ISO/TS 32004
    /// §6.5.2, Table 6): <c>MACLocation</c> <c>AttachedToSig</c> and <c>SigObjRef</c>, no byte range and no MAC.
    /// </summary>
    /// <param name="signature">The signature dictionary the token is attached to.</param>
    /// <returns>The dictionary.</returns>
    public static CosDictionary AttachedTo(CosReference signature) => new()
    {
        [MacLocation] = AttachedToSig,
        [SigObjRef] = signature,
    };

    /// <summary>Creates the placeholder dictionary for a token of <paramref name="tokenLength"/> bytes.</summary>
    /// <param name="tokenLength">The DER-encoded token's length.</param>
    /// <param name="writtenBefore">How many bytes of the file, from its header, precede the cross-reference section.</param>
    /// <returns>The placeholder.</returns>
    public static AuthCodePlaceholder Create(int tokenLength, long writtenBefore)
    {
        // Wide enough for any offset in a file twice as long as what is written before the section, plus a gigabyte of section.
        long bound = (2 * writtenBefore) + (1L << 30);
        int width = Math.Max(10, bound.ToString(CultureInfo.InvariantCulture).Length);
        var widest = new CosInteger(long.Parse(new string('9', width), CultureInfo.InvariantCulture));
        var byteRange = new CosArray([new CosInteger(0), widest, widest, widest]);
        var mac = new CosString(new byte[tokenLength], hexadecimal: true);
        var dictionary = new CosDictionary
        {
            [MacLocation] = Standalone,
            [KnownNames.ByteRange] = byteRange,
            [Mac] = mac,
        };
        return new AuthCodePlaceholder(dictionary, width, Serialize(byteRange), Serialize(mac));
    }

    /// <summary>
    /// Fills in the byte range and the token in the section just written (the file's last bytes), digesting every byte of it the
    /// range covers into <paramref name="digest"/>, which holds every byte before it.
    /// </summary>
    /// <param name="section">The bytes of the cross-reference section, trailer and end of file, writable.</param>
    /// <param name="sectionStart">The section's offset from the file's header.</param>
    /// <param name="digest">The file's digest so far: every byte from the header to <paramref name="sectionStart"/>.</param>
    /// <param name="writer">The token composer.</param>
    /// <exception cref="InvalidOperationException">The placeholders are not in the section, or an offset outgrows its width.</exception>
    public void Fill(Span<byte> section, long sectionStart, IncrementalDigest digest, IntegrityCodeWriter writer)
    {
        int macAt = FillByteRange(section, sectionStart, sectionStart + section.Length);
        digest.AppendData(section[..macAt]);
        digest.AppendData(section[(macAt + MacLength)..]);
        FillToken(section, macAt, digest, writer);
    }

    /// <summary>Gets the length of the MAC string's region: <c>&lt;</c>, the token's hexadecimal digits and <c>&gt;</c> (Table 6).</summary>
    public int MacLength => _macText.Length;

    /// <summary>
    /// Fills in the byte range in <paramref name="region"/>, a written stretch of the file that holds the placeholders, for a file of
    /// <paramref name="fileLength"/> bytes: <c>[0 L1 S L2]</c> with the MAC string the only bytes left out (ISO/TS 32004 §6.5.1).
    /// Filling it in moves no byte. The MAC string is left as it was, for <see cref="FillToken"/> once the rest of the file is digested.
    /// </summary>
    /// <param name="region">The written bytes that hold the dictionary, writable.</param>
    /// <param name="regionStart">The region's offset from the file's header.</param>
    /// <param name="fileLength">The length of the whole file from its header.</param>
    /// <returns>The offset of the MAC string's region (its <c>&lt;</c>) in <paramref name="region"/>.</returns>
    /// <exception cref="InvalidOperationException">The placeholders are not in the region, or an offset outgrows its width.</exception>
    public int FillByteRange(Span<byte> region, long regionStart, long fileLength)
    {
        int rangeAt = region.IndexOf(_byteRangeText);
        int macAt = rangeAt < 0 ? -1 : region[rangeAt..].IndexOf(_macText);
        if (macAt < 0)
        {
            throw new InvalidOperationException("The AuthCode placeholders are not in the written trailer.");
        }

        macAt += rangeAt;
        long first = regionStart + macAt;
        long resume = first + _macText.Length;
        long rest = fileLength - resume;
        string range = string.Create(CultureInfo.InvariantCulture, $"[0 {Padded(first)} {Padded(resume)} {Padded(rest)}]");
        if (rest < 0 || range.Length != _byteRangeText.Length)
        {
            throw new InvalidOperationException("The AuthCode byte range does not fit its placeholder.");
        }

        Encoding.ASCII.GetBytes(range, region.Slice(rangeAt, range.Length));
        Dictionary[KnownNames.ByteRange] = new CosArray([new CosInteger(0), new CosInteger(first), new CosInteger(resume), new CosInteger(rest)]);
        return macAt;
    }

    /// <summary>
    /// Composes the token from <paramref name="digest"/>, which holds every byte the byte range covers (§6.6.2), and writes it as the
    /// MAC string at <paramref name="macAt"/> in <paramref name="region"/>.
    /// </summary>
    /// <param name="region">The written bytes that hold the dictionary, writable; its byte range already filled in.</param>
    /// <param name="macAt">The offset of the MAC string's region, from <see cref="FillByteRange"/>.</param>
    /// <param name="digest">The digest of the whole file but the MAC string; reset afterward.</param>
    /// <param name="writer">The token composer.</param>
    /// <exception cref="InvalidOperationException">The token does not fit its placeholder.</exception>
    public void FillToken(Span<byte> region, int macAt, IncrementalDigest digest, IntegrityCodeWriter writer)
    {
        Span<byte> dataDigest = stackalloc byte[IntegrityCodeWriter.DigestLength];
        digest.GetHashAndReset(dataDigest);
        byte[] token = writer.CreateToken(dataDigest);
        if (token.Length * 2 != _macText.Length - 2)
        {
            throw new InvalidOperationException("The PDF MAC token does not fit its placeholder.");
        }

        Encoding.ASCII.GetBytes(Convert.ToHexString(token), region.Slice(macAt + 1, token.Length * 2));
        Dictionary[Mac] = CosString.FromOwnedBytes(token, hexadecimal: true);
    }

    private string Padded(long value) => value.ToString(new string('0', _width), CultureInfo.InvariantCulture);

    private static byte[] Serialize(CosObject value)
    {
        var bytes = new ArrayBufferWriter<byte>();
        CosWriter.Write(value, bytes);
        return bytes.WrittenSpan.ToArray();
    }
}
