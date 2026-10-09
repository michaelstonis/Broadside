using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>An ICCBased colour space: colours described by an embedded ICC profile, with an alternate space for processors without one.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.5, Tables 65 to 68: <c>[/ICCBased stream]</c>, the stream holding the profile with <c>N</c> (1, 3 or 4),
/// <c>Alternate</c>, <c>Range</c> and <c>Metadata</c>. Only the profile header is read here (<see cref="ProfileHeader"/>); converting
/// through the profile is the colour-management engine's job (Phase 3). Until then the managed default converts through
/// <see cref="Alternate"/>, or through the Lab equations when the profile's data colour space is Lab.
/// </para>
/// <para>
/// Repairs: when the header and <c>N</c> disagree, a valid header wins; a missing or unusable <c>N</c> is taken from the header, else
/// from the alternate, else 1. An alternate that is missing, of another component count, or not a device, CalGray, CalRGB or Lab
/// space gives way to DeviceGray, DeviceRGB or DeviceCMYK by component count. A <c>Range</c> that
/// is not 2 × N numbers reads as its default.
/// </para>
/// </remarks>
public sealed class PdfIccBasedColorSpace : PdfColorSpace
{
    private HeaderState? _header;

    internal PdfIccBasedColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.IccBased;

    /// <summary>Gets the profile stream.</summary>
    public CosStream Stream => (CosStream)Element(1);

    /// <summary>Gets the stream's <c>N</c> entry, or <see langword="null"/> when it is missing or not an integer.</summary>
    /// <remarks>ISO 32000-2 Table 65 (required): 1, 3 or 4.</remarks>
    public int? DeclaredComponentCount =>
        Cache!.Resolve(Stream.Dictionary.GetValueOrDefault(ColorSpaceNames.N)) is CosInteger n && n.Value is >= int.MinValue and <= int.MaxValue
            ? (int)n.Value
            : null;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 Table 65: N "shall match the number of components actually in the ICC profile".</remarks>
    public override int ComponentCount
    {
        get
        {
            if (ProfileHeader is { ComponentCount: 1 or 3 or 4 } header)
            {
                return header.ComponentCount;
            }

            if (DeclaredComponentCount is int declared and (1 or 3 or 4))
            {
                return declared;
            }

            return DeclaredAlternate is { } alternate && IsAlternateFamily(alternate) ? alternate.ComponentCount : 1;
        }
    }

    /// <summary>Gets the space used in place of the profile: the <c>Alternate</c> entry when usable, else the device space with as many components.</summary>
    /// <remarks>ISO 32000-2 Table 65: absent, DeviceGray, DeviceRGB or DeviceCMYK depending on N.</remarks>
    public PdfColorSpace Alternate
    {
        get
        {
            int count = ComponentCount;
            return DeclaredAlternate is { } alternate && IsAlternateFamily(alternate) && alternate.ComponentCount == count
                ? alternate
                : DeviceSpace(count);
        }
    }

    /// <summary>Gets the ranges of the components, 2 × N numbers; [0 1] each, or the Lab ranges of Table 68 for a Lab profile, when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Tables 65 and 68.</remarks>
    public IReadOnlyList<double> Range => ReadRange(ComponentCount) ?? DefaultRange(ComponentCount);

    /// <summary>Gets the profile stream's <c>Metadata</c> stream, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 65 (PDF 1.4).</remarks>
    public CosStream? Metadata => Cache!.Resolve(Stream.Dictionary.GetValueOrDefault(ColorSpaceNames.Metadata)) as CosStream;

    /// <summary>Gets the profile's header, or <see langword="null"/> when the stream does not decode to an ICC profile.</summary>
    /// <remarks>ICC.1:2022 §7.2. Read once, and again when the stream changes.</remarks>
    public IccProfileHeader? ProfileHeader
    {
        get
        {
            CosStream stream = Stream;
            HeaderState? state = Volatile.Read(ref _header);
            if (state is null || !ReferenceEquals(state.Stream, stream) || state.Version != stream.Version)
            {
                ReadOnlyMemory<byte> data = Cache!.Document.DecodeStream(stream);
                state = new HeaderState(stream, stream.Version, IccProfileHeader.Parse(data.Span), data.Length);
                Volatile.Write(ref _header, state);
            }

            return state.Header;
        }
    }

    /// <summary>Gets a value indicating whether the profile's data colour space is Lab, whose values the alternate cannot take.</summary>
    internal bool IsLabProfile => ProfileHeader is { DataColorSpace: "Lab " } && ComponentCount == 3;

