using Broadside.Graphics;
using Broadside.Rendering.Icc;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>icc-profile</c> target (issues #117, #118 and #119): the input is an ICC profile. It is read with <see cref="IccProfile.TryParse"/>;
/// a profile with tags to convert by (matrix/TRC, monochrome or lookup tables) is then built into its transform to sRGB (the rendering
/// intent and black point compensation chosen by the input's length) and converts a fixed set of colours, out-of-range and NaN
/// components included; a profile with BToA tags is also the destination of a transform from sRGB with black point compensation
/// (issue #119); and the same colours go to the connection space and back through the BToA tags. Invariants: no exception; a refused profile says why;
/// every converted sRGB component and every device component from the connection space is a number in [0, 1].
/// </summary>
/// <remarks>ICC.1:2022 §7.2, §7.3, §8.10.2, §10.6, §10.10 to §10.13, §10.18, §10.22, §10.31, Annex F.1 to F.3; ISO 32000-2 §8.6.5.5, §8.6.5.8, §8.6.5.9.</remarks>
internal static class IccProfileTarget
{
    private static readonly float[] Samples = [0, 0, 0, 1, 1, 1, 0.5f, 0.25f, 0.75f, float.NaN, -1, 2, 0.001f, 0.999f, 0.5f, 0.3f, 0.6f, 0.9f, 0.2f, 0.1f];

    public static void Target(ReadOnlySpan<byte> data)
    {
        // A whole file is read from the profile its first 'acsp' signature belongs to (36 bytes into the header), so the unfiltered
        // profiles of icc-rgb-matrix.pdf and icc-lut.pdf seed the target in smoke mode.
        if (data.StartsWith("%PDF-"u8) && data.IndexOf("acsp"u8) is >= 36 and int signature)
        {
            data = data[(signature - 36)..];
        }

        if (!IccProfile.TryParse(data, out IccProfile? profile, out string? problem))
        {
            if (string.IsNullOrEmpty(problem))
            {
                throw new InvalidOperationException("A refused profile gave no reason.");
            }

            return;
        }

        if (profile.Model == IccProfileModel.Other)
        {
            return;
        }

        var intent = (RenderingIntent)(data.Length % 4);
        bool compensate = data.Length % 8 >= 4;
        IccTransform transform = IccTransform.Create(profile, IccProfile.Srgb, intent, compensate);
        int count = Samples.Length / transform.InputCount;
        Span<float> rgb = stackalloc float[3 * Samples.Length];
        transform.Convert(Samples, rgb, count);
        InUnitRange(rgb[..(3 * count)]);

        // A profile with BToA tags is also a destination, as an output intent is: the black point search and the BToA tables run.
        if (profile.SelectBToA(intent) is not null)
        {
            IccTransform toProfile = IccTransform.Create(IccProfile.Srgb, profile, intent, blackPointCompensation: true);
            Span<float> output = stackalloc float[toProfile.OutputCount * 6];
            toProfile.Convert(Samples.AsSpan(0, 18), output, 6);
            InUnitRange(output);
        }

        Span<float> pcs = stackalloc float[3];
        Span<float> device = stackalloc float[16];
        for (int i = 0; i + transform.InputCount <= Samples.Length; i += transform.InputCount)
        {
            if (profile.TryConvertToConnectionSpace(Samples.AsSpan(i, transform.InputCount), pcs, intent)
                && profile.TryConvertFromConnectionSpace(pcs, device, intent))
            {
                InUnitRange(device[..transform.InputCount]);
            }
        }
    }

    private static void InUnitRange(ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            if (!(value is >= 0 and <= 1))
            {
                throw new InvalidOperationException($"A converted component is {value}, outside [0, 1].");
            }
        }
    }
}
