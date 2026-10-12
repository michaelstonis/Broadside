using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>An ICC colour profile read for conversion: its header, its computational model and the tags that model uses.</summary>
/// <remarks>
/// <para>
/// ICC.1:2022 §7.2 (header, read by <see cref="IccProfileHeader"/>), §7.3 (tag table), §9.2 (the tags), §10.6, §10.18 and §10.31
/// (curveType, parametricCurveType and XYZType) and Annex F.2 and F.3 (the monochrome and three-component matrix-based models), as
/// ISO 32000-2 §8.6.5.5 uses an ICCBased colour space's profile: only the device-to-PCS direction is read. Version 2 and version 4
/// profiles read alike.
/// </para>
/// <para>
/// Reading is lenient where the deviation does not matter to the conversion: tag table entries that point outside the data are
/// treated as missing tags, and a missing or unusable <c>wtpt</c> reads as the header's PCS illuminant. A gray or RGB profile whose
/// TRC or matrix column tags are missing, of another type or truncated is refused, unless it has an <c>AToB0</c> tag to fall back
/// on, which makes it <see cref="IccProfileModel.Other"/>. Where a profile has both an <c>AToB</c> tag and matrix/TRC tags, the
/// matrix/TRC tags are used for now, although §8.10.2 gives the lookup tables precedence.
/// </para>
/// </remarks>
public sealed class IccProfile
{
    private const int TagTableOffset = IccProfileHeader.Length;

    private IccProfile(IccProfileHeader header, IccProfileModel model, CieXyz mediaWhitePoint, IccCurve[] curves, CieXyz[] columns)
    {
        Header = header;
        Model = model;
        MediaWhitePoint = mediaWhitePoint;
        Curves = curves;
        Columns = columns;
    }

    /// <summary>Gets the profile header.</summary>
    /// <remarks>ICC.1:2022 §7.2.</remarks>
    public IccProfileHeader Header { get; }

    /// <summary>Gets the computational model the profile's tags describe.</summary>
    /// <remarks>ICC.1:2022 §8.10 and Annex F.</remarks>
    public IccProfileModel Model { get; }

    /// <summary>Gets the media white point (<c>wtpt</c>), or the header's PCS illuminant when the tag is missing or unusable.</summary>
    /// <remarks>ICC.1:2022 §9.2.36; used by the ICC-absolute colorimetric intent (§6.3.2.2).</remarks>
    public CieXyz MediaWhitePoint { get; }

    /// <summary>Gets the tone reproduction curves: red, green and blue, or the one gray curve; empty for <see cref="IccProfileModel.Other"/>.</summary>
    internal IccCurve[] Curves { get; }

    /// <summary>Gets the PCSXYZ matrix columns red, green and blue; empty unless <see cref="IccProfileModel.MatrixTrc"/>.</summary>
    internal CieXyz[] Columns { get; }