    private PdfColorSpace? DeclaredAlternate =>
        Stream.Dictionary.TryGetValue(ColorSpaceNames.Alternate, out CosObject? value) ? Cache!.Find(value, DiagnosticReference, IsInline, out _) : null;

    /// <summary>Returns the decoded profile.</summary>
    /// <returns>The profile's bytes, decoded through the stream's filters.</returns>
    /// <remarks>ISO 32000-2 §8.6.5.5: the stream's data is the ICC profile.</remarks>
    public ReadOnlyMemory<byte> GetProfileData() => Cache!.Document.DecodeStream(Stream);

    /// <inheritdoc/>
    internal override bool Validate()
    {
        CosDictionary dictionary = Stream.Dictionary;
        int? declared = DeclaredComponentCount;
        IccProfileHeader? header = ProfileHeader;
        if (declared is not (1 or 3 or 4))
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, $"An ICCBased stream's N is missing or not 1, 3 or 4; {ComponentCount} is used.");
        }

        if (header is null)
        {
            Report(DiagnosticCodes.IccProfileInvalid, "An ICCBased stream does not hold an ICC profile (fewer than 128 bytes or no 'acsp' signature); the alternate colour space is used.");
        }
        else
        {
            if (header.ProfileSize > _header!.Length)
            {
                Report(DiagnosticCodes.IccProfileInvalid, "An ICC profile's header states a size larger than the stream's data; the profile is truncated.");
            }

            if (!header.IsSupportedForPdf && header.Version.Major < 5)
            {
                Report(DiagnosticCodes.IccProfileInvalid, $"An ICC profile of class '{header.DeviceClass}' and colour space '{header.DataColorSpace}' is not one Table 67 allows; the alternate colour space is used.");
            }

            if (declared is 1 or 3 or 4 && header.ComponentCount is 1 or 3 or 4 && declared != header.ComponentCount)
            {
                Report(DiagnosticCodes.IccComponentCountMismatch, $"An ICCBased stream's N is {declared} but its profile has {header.ComponentCount} components; the profile's count is used.");
            }
        }

        if (dictionary.ContainsKey(ColorSpaceNames.Alternate) && !ReferenceEquals(Alternate, DeclaredAlternate))
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "An ICCBased Alternate has the wrong number of components or cannot be an alternate; the device colour space with as many components is used.");
        }

        if (dictionary.ContainsKey(ColorSpaceNames.Range) && ReadRange(ComponentCount) is null)
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "An ICCBased Range is not 2 x N numbers with each minimum below its maximum; the default is used.");
        }

        return true;
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        dependencies.Add(new FunctionDependency(Stream, Stream.Version));
        dependencies.Add(new FunctionDependency(Stream.Dictionary, Stream.Dictionary.Version));
    }

    /// <inheritdoc/>
    private protected override ComponentRange GetRangeCore(int index)
    {
        IReadOnlyList<double> range = Range;
        return new ComponentRange(range[2 * index], range[(2 * index) + 1]);
    }

    /// <summary>Returns DeviceGray, DeviceRGB or DeviceCMYK for 1, 3 or 4 components.</summary>
    internal static PdfColorSpace DeviceSpace(int count) => count switch
    {
        3 => PdfDeviceRgbColorSpace.Instance,
        4 => PdfDeviceCmykColorSpace.Instance,
        _ => PdfDeviceGrayColorSpace.Instance,
    };

    /// <summary>
    /// A device space or CalGray, CalRGB or Lab. Another ICCBased space is not taken: an alternate exists for processors that cannot
    /// use profiles, and refusing it rules out alternates that lead back to this space.
    /// </summary>
    private static bool IsAlternateFamily(PdfColorSpace space) => space.Family < PdfColorSpaceFamily.IccBased;

    private double[] DefaultRange(int count)
    {
        if (IsLabProfile)
        {
            return [0, 100, -128, 127, -128, 127];
        }

        double[] range = new double[2 * count];
        for (int i = 0; i < count; i++)
        {
            range[(2 * i) + 1] = 1;
        }

        return range;
    }

    private double[]? ReadRange(int count)
    {
        if (ColorEntries.Numbers(Cache!, Stream.Dictionary, ColorSpaceNames.Range) is not { } range || range.Length != 2 * count)
        {
            return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (range[2 * i] > range[(2 * i) + 1])
            {
                return null;
            }
        }

        return range;
    }

    /// <summary>The header read from one version of the stream.</summary>
    private sealed record HeaderState(CosStream Stream, int Version, IccProfileHeader? Header, int Length);
}
