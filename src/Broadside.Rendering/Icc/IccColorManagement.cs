using System.Collections.Concurrent;
using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>
/// The managed ICC engine behind the colour-management extension point: ICCBased gray and RGB colours convert through their
/// profiles' matrix/TRC and monochrome models; every other source goes to the wrapped colour management.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.5 and §10.3, ICC.1:2022 Annex F.2 and F.3 (see <see cref="IccProfile"/>). Register it with
/// <see cref="IccPdfOptionsExtensions.UseIccColorManagement"/> or <see cref="PdfOptions.UseColorManagement"/>. Colours convert
/// media-relative (ICC-absolute for <see cref="RenderingIntent.AbsoluteColorimetric"/>) to sRGB; gray and CMYK targets then go
/// through the wrapped management's DeviceRGB conversion.
/// </para>
/// <para>
/// Device spaces, CalGray, CalRGB, Lab, and ICCBased spaces whose profile is CMYK, Lab, lookup-table based, of a version after 4, or
/// unreadable go to the wrapped management, which converts them through their alternate (or, for a Lab profile, as Lab). A profile
/// whose header is fine but whose tags cannot be read is recorded on the document as <c>IccProfileInvalid</c> through
/// <see cref="PdfIccBasedColorSpace.ReportProfileInvalid"/>; a bad header is already recorded when the space is read.
/// </para>
/// <para>
/// Profiles are read once and their transforms built once per rendering intent, keyed by the profile's bytes, so the same profile
/// embedded in many spaces or documents shares them; the cache keeps at most <see cref="CacheCapacity"/> profiles. Thread-safe.
/// </para>
/// </remarks>
public sealed class IccColorManagement : IColorManagement
{
    /// <summary>The most profiles kept; when more arrive the cache starts afresh.</summary>
    public const int CacheCapacity = 64;

    private readonly ConcurrentDictionary<ProfileKey, ProfileEntry> _profiles = new();

    /// <summary>Initializes a new instance of the <see cref="IccColorManagement"/> class.</summary>
    /// <param name="fallback">The colour management for everything the engine does not convert; <see cref="ManagedColorManagement.Default"/> when <see langword="null"/>.</param>
    public IccColorManagement(IColorManagement? fallback = null) => Fallback = fallback ?? ManagedColorManagement.Default;

    /// <summary>Gets the colour management for every source the engine does not convert itself.</summary>
    public IColorManagement Fallback { get; }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §8.6.5.5 and §10.3.</remarks>
    public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not PdfIccBasedColorSpace icc
            || icc.ProfileHeader is not { IsSupportedForPdf: true, DataColorSpace: "GRAY" or "RGB " } header
            || header.ComponentCount != icc.ComponentCount)
        {
            return Fallback.CreateConverter(source, conversion);
        }

        ProfileEntry entry = Find(icc.GetProfileData().Span);
        if (entry.Profile is not { Model: IccProfileModel.MatrixTrc or IccProfileModel.Monochrome })
        {
            if (entry.Problem is { } problem)
            {
                icc.ReportProfileInvalid($"An ICC profile cannot be used: {problem}; the alternate colour space is used.");
            }

            return Fallback.CreateConverter(source, conversion);
        }

        IColorConverter? fromRgb = conversion.Target == DeviceColorModel.Rgb ? null : Fallback.CreateConverter(PdfDeviceRgbColorSpace.Instance, conversion);
        return new IccConverter(entry.Transform(conversion.Intent), fromRgb);
    }

    private ProfileEntry Find(ReadOnlySpan<byte> data)
    {
        var key = new ProfileKey(data.ToArray());
        if (_profiles.TryGetValue(key, out ProfileEntry? entry))
        {
            return entry;
        }

        if (_profiles.Count >= CacheCapacity)
        {
            _profiles.Clear();
        }

        entry = IccProfile.TryParse(data, out IccProfile? profile, out string? problem) ? new ProfileEntry(profile, null) : new ProfileEntry(null, problem);
        return _profiles.GetOrAdd(key, entry);
    }

    /// <summary>A profile's bytes, compared by content.</summary>
    private sealed class ProfileKey : IEquatable<ProfileKey>
    {
        private readonly byte[] _data;
        private readonly int _hash;

        public ProfileKey(byte[] data)
        {
            _data = data;
            var hash = default(HashCode);
            hash.AddBytes(data);
            _hash = hash.ToHashCode();
        }

        public bool Equals(ProfileKey? other) => other is not null && _hash == other._hash && _data.AsSpan().SequenceEqual(other._data);

        public override bool Equals(object? obj) => Equals(obj as ProfileKey);

        public override int GetHashCode() => _hash;
    }

    /// <summary>A read profile, or why it cannot be read, and its transforms by rendering intent, built on first use.</summary>
    private sealed class ProfileEntry(IccProfile? profile, string? problem)
    {
        private readonly IccTransform?[] _transforms = new IccTransform?[4];

        public IccProfile? Profile => profile;

        public string? Problem => problem;

        public IccTransform Transform(RenderingIntent intent)
        {
            int index = (int)intent & 3;
            IccTransform? transform = Volatile.Read(ref _transforms[index]);
            if (transform is null)
            {
                transform = IccTransform.Create(profile!, intent);
                Volatile.Write(ref _transforms[index], transform);
            }

            return transform;
        }
    }
}