    /// <summary>Reads a profile.</summary>
    /// <param name="data">The profile's bytes.</param>
    /// <param name="profile">The profile, when it can be read.</param>
    /// <param name="problem">Why it cannot, when it cannot: a phrase such as "its rTRC tag is missing".</param>
    /// <returns>Whether the profile can be read. Never throws, whatever the bytes.</returns>
    /// <remarks>ICC.1:2022 §7 to §10 and Annex F.</remarks>
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out IccProfile? profile, [NotNullWhen(false)] out string? problem)
    {
        profile = null;
        if (IccProfileHeader.Parse(data) is not { } header)
        {
            problem = "it has no ICC header (fewer than 128 bytes or no 'acsp' signature)";
            return false;
        }

        if (data.Length < TagTableOffset + 4)
        {
            problem = "it has no tag table";
            return false;
        }

        var tags = new TagTable(data);
        CieXyz white = tags.TryRead("wtpt"u8, out ReadOnlySpan<byte> wtpt) && TryReadXyz(wtpt, out CieXyz point) ? point : header.Illuminant;
        bool hasLut = tags.TryRead("A2B0"u8, out _);
        switch (header.DataColorSpace)
        {
            case "RGB ":
                if (TryReadMatrix(tags, out IccCurve[]? curves, out CieXyz[]? columns, out problem))
                {
                    profile = new IccProfile(header, IccProfileModel.MatrixTrc, white, curves, columns);
                    return true;
                }

                if (!hasLut)
                {
                    return false;
                }

                break;
            case "GRAY":
                if (TryReadCurve(tags, "kTRC"u8, out IccCurve? gray, out problem))
                {
                    profile = new IccProfile(header, IccProfileModel.Monochrome, white, [gray], []);
                    return true;
                }

                if (!hasLut)
                {
                    return false;
                }

                break;
        }

        profile = new IccProfile(header, IccProfileModel.Other, white, [], []);
        problem = null;
        return true;
    }

    /// <summary>Reads the three TRC curves and matrix columns of a three-component matrix-based profile (ICC.1:2022 §8.3.3, Annex F.3).</summary>
    private static bool TryReadMatrix(
        TagTable tags,
        [NotNullWhen(true)] out IccCurve[]? curves,
        [NotNullWhen(true)] out CieXyz[]? columns,
        [NotNullWhen(false)] out string? problem)
    {
        ReadOnlySpan<byte> curveTags = "rTRCgTRCbTRC"u8;
        ReadOnlySpan<byte> columnTags = "rXYZgXYZbXYZ"u8;
        curves = new IccCurve[3];
        columns = new CieXyz[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryReadCurve(tags, curveTags.Slice(4 * i, 4), out IccCurve? curve, out problem)
                || !TryReadColumn(tags, columnTags.Slice(4 * i, 4), out columns[i], out problem))
            {
                curves = null;
                columns = null;
                return false;
            }

            curves[i] = curve;
        }

        problem = null;
        return true;
    }

    private static bool TryReadCurve(TagTable tags, ReadOnlySpan<byte> signature, [NotNullWhen(true)] out IccCurve? curve, [NotNullWhen(false)] out string? problem)
    {
        curve = null;
        if (!tags.TryRead(signature, out ReadOnlySpan<byte> tag))
        {
            problem = $"its {Name(signature)} tag is missing";
            return false;
        }

        if (!IccCurve.TryRead(tag, out curve, out string? reason))
        {
            problem = $"its {Name(signature)} tag is unusable: {reason}";
            return false;
        }

        problem = null;
        return true;
    }

    private static bool TryReadColumn(TagTable tags, ReadOnlySpan<byte> signature, out CieXyz column, [NotNullWhen(false)] out string? problem)
    {
        column = default;
        if (!tags.TryRead(signature, out ReadOnlySpan<byte> tag))
        {
            problem = $"its {Name(signature)} tag is missing";
            return false;
        }

        if (!TryReadXyz(tag, out column))
        {
            problem = $"its {Name(signature)} tag is not an XYZType";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>The first value of an XYZType tag (ICC.1:2022 §10.31: s15Fixed16Number X, Y, Z after the 8-byte type header).</summary>
    private static bool TryReadXyz(ReadOnlySpan<byte> tag, out CieXyz xyz)
    {
        xyz = default;
        if (tag.Length < 20 || !tag.StartsWith("XYZ "u8))
        {
            return false;
        }

        xyz = new CieXyz(Fixed(tag[8..]), Fixed(tag[12..]), Fixed(tag[16..]));
        return true;
    }

    /// <summary>An s15Fixed16Number (ICC.1:2022 §4.6).</summary>
    private static double Fixed(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32BigEndian(bytes) / 65536.0;

    private static string Name(ReadOnlySpan<byte> signature) => System.Text.Encoding.Latin1.GetString(signature);

    /// <summary>The tag table (ICC.1:2022 §7.3): signature, offset and size of each tag; entries that point outside the data are skipped.</summary>
    private readonly ref struct TagTable
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly int _count;

        public TagTable(ReadOnlySpan<byte> data)
        {
            _data = data;
            uint declared = BinaryPrimitives.ReadUInt32BigEndian(data[TagTableOffset..]);
            _count = (int)Math.Min(declared, (uint)(data.Length - TagTableOffset - 4) / 12);
        }

        public bool TryRead(ReadOnlySpan<byte> signature, out ReadOnlySpan<byte> tag)
        {
            for (int i = 0; i < _count; i++)
            {
                ReadOnlySpan<byte> entry = _data.Slice(TagTableOffset + 4 + (12 * i), 12);
                if (!entry[..4].SequenceEqual(signature))
                {
                    continue;
                }

                uint offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
                uint size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                if (offset <= (uint)_data.Length && size <= (uint)_data.Length - offset)
                {
                    tag = _data.Slice((int)offset, (int)size);
                    return true;
                }
            }

            tag = default;
            return false;
        }
    }
}
