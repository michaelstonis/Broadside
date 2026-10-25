using Broadside.Images;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>platform-image-eligibility</c> target (issue #372): the input is JPEG data, read by the rule that decides whether a platform
/// image decoder may be offered it, against the dictionary sizes its own frame header declares (and once against other sizes), then
/// cleaned of its Exif and ICC payloads. Invariants: an eligible request has 1 or 3 components of 8 bits and the dictionary's size;
/// cleaning keeps the length and every marker, so the cleaned data gets the same answer with the same facts.
/// </summary>
/// <remarks>ITU-T T.81 Annex B; ISO 32000-2 §7.4.8 Table 13 and §8.9.5.1 Table 87; ICC.1 Annex B.4.</remarks>
internal static class PlatformImageEligibilityTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        int colorTransform = data.Length > 2 ? (data[^1] % 3) - 1 : -1;
        bool eligible = PlatformImageEligibility.TryCreateRequest(data, colorTransform, 0, 0, out _, out string? reason);
        if (eligible || reason is null)
        {
            throw new InvalidOperationException("A 0 x 0 dictionary size was accepted, or a refusal gave no reason.");
        }

        // The frame's own size, when the rule got as far as comparing it.
        int width = 75;
        int height = 43;
        if (reason.StartsWith("the frame is ", StringComparison.Ordinal) && reason.Contains(" and the image dictionary says", StringComparison.Ordinal))
        {
            string[] size = reason["the frame is ".Length..reason.IndexOf(" and", StringComparison.Ordinal)].Split(" x ");
            width = int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture);
            height = int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        eligible = PlatformImageEligibility.TryCreateRequest(data, colorTransform, width, height, out PlatformImageRequest request, out _);
        if (eligible && (request.Components is not (1 or 3) || request.BitsPerComponent != 8 || request.Width != width || request.Height != height || request.Reduction != 0))
        {
            throw new InvalidOperationException($"An eligible request broke the rule: {request}.");
        }

        byte[] cleaned = data.ToArray();
        PlatformImageEligibility.Sanitize(cleaned, exif: true, icc: true);
        bool again = PlatformImageEligibility.TryCreateRequest(cleaned, colorTransform, width, height, out PlatformImageRequest cleanedRequest, out _);
        if (again != eligible || (eligible && cleanedRequest != request))
        {
            throw new InvalidOperationException("Zero-filling the Exif and ICC payloads changed what the rule decided.");
        }
    }
}
