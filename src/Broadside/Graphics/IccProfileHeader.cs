using System.Buffers.Binary;
using System.Text;

namespace Broadside.Graphics;

/// <summary>The 128-byte header of an ICC colour profile: its version, class, colour spaces and rendering intent.</summary>
/// <remarks>
/// <para>
/// ICC.1:2022 §7.2, Tables 17 to 19, as ISO 32000-2 §8.6.5.5 (Tables 66 and 67) needs it to decide whether a profile can be used
/// in PDF. Signatures are kept as their four characters, trailing spaces included (<c>"RGB "</c>, <c>"Lab "</c>). Only the header
/// is read; the profile's tags are the colour-management engine's business.
/// </para>
/// </remarks>
public sealed class IccProfileHeader
{
    /// <summary>The length of the header, which is also the offset of the tag table.</summary>
    public const int Length = 128;

    private IccProfileHeader()
    {
    }

    /// <summary>Gets the profile size the header states, in bytes.</summary>
    /// <remarks>ICC.1:2022 §7.2.2.</remarks>
    public long ProfileSize { get; private init; }

    /// <summary>Gets the preferred CMM type signature; empty when zero.</summary>
    /// <remarks>ICC.1:2022 §7.2.3.</remarks>
    public string PreferredCmm { get; private init; } = string.Empty;

    /// <summary>Gets the profile version: major, minor and bug-fix numbers (for instance 2.1.0 or 4.4.0).</summary>
    /// <remarks>ICC.1:2022 §7.2.4.</remarks>
    public Version Version { get; private init; } = new(0, 0, 0);

    /// <summary>Gets the profile/device class signature: <c>scnr</c>, <c>mntr</c>, <c>prtr</c>, <c>link</c>, <c>spac</c>, <c>abst</c> or <c>nmcl</c>.</summary>
    /// <remarks>ICC.1:2022 §7.2.5, Table 18.</remarks>
    public string DeviceClass { get; private init; } = string.Empty;

    /// <summary>Gets the data colour space signature, such as <c>"GRAY"</c>, <c>"RGB "</c>, <c>"CMYK"</c> or <c>"Lab "</c>.</summary>
    /// <remarks>ICC.1:2022 §7.2.6, Table 19.</remarks>
    public string DataColorSpace { get; private init; } = string.Empty;

    /// <summary>Gets the profile connection space signature: <c>"XYZ "</c> or <c>"Lab "</c>.</summary>
    /// <remarks>ICC.1:2022 §7.2.7.</remarks>
    public string ConnectionSpace { get; private init; } = string.Empty;

    /// <summary>Gets the number of components of the data colour space; 0 when the signature is not one Table 19 lists.</summary>
    /// <remarks>ICC.1:2022 Table 19.</remarks>
    public int ComponentCount { get; private init; }

    /// <summary>Gets the rendering intent the header records (which PDF ignores, §8.6.5.5).</summary>
    /// <remarks>ICC.1:2022 §7.2.15.</remarks>
    public RenderingIntent RenderingIntent { get; private init; }

    /// <summary>Gets the illuminant of the profile connection space, D50 in conforming profiles.</summary>
    /// <remarks>ICC.1:2022 §7.2.16.</remarks>
    public CieXyz Illuminant { get; private init; }

    /// <summary>
    /// Gets a value indicating whether PDF allows the profile in an ICCBased colour space: a scanner, monitor, printer or colour space
    /// class profile of a gray, RGB, CMYK or Lab data colour space, version 2 to 4.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.5.5, Table 67 (classes and spaces) and Table 66 (versions: a reader shall support ICC.1:2010, version 4, and
    /// shall use the alternate for later versions it cannot process).
    /// </remarks>
    public bool IsSupportedForPdf =>
        DeviceClass is "scnr" or "mntr" or "prtr" or "spac"
        && DataColorSpace is "GRAY" or "RGB " or "CMYK" or "Lab "
        && Version.Major is >= 2 and <= 4;

    /// <summary>Reads the header at the start of a profile.</summary>
    /// <param name="profile">The profile's bytes (at least the first 128).</param>
    /// <returns>The header; <see langword="null"/> when there are fewer than 128 bytes or the <c>acsp</c> signature is missing.</returns>
    /// <remarks>ICC.1:2022 §7.2.9: bytes 36 to 39 are the profile file signature <c>acsp</c>.</remarks>
    public static IccProfileHeader? Parse(ReadOnlySpan<byte> profile)
    {
        if (profile.Length < Length || !profile.Slice(36, 4).SequenceEqual("acsp"u8))
        {
            return null;
        }

        string space = Signature(profile.Slice(16, 4));
        uint intent = BinaryPrimitives.ReadUInt32BigEndian(profile.Slice(64, 4));
        return new IccProfileHeader
        {
            ProfileSize = BinaryPrimitives.ReadUInt32BigEndian(profile),
            PreferredCmm = Signature(profile.Slice(4, 4)),
            Version = new Version(profile[8], profile[9] >> 4, profile[9] & 0xF),
            DeviceClass = Signature(profile.Slice(12, 4)),
            DataColorSpace = space,
            ConnectionSpace = Signature(profile.Slice(20, 4)),
            ComponentCount = ComponentsOf(space),
            RenderingIntent = intent switch
            {
                0 => RenderingIntent.Perceptual,
                2 => RenderingIntent.Saturation,
                3 => RenderingIntent.AbsoluteColorimetric,
                _ => RenderingIntent.RelativeColorimetric,
            },
            Illuminant = new CieXyz(Fixed(profile[68..]), Fixed(profile[72..]), Fixed(profile[76..])),
        };
    }

    /// <summary>The number of components of a data colour space signature (ICC.1:2022 Table 19); 0 when unknown.</summary>
    private static int ComponentsOf(string signature) => signature switch
    {
        "GRAY" => 1,
        "XYZ " or "Lab " or "Luv " or "YCbr" or "Yxy " or "RGB " or "HSV " or "HLS " or "CMY " => 3,
        "CMYK" => 4,
        [char digit, 'C', 'L', 'R'] when digit is >= '2' and <= '9' => digit - '0',
        [char letter, 'C', 'L', 'R'] when letter is >= 'A' and <= 'F' => letter - 'A' + 10,
        _ => 0,
    };

    private static string Signature(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes) == 0 ? string.Empty : Encoding.Latin1.GetString(bytes);

    /// <summary>An s15Fixed16Number (ICC.1:2022 §4.6).</summary>
    private static double Fixed(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32BigEndian(bytes) / 65536.0;
}
