using System.Collections.Concurrent;
using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>
/// The managed ICC engine behind the colour-management extension point: ICCBased gray, RGB and CMYK colours convert through their
/// profiles' lookup tables, matrix/TRC or monochrome models to an RGB profile (Broadside's own sRGB unless another is chosen), with
/// the rendering intent's tags and black point compensation; device colours convert through the output intent's profile when the
/// content has one; every other source goes to the wrapped colour management.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.5 and §10.3, ICC.1:2022 §8.10.2, §10.10 to §10.13 and Annex F (see <see cref="IccProfile"/>). Register it with
/// <see cref="IccPdfOptionsExtensions.UseIccColorManagement"/> or <see cref="PdfOptions.UseColorManagement"/>.
/// </para>
/// <para>
/// Rendering intents (§8.6.5.8): the intent of the conversion chooses the source's AToB tag and the destination's BToA tag
/// (ICC.1:2022 Table 25); the absolute colorimetric intent keeps each medium's white (see <see cref="IccProfile"/>). Black point
/// compensation (§8.6.5.9, PDF 2.0 Application Note 001, ISO 18619): applied when <c>UseBlackPtComp</c> is ON, never when it is OFF
/// or the intent is absolute colorimetric, and when it is Default as <see cref="CompensatesBlackPointByDefault"/> says.
/// </para>
/// <para>
/// Output intents (§14.11.5): when <see cref="ColorConversion.OutputIntent"/> has a usable profile and
/// <see cref="UsesOutputIntents"/> is set, DeviceCMYK colours (and DeviceRGB or DeviceGray colours when the profile is RGB or gray)
/// convert as if described by that profile, after default colour spaces (§8.6.5.6) had their chance; and when the target is the
/// profile's own device (a CMYK target and a CMYK output intent), device colours of that kind pass through unchanged and ICCBased
/// colours convert to the output intent's profile through its BToA tags. Without an output intent, device colours convert as the
/// wrapped colour management converts them.
/// </para>
/// <para>
/// Device spaces, CalGray, CalRGB, Lab, and ICCBased spaces whose profile is a Lab colour space profile, of a version after 4, or
/// unreadable go to the wrapped management, which converts them through their alternate (or, for a Lab profile, as Lab). A profile
/// whose header is fine but whose tags cannot be read is recorded on the document as <c>IccProfileInvalid</c> through
/// <see cref="PdfIccBasedColorSpace.ReportProfileInvalid"/>, as is a lookup table passed over for other tags of the profile; a bad
/// header is already recorded when the space is read. Gray and CMYK targets other than an output intent's device convert the RGB
/// result through the wrapped management's DeviceRGB conversion.
/// </para>
/// <para>
/// Profiles are read once and their transforms built once per destination, rendering intent and black point compensation, keyed by
/// the profile's bytes, so the same profile embedded in many spaces or documents shares them; the cache keeps at most
/// <see cref="CacheCapacity"/> profiles. Immutable and thread-safe; the <c>With</c> methods return new instances.
/// </para>
/// </remarks>
public sealed class IccColorManagement : IColorManagement
{
    /// <summary>The most profiles kept; when more arrive the cache starts afresh.</summary>
    public const int CacheCapacity = 64;

    private readonly ConcurrentDictionary<ProfileKey, ProfileEntry> _profiles = new();

    /// <summary>Initializes a new instance of the <see cref="IccColorManagement"/> class.</summary>
    /// <param name="fallback">The colour management for everything the engine does not convert; <see cref="ManagedColorManagement.Default"/> when <see langword="null"/>.</param>
    public IccColorManagement(IColorManagement? fallback = null)
        : this(fallback ?? ManagedColorManagement.Default, IccProfile.Srgb, compensatesBlackPoint: true, usesOutputIntents: true)
    {
    }

    private IccColorManagement(IColorManagement fallback, IccProfile rgbProfile, bool compensatesBlackPoint, bool usesOutputIntents)
    {
        Fallback = fallback;
        RgbProfile = rgbProfile;
        CompensatesBlackPointByDefault = compensatesBlackPoint;
        UsesOutputIntents = usesOutputIntents;
    }

