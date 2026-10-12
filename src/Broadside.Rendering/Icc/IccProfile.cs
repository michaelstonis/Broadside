using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>An ICC colour profile read for conversion: its header, its computational model and the tags that model uses.</summary>
/// <remarks>
/// <para>
/// ICC.1:2022 §7.2 (header, read by <see cref="IccProfileHeader"/>), §7.3 (tag table), §9.2 (the tags), §10.6, §10.18 and §10.31
/// (curveType, parametricCurveType and XYZType), §10.10 to §10.13 (lut16Type, lut8Type, lutAToBType and lutBToAType), §10.22
/// (s15Fixed16ArrayType, the <c>chad</c> tag) and Annex F.2 and F.3 (the monochrome and three-component matrix-based models).
/// Version 2 and version 4 profiles read alike.
/// </para>
/// <para>
/// The tags for a rendering intent are chosen by §8.10.2: the AToB (or BToA) tag of the intent (Table 25: <c>AToB0</c> perceptual,
/// <c>AToB1</c> media-relative and ICC-absolute colorimetric, <c>AToB2</c> saturation), else <c>AToB0</c>, else the matrix/TRC or gray
/// TRC tags. The DToB and BToD tags of §8.10.2 a) (multiProcessElementsType) are not supported, so b) to d) apply.
/// </para>
/// <para>
/// Reading is lenient where the deviation does not matter to the conversion: tag table entries that point outside the data are
/// treated as missing tags, and a missing or unusable <c>wtpt</c> reads as the header's PCS illuminant. An AToB tag that cannot be
/// used (malformed, or with channel counts that do not match the header) is passed over for the next tag of the precedence order;
/// when that leaves a gray, RGB or CMYK profile with nothing to convert by, the profile is refused. An unusable BToA tag is treated
/// as missing.
/// </para>
/// </remarks>
public sealed class IccProfile
{
    private const int TagTableOffset = IccProfileHeader.Length;

    private readonly IccLut?[] _aToB;
    private readonly IccLut?[] _bToA;
    private readonly double[]? _chromaticAdaptation;

    private IccProfile(IccProfileHeader header, IccProfileModel model, CieXyz mediaWhitePoint, Tags tags)
    {
        Header = header;
        Model = model;
        MediaWhitePoint = mediaWhitePoint;
        Curves = tags.Curves;
        Columns = tags.Columns;
        _aToB = tags.AToB;
        _bToA = tags.BToA;
        _chromaticAdaptation = tags.ChromaticAdaptation;
        Warning = tags.LutProblem;
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

    /// <summary>
    /// Gets the chromatic adaptation matrix (<c>chad</c>), nine values row by row, or <see langword="null"/> when the profile has no
    /// usable one.
    /// </summary>
    /// <remarks>
    /// ICC.1:2022 §9.2.15 and §10.22 (s15Fixed16ArrayType). It records how the profile's colorimetry was adapted to the PCS white;
    /// the tags already hold adapted values, so conversion does not apply it.
    /// </remarks>
    public IReadOnlyList<double>? ChromaticAdaptation => _chromaticAdaptation;

    /// <summary>Gets the tone reproduction curves: red, green and blue, or the one gray curve; empty when the profile has none.</summary>
    internal IccCurve[] Curves { get; }

    /// <summary>Gets the PCSXYZ matrix columns red, green and blue; empty unless the profile has matrix/TRC tags.</summary>
    internal CieXyz[] Columns { get; }

    /// <summary>Gets why an AToB tag the profile has cannot be used, when the profile still converts by other tags.</summary>
    internal string? Warning { get; }

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

        var table = new TagTable(data);
        CieXyz white = table.TryRead("wtpt"u8, out ReadOnlySpan<byte> wtpt) && TryReadXyz(wtpt, out CieXyz point) ? point : header.Illuminant;
        var tags = new Tags { ChromaticAdaptation = table.TryRead("chad"u8, out ReadOnlySpan<byte> chad) ? ReadArray(chad, 9) : null };
        ReadLookupTables(table, header, tags);
        bool hasLut = Array.Exists(tags.AToB, static lut => lut is not null);
        string? matrixProblem = null;
        IccProfileModel? model = header.DataColorSpace switch
        {
            "RGB " when TryReadMatrix(table, out IccCurve[]? curves, out CieXyz[]? columns, out matrixProblem) => Matrix(tags, curves, columns, IccProfileModel.MatrixTrc),
            "GRAY" when TryReadCurve(table, "kTRC"u8, out IccCurve? gray, out matrixProblem) => Matrix(tags, [gray], [], IccProfileModel.Monochrome),
            _ => null,
        };

        if (hasLut)
        {
            model = IccProfileModel.LookupTable;
        }
        else if (model is null && header.DataColorSpace is "GRAY" or "RGB " or "CMYK")
        {
            problem = tags.LutProblem ?? matrixProblem ?? "its A2B0 tag is missing";
            return false;
        }

        profile = new IccProfile(header, model ?? IccProfileModel.Other, white, tags);
        problem = null;
        return true;

        static IccProfileModel Matrix(Tags tags, IccCurve[] curves, CieXyz[] columns, IccProfileModel model)
        {
            tags.Curves = curves;
            tags.Columns = columns;
            return model;
        }
    }

