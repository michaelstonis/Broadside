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
/// </remarks>
internal sealed class AuthCodePlaceholder
{
    private static readonly CosName MacLocation = new("MACLocation");
    private static readonly CosName Standalone = new("Standalone");
    private static readonly CosName Mac = new("MAC");

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
        int rangeAt = section.IndexOf(_byteRangeText);
        int macAt = rangeAt < 0 ? -1 : section[rangeAt..].IndexOf(_macText);
        if (macAt < 0)
        {
            throw new InvalidOperationException("The AuthCode placeholders are not in the written trailer.");
        }

        macAt += rangeAt;
        long first = sectionStart + macAt;
        long resume = first + _macText.Length;
        long rest = sectionStart + section.Length - resume;
        string range = string.Create(CultureInfo.InvariantCulture, $"[0 {Padded(first)} {Padded(resume)} {Padded(rest)}]");
        if (range.Length != _byteRangeText.Length)
        {
            throw new InvalidOperationException("The AuthCode byte range does not fit its placeholder.");
        }

        Encoding.ASCII.GetBytes(range, section.Slice(rangeAt, range.Length));
        digest.AppendData(section[..macAt]);
        digest.AppendData(section[(macAt + _macText.Length)..]);
        Span<byte> dataDigest = stackalloc byte[IntegrityCodeWriter.DigestLength];
        digest.GetHashAndReset(dataDigest);
        byte[] token = writer.CreateToken(dataDigest);
        if (token.Length * 2 != _macText.Length - 2)
        {
            throw new InvalidOperationException("The PDF MAC token does not fit its placeholder.");
        }

        Encoding.ASCII.GetBytes(Convert.ToHexString(token), section.Slice(macAt + 1, token.Length * 2));
        Dictionary[KnownNames.ByteRange] = new CosArray([new CosInteger(0), new CosInteger(first), new CosInteger(resume), new CosInteger(rest)]);
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