    /// <summary>Gets the colour management for every source the engine does not convert itself.</summary>
    public IColorManagement Fallback { get; }

    /// <summary>Gets the profile of the RGB colours the engine converts to; <see cref="IccProfile.Srgb"/> unless another is chosen.</summary>
    /// <remarks>ISO 32000-2 §10.3: the CIE-based to device step describes the device by a profile.</remarks>
    public IccProfile RgbProfile { get; }

    /// <summary>
    /// Gets a value indicating whether black point compensation applies when the graphics state leaves it to the processor
    /// (<c>UseBlackPtComp</c> Default, or absent). <see langword="true"/> unless changed.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.6.5.9: "If the value is not given or set to Default, then the behaviour is left to the PDF processor".</remarks>
    public bool CompensatesBlackPointByDefault { get; }

    /// <summary>Gets a value indicating whether device colours convert through the content's output intent profile. <see langword="true"/> unless changed.</summary>
    /// <remarks>ISO 32000-2 §14.11.5: output intents are for information, and a processor is free to disregard them.</remarks>
    public bool UsesOutputIntents { get; }

    /// <summary>Returns an engine that converts to another RGB profile.</summary>
    /// <param name="profile">An RGB profile with matrix/TRC tags or BToA tags.</param>
    /// <returns>A new engine with the same fallback and settings.</returns>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is not an RGB profile that colours can convert to.</exception>
    /// <remarks>ISO 32000-2 §10.3; ICC.1:2022 Annex F.1 and F.3 (the inverted matrix/TRC model) or §10.10, §10.11 and §10.13 (BToA).</remarks>
    public IccColorManagement WithRgbProfile(IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Header.DataColorSpace != "RGB " || !profile.CanBeDestination(RenderingIntent.RelativeColorimetric))
        {
            throw new ArgumentException("The profile shall be an RGB profile with matrix/TRC or BToA tags.", nameof(profile));
        }