    /// <summary>Converts one device colour to the profile connection space.</summary>
    /// <param name="device">The colour's <see cref="IccProfileHeader.ComponentCount"/> components, each 0 to 1 (clipped).</param>
    /// <param name="connection">
    /// Receives three values in the profile's connection space (<see cref="IccProfileHeader.ConnectionSpace"/>): X, Y and Z with the
    /// PCS white's Y = 1, or L* (0 to 100), a* and b*.
    /// </param>
    /// <param name="intent">The rendering intent, which chooses the tag (ICC.1:2022 §8.10.2, Table 25).</param>
    /// <returns>Whether the profile has tags to convert by: <see langword="false"/> for <see cref="IccProfileModel.Other"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="device"/> or <paramref name="connection"/> is too short.</exception>
    /// <remarks>
    /// ICC.1:2022 §10.10 to §10.12 and Annex F.2, F.3: media-relative PCS values (§6.3.2.1), or ICC-absolute ones for
    /// <see cref="RenderingIntent.AbsoluteColorimetric"/> (§6.3.2.2, Equation 4: scaled by the media white point over the PCS white).
    /// </remarks>
    public bool TryConvertToConnectionSpace(ReadOnlySpan<float> device, Span<float> connection, RenderingIntent intent = RenderingIntent.RelativeColorimetric)
    {
        int components = Math.Max(Header.ComponentCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(device.Length, components, nameof(device));
        ArgumentOutOfRangeException.ThrowIfLessThan(connection.Length, 3, nameof(connection));
        bool lab = Header.ConnectionSpace == "Lab ";
        if (SelectAToB(intent) is { } lut)
        {
            Span<float> scratch = stackalloc float[2 * IccClut.MaxChannels];
            Span<float> normalized = stackalloc float[3];
            lut.Evaluate(device, normalized, scratch);
            IccPcs.Decode(lut.Kind, lab, normalized, connection);
        }
        else if (Columns.Length == 3)
        {
            float r = (float)Curves[0].Evaluate(device[0]);
            float g = (float)Curves[1].Evaluate(device[1]);
            float b = (float)Curves[2].Evaluate(device[2]);
            for (int row = 0; row < 3; row++)
            {
                connection[row] = (float)((Get(Columns[0], row) * r) + (Get(Columns[1], row) * g) + (Get(Columns[2], row) * b));
            }

            if (lab)
            {
                IccPcs.XyzToLab(connection[0], connection[1], connection[2], out connection[0], out connection[1], out connection[2]);
            }
        }
        else if (Curves.Length == 1)
        {
            // Annex F.2: the curve scales the PCS white, which is L* = 100 for a PCSLAB profile.
            float y = (float)Curves[0].Evaluate(device[0]);
            if (lab)
            {
                connection[0] = 100 * y;
                connection[1] = 0;
                connection[2] = 0;
            }
            else
            {
                connection[0] = (float)(CieXyz.D50.X * y);
                connection[1] = y;
                connection[2] = (float)(CieXyz.D50.Z * y);
            }
        }
        else
        {
            return false;
        }

        if (intent == RenderingIntent.AbsoluteColorimetric)
        {
            ScaleToAbsolute(connection, lab, inverse: false);
        }

        return true;
    }

    /// <summary>Converts one colour in the profile connection space to the profile's device colour, through its BToA tags.</summary>
    /// <param name="connection">Three PCS values, as <see cref="TryConvertToConnectionSpace"/> gives them.</param>
    /// <param name="device">Receives <see cref="IccProfileHeader.ComponentCount"/> components, each 0 to 1.</param>
    /// <param name="intent">The rendering intent, which chooses the tag (ICC.1:2022 §8.10.2, Table 25).</param>
    /// <returns>Whether the profile has a usable BToA tag; matrix/TRC profiles are not inverted (Annex F.1).</returns>
    /// <exception cref="ArgumentException"><paramref name="connection"/> or <paramref name="device"/> is too short.</exception>
    /// <remarks>ICC.1:2022 §10.10, §10.11 and §10.13; ICC-absolute PCS values for <see cref="RenderingIntent.AbsoluteColorimetric"/> (§6.3.2.2).</remarks>
    public bool TryConvertFromConnectionSpace(ReadOnlySpan<float> connection, Span<float> device, RenderingIntent intent = RenderingIntent.RelativeColorimetric)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connection.Length, 3, nameof(connection));
        if (SelectBToA(intent) is not { } lut)
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(device.Length, lut.OutputCount, nameof(device));
        bool lab = Header.ConnectionSpace == "Lab ";
        Span<float> pcs = stackalloc float[3];
        connection[..3].CopyTo(pcs);
        if (intent == RenderingIntent.AbsoluteColorimetric)
        {
            ScaleToAbsolute(pcs, lab, inverse: true);
        }

