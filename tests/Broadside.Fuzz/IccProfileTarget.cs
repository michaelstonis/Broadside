using Broadside.Graphics;
using Broadside.Rendering.Icc;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>icc-profile</c> target (issues #117 and #118): the input is an ICC profile. It is read with <see cref="IccProfile.TryParse"/>;
/// a profile with tags to convert by (matrix/TRC, monochrome or lookup tables) is then built into its transform (media-relative, or
/// ICC-absolute for inputs of odd length) and converts a fixed set of colours, out-of-range and NaN components included, and the
/// same colours go to the connection space and back through the BToA tags. Invariants: no exception; a refused profile says why;
/// every converted sRGB component and every device component from the connection space is a number in [0, 1].
/// </summary>
/// <remarks>ICC.1:2022 §7.2, §7.3, §8.10.2, §10.6, §10.10 to §10.13, §10.18, §10.22, §10.31, Annex F.2 and F.3; ISO 32000-2 §8.6.5.5.</remarks>
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

        RenderingIntent intent = data.Length % 2 == 0 ? RenderingIntent.RelativeColorimetric : RenderingIntent.AbsoluteColorimetric;
        IccTransform transform = IccTransform.Create(profile, intent);
        int count = Samples.Length / transform.InputCount;
        Span<float> rgb = stackalloc float[3 * Samples.Length];
        transform.Convert(Samples, rgb, count);
        InUnitRange(rgb[..(3 * count)]);

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
