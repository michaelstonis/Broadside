using Broadside.Graphics;
using Broadside.Rendering.Icc;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>icc-profile</c> target (issue #117): the input is an ICC profile. It is read with <see cref="IccProfile.TryParse"/>; a
/// matrix/TRC or monochrome profile is then built into its transform (media-relative, or ICC-absolute for inputs of odd length)
/// and converts a fixed set of colours, out-of-range and NaN components included. Invariants: no exception; a refused profile
/// says why; every converted component is a number in [0, 1].
/// </summary>
/// <remarks>ICC.1:2022 §7.2, §7.3, §10.6, §10.18, §10.31, Annex F.2 and F.3; ISO 32000-2 §8.6.5.5.</remarks>
internal static class IccProfileTarget
{
    private static readonly float[] Samples = [0, 0, 0, 1, 1, 1, 0.5f, 0.25f, 0.75f, float.NaN, -1, 2, 0.001f, 0.999f, 0.5f];

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (!IccProfile.TryParse(data, out IccProfile? profile, out string? problem))
        {
            if (string.IsNullOrEmpty(problem))
            {
                throw new InvalidOperationException("A refused profile gave no reason.");
            }

            return;
        }

        if (profile.Model is not (IccProfileModel.MatrixTrc or IccProfileModel.Monochrome))
        {
            return;
        }

        IccTransform transform = IccTransform.Create(profile, data.Length % 2 == 0 ? RenderingIntent.RelativeColorimetric : RenderingIntent.AbsoluteColorimetric);
        int count = Samples.Length / transform.InputCount;
        Span<float> rgb = stackalloc float[3 * Samples.Length];
        transform.Convert(Samples, rgb, count);
        foreach (float value in rgb[..(3 * count)])
        {
            if (!(value is >= 0 and <= 1))
            {
                throw new InvalidOperationException($"A converted component is {value}, outside [0, 1].");
            }
        }
    }
}