        Span<float> normalized = stackalloc float[3];
        Span<float> scratch = stackalloc float[2 * IccClut.MaxChannels];
        IccPcs.Encode(lut.Kind, lab, pcs, normalized);
        lut.Evaluate(normalized, device, scratch);
        return true;
    }

    /// <summary>The AToB tag for an intent by §8.10.2 b) and c), or <see langword="null"/> when d) applies (the matrix/TRC tags).</summary>
    internal IccLut? SelectAToB(RenderingIntent intent) => Select(_aToB, intent, hasMatrix: Curves.Length > 0);

    /// <summary>The BToA tag for an intent by §8.10.2 b) and c).</summary>
    internal IccLut? SelectBToA(RenderingIntent intent) => Select(_bToA, intent, hasMatrix: false);

    /// <summary>
    /// The intent's tag, else tag 0; else, when there is nothing to fall back on, any usable tag (lenient: §8.10.2 requires tag 0
    /// before 1 and 2).
    /// </summary>
    private static IccLut? Select(IccLut?[] luts, RenderingIntent intent, bool hasMatrix)
    {
        int index = intent switch
        {
            RenderingIntent.Perceptual => 0,
            RenderingIntent.Saturation => 2,
            _ => 1,
        };
        return luts[index] ?? luts[0] ?? (hasMatrix ? null : luts[1] ?? luts[2]);
    }

    private static double Get(CieXyz xyz, int row) => row switch
    {
        0 => xyz.X,
        1 => xyz.Y,
        _ => xyz.Z,
    };

    /// <summary>Equation 4 of §6.3.2.2: media-relative PCSXYZ times the media white over the PCS white (or its inverse).</summary>
    private void ScaleToAbsolute(Span<float> pcs, bool lab, bool inverse)
    {
        if (lab)
        {
            IccPcs.LabToXyz(pcs[0], pcs[1], pcs[2], out pcs[0], out pcs[1], out pcs[2]);
        }

        CieXyz white = MediaWhitePoint;
        CieXyz d50 = CieXyz.D50;
        double[] scale = [white.X / d50.X, white.Y / d50.Y, white.Z / d50.Z];
        for (int i = 0; i < 3; i++)
        {
            pcs[i] = (float)(inverse ? pcs[i] / scale[i] : pcs[i] * scale[i]);
        }

        if (lab)
        {
            IccPcs.XyzToLab(pcs[0], pcs[1], pcs[2], out pcs[0], out pcs[1], out pcs[2]);
        }
    }

    /// <summary>Reads the AToB0 to AToB2 and BToA0 to BToA2 tags whose channel counts fit the header (§9.2.1 to §9.2.3, §9.2.6 to §9.2.8).</summary>
    private static void ReadLookupTables(TagTable table, IccProfileHeader header, Tags tags)
    {
        bool pcsXyz = header.ConnectionSpace == "XYZ ";
        if (!pcsXyz && header.ConnectionSpace != "Lab ")
        {
            return;
        }

        int components = header.ComponentCount;
        ReadOnlySpan<byte> aToB = "A2B0A2B1A2B2"u8;
        ReadOnlySpan<byte> bToA = "B2A0B2A1B2A2"u8;
        for (int i = 0; i < 3; i++)
        {
            ReadOnlySpan<byte> signature = aToB.Slice(4 * i, 4);
            if (table.TryRead(signature, out ReadOnlySpan<byte> tag))
            {
                if (!IccLut.TryRead(tag, pcsXyzInput: false, out IccLut? lut, out string? reason))
                {
                    tags.LutProblem ??= $"its {Name(signature)} tag is unusable: {reason}";
                }
                else if (lut.InputCount != components || lut.OutputCount != 3)
                {
                    tags.LutProblem ??= $"its {Name(signature)} tag takes {lut.InputCount} channels to {lut.OutputCount}, not {components} to 3";
                }
                else
                {
                    tags.AToB[i] = lut;
                }
            }

            if (table.TryRead(bToA.Slice(4 * i, 4), out tag)
                && IccLut.TryRead(tag, pcsXyz, out IccLut? inverse, out _)
                && inverse.InputCount == 3
                && inverse.OutputCount == components)
            {
                tags.BToA[i] = inverse;
            }
        }
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

    /// <summary>
    /// The first <paramref name="count"/> values of an s15Fixed16ArrayType tag (ICC.1:2022 §10.22, Table 76: 'sf32', 4 reserved bytes,
    /// then the values; their number follows from the tag's size), or <see langword="null"/> when it is not one or is shorter.
    /// </summary>
    private static double[]? ReadArray(ReadOnlySpan<byte> tag, int count)
    {
        if (!tag.StartsWith("sf32"u8) || tag.Length < 8 + (4 * count))
        {
            return null;
        }

        double[] values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = Fixed(tag[(8 + (4 * i))..]);
        }

        return values;
    }

    /// <summary>An s15Fixed16Number (ICC.1:2022 §4.6).</summary>
    private static double Fixed(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32BigEndian(bytes) / 65536.0;

    private static string Name(ReadOnlySpan<byte> signature) => System.Text.Encoding.Latin1.GetString(signature);

    /// <summary>The tags read for a profile.</summary>
    private sealed class Tags
    {
        public IccLut?[] AToB { get; } = new IccLut?[3];

        public IccLut?[] BToA { get; } = new IccLut?[3];

        public IccCurve[] Curves { get; set; } = [];

        public CieXyz[] Columns { get; set; } = [];

        public double[]? ChromaticAdaptation { get; init; }

        public string? LutProblem { get; set; }
    }

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