        return new IccColorManagement(Fallback, profile, CompensatesBlackPointByDefault, UsesOutputIntents);
    }

    /// <summary>Returns an engine that decides black point compensation the other way when the graphics state leaves it to the processor.</summary>
    /// <param name="compensate">Whether to compensate when <c>UseBlackPtComp</c> is Default or absent.</param>
    /// <returns>A new engine with the same fallback and settings.</returns>
    /// <remarks>ISO 32000-2 §8.6.5.9.</remarks>
    public IccColorManagement WithDefaultBlackPointCompensation(bool compensate) =>
        new(Fallback, RgbProfile, compensate, UsesOutputIntents);

    /// <summary>Returns an engine that uses or disregards output intents.</summary>
    /// <param name="use">Whether device colours convert through the content's output intent profile.</param>
    /// <returns>A new engine with the same fallback and settings.</returns>
    /// <remarks>ISO 32000-2 §14.11.5.</remarks>
    public IccColorManagement WithOutputIntents(bool use) => new(Fallback, RgbProfile, CompensatesBlackPointByDefault, use);

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §8.6.5.5, §8.6.5.8, §8.6.5.9, §10.3 and §14.11.5.</remarks>
    public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfIccBasedColorSpace? output = UsesOutputIntents ? Usable(conversion.OutputIntent?.DestinationProfile) : null;
        PdfIccBasedColorSpace? icc = source as PdfIccBasedColorSpace;
        if (icc is null && output is not null && Characterizes(output, source))
        {
            if (TargetFamily(conversion.Target) == source.Family)
            {
                // Device colours on the device the output intent describes are already that device's colours.
                return Fallback.CreateConverter(source, conversion);
            }

            icc = output;
        }

        if (Usable(icc) is null)
        {
            return Fallback.CreateConverter(source, conversion);
        }

        ProfileEntry entry = Find(icc!.GetProfileData().Span);
        if (entry.Profile is not { Model: not IccProfileModel.Other } profile)
        {
            if (entry.Problem is { } problem)
            {
                icc.ReportProfileInvalid($"An ICC profile cannot be used: {problem}; the alternate colour space is used.");
            }

            return Fallback.CreateConverter(source, conversion);
        }

        if (profile.Warning is { } warning)
        {
            icc.ReportProfileInvalid($"An ICC profile tag cannot be used: {warning}; the profile's other tags are used.");
        }

        RenderingIntent intent = conversion.Intent;
        bool compensate = intent != RenderingIntent.AbsoluteColorimetric && conversion.BlackPointCompensation switch
        {
            BlackPointCompensation.On => true,
            BlackPointCompensation.Off => false,
            _ => CompensatesBlackPointByDefault,
        };

        if (conversion.Target == DeviceColorModel.Cmyk && output is not null && OutputProfile(output, intent) is { } device)
        {
            if (ReferenceEquals(device, profile))
            {
                // The source is described by the output intent's own profile: its colours are the device's.
                return Fallback.CreateConverter(PdfDeviceCmykColorSpace.Instance, conversion);
            }

            return new IccConverter(entry.Transform(device, intent, compensate), fromRgb: null);
        }

        IColorConverter? fromRgb = conversion.Target == DeviceColorModel.Rgb ? null : Fallback.CreateConverter(PdfDeviceRgbColorSpace.Instance, conversion);
        return new IccConverter(entry.Transform(RgbProfile, intent, compensate), fromRgb);
    }

    /// <summary>The space when its header is one the engine converts (gray, RGB or CMYK, version 2 to 4, N as the header says).</summary>
    private static PdfIccBasedColorSpace? Usable(PdfIccBasedColorSpace? space) =>
        space is { ProfileHeader: { IsSupportedForPdf: true, DataColorSpace: "GRAY" or "RGB " or "CMYK" } header } && header.ComponentCount == space.ComponentCount
            ? space
            : null;

    /// <summary>Whether an output intent's profile describes a device space's colours: same kind (§14.11.5).</summary>
    private static bool Characterizes(PdfIccBasedColorSpace output, PdfColorSpace source) => (source.Family, output.ProfileHeader!.DataColorSpace) switch
    {
        (PdfColorSpaceFamily.DeviceCmyk, "CMYK") or (PdfColorSpaceFamily.DeviceRgb, "RGB ") or (PdfColorSpaceFamily.DeviceGray, "GRAY") => true,
        _ => false,
    };

    private static PdfColorSpaceFamily TargetFamily(DeviceColorModel target) => target switch
    {
        DeviceColorModel.Gray => PdfColorSpaceFamily.DeviceGray,
        DeviceColorModel.Cmyk => PdfColorSpaceFamily.DeviceCmyk,
        _ => PdfColorSpaceFamily.DeviceRgb,
    };

    /// <summary>The output intent's profile as a CMYK destination for the intent, when it is one with BToA tags.</summary>
    private IccProfile? OutputProfile(PdfIccBasedColorSpace output, RenderingIntent intent)
    {
        if (output.ProfileHeader!.DataColorSpace != "CMYK")
        {
            return null;
        }

        ProfileEntry entry = Find(output.GetProfileData().Span);
        return entry.Profile is { } profile && profile.SelectBToA(intent) is not null ? profile : null;
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

    /// <summary>A read profile, or why it cannot be read, and its transforms by destination, intent and compensation, built on first use.</summary>
    private sealed class ProfileEntry(IccProfile? profile, string? problem)
    {
        private readonly ConcurrentDictionary<(IccProfile Destination, RenderingIntent Intent, bool Compensate), IccTransform> _transforms = new();

        public IccProfile? Profile => profile;

        public string? Problem => problem;

        public IccTransform Transform(IccProfile destination, RenderingIntent intent, bool compensate) =>
            _transforms.GetOrAdd((destination, intent, compensate), static (key, source) => IccTransform.Create(source, key.Destination, key.Intent, key.Compensate), profile!);
    }
}
